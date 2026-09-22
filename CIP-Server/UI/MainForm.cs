// ============================================================================
//  CIP-Server — Crestron CIP 协议模拟服务器（MIT License · 代码由 AI 辅助生成）
//  文件: UI/MainForm.cs
//  说明: 主窗体（纯代码构建，无 Designer 文件），Fluent 风格浅色卡片布局：
//        1) 顶部卡片: 标题/副标题 + 状态胶囊 + 服务器参数行（型号/固件/主机名/端口）
//                     + 信息条（运行状态详情）
//        2) 客户端卡片: 每秒刷新（uiTimer）的客户端表格
//                      （远程地址 / IPID / 设备名 / 状态 / 协议模式 / 最后活动）
//        3) 主体区左右两栏:
//           左 — 分段式选项卡卡片: 数字（高亮切换）/模拟（进度条）/串口（文本）
//           右 — 深色日志卡片: TX/RX 分色渲染，可开关十六进制与自动滚动
//  视觉: 统一由 UiTheme 提供配色/字体；卡片为圆角 + 细边框（CardPanel），
//        按钮为自绘圆角样式（FlatButton），页签为分段控件（SegmentedTabs）。
//  线程约定: 网络线程的日志/会话事件经 BeginInvoke 封送到 UI 线程后再操作控件。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using CipServerSim.Logging;
using CipServerSim.Server;
using CipServerSim.State;

namespace CipServerSim.UI
{
    /// <summary>主窗体：服务器控制 / 客户端列表 / 数字·模拟·串口状态监视 / 报文日志</summary>
    public sealed class MainForm : Form
    {
        const int HeaderHeight = 132;
        const int ClientHeight = 174;
        const int ControlRow = 40;

        readonly DeviceConfig config = DeviceConfig.LoadDefault();
        JoinState joins;
        CipServer server;
        Timer uiTimer;

        /// <summary>客户端列表快照签名：与上次相同则跳过重建行，避免每秒重绘闪烁</summary>
        string clientSignature = "";

        FlatButton btnStartStop;
        ComboBox cboModel;
        TextBox txtFirmware;
        TextBox txtHostName;
        NumericUpDown numPort94;
        NumericUpDown numPort95;
        CheckBox chkPort95;
        FlatButton btnSaveConfig;
        FlatButton btnIpTable;
        StatusPill pillStatus;
        Label lblInfo;
        BufferedGrid gridClients;
        DigitalJoinPanel pnlDigital;
        AnalogJoinPanel pnlAnalog;
        SerialJoinPanel pnlSerial;
        ToolStripButton btnHexToggle;
        ToolStripButton btnScrollToggle;
        RichTextBox rtbLog;

        public MainForm()
        {
            Text = "Crestron CIP 协议模拟服务器 · CIP-Server";
            Font = UiTheme.FontBase;
            BackColor = UiTheme.WindowBack;
            DoubleBuffered = true;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1360, 800);
            MinimumSize = new Size(1160, 700);

            BuildUi();
            LoadConfigToUi();

            Log.InitFile(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cip.log"));
            Log.Line += OnLogLine;
            if (!string.IsNullOrEmpty(config.LoadWarning)) Log.Info("× " + config.LoadWarning);
            Log.Info(string.Format("CIP-Server 模拟器已就绪 · 当前型号 {0} · 固件 {1}", config.Model, config.Firmware));
            Log.Info(string.Format("IP 表: {0}", config.IpTableSummary()));
            UpdateHexDisplay();

            uiTimer = new Timer { Interval = 1000 };
            uiTimer.Tick += delegate { if (server != null && server.Running) RefreshClients(); };
            uiTimer.Start();

            // 命令行参数 autostart：启动后自动开启服务器（便于自动化测试）
            foreach (var arg in Environment.GetCommandLineArgs())
            {
                if (string.Equals(arg, "autostart", StringComparison.OrdinalIgnoreCase))
                {
                    Load += delegate { StartServer(); };
                    break;
                }
            }
        }

        void BuildUi()
        {
            pnlDigital = new DigitalJoinPanel();
            pnlAnalog = new AnalogJoinPanel();
            pnlSerial = new SerialJoinPanel();

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                BackColor = UiTheme.WindowBack,
                Padding = new Padding(12, 12, 12, 12)
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, HeaderHeight + 10));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, ClientHeight + 10));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            root.Controls.Add(BuildHeaderCard(), 0, 0);
            root.Controls.Add(BuildClientCard(), 0, 1);
            root.Controls.Add(BuildBody(), 0, 2);

            Controls.Add(root);
        }

        /// <summary>顶部卡片：标题/副标题 + 状态胶囊 + 参数控制行 + 信息条</summary>
        Control BuildHeaderCard()
        {
            var card = UiTheme.Card(UiTheme.CardBack, UiTheme.WindowBack, 10);
            card.Dock = DockStyle.Fill;
            card.Padding = new Padding(16, 12, 16, 12);
            card.Margin = new Padding(0, 0, 0, 10);

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = UiTheme.CardBack };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, ControlRow));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            // ---- 标题行 ----
            var head = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = UiTheme.CardBack };
            head.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            head.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var titleBox = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                WrapContents = false,
                BackColor = UiTheme.CardBack,
                Margin = new Padding(0)
            };
            var title = UiTheme.Title("CIP-Server");
            title.Margin = new Padding(0, 0, 12, 0);
            var subtitle = UiTheme.Caption("Crestron CIP 协议模拟服务器 · IPID / Join 状态模拟（无需真实中控）");
            subtitle.Margin = new Padding(0, 14, 0, 0);
            titleBox.Controls.Add(title);
            titleBox.Controls.Add(subtitle);

            pillStatus = new StatusPill { Anchor = AnchorStyles.Right, Margin = new Padding(12, 0, 0, 0) };
            pillStatus.SetState(false, "已停止");

            head.Controls.Add(titleBox, 0, 0);
            head.Controls.Add(pillStatus, 1, 0);

            // ---- 参数控制行 ----
            var flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                WrapContents = false,
                BackColor = UiTheme.CardBack,
                Margin = new Padding(0)
            };
            btnStartStop = new FlatButton { Kind = ButtonKind.Primary, Text = "启动服务器", Width = 116 };
            btnStartStop.Height = 34;
            btnStartStop.Click += BtnStartStop_Click;
            flow.Controls.Add(VerticalCenter(btnStartStop, ControlRow, 4, 0));

            cboModel = new ComboBox { Width = 116, DropDownStyle = ComboBoxStyle.DropDown };
            cboModel.Items.AddRange(new object[] { "CP4", "CP3", "CP3N", "MC4", "MC3", "MC2S", "PRO3", "RMC4", "RMC3", "DINAP3" });
            UiTheme.StyleInput(cboModel);
            AddField(flow, "型号", cboModel);

            txtFirmware = new TextBox { Width = 116 };
            UiTheme.StyleInput(txtFirmware);
            AddField(flow, "固件", txtFirmware);

            txtHostName = new TextBox { Width = 100 };
            UiTheme.StyleInput(txtHostName);
            AddField(flow, "主机名", txtHostName);

            numPort94 = new NumericUpDown { Minimum = 1024, Maximum = 65535, Width = 70 };
            UiTheme.StyleInput(numPort94);
            AddField(flow, "raw 端口", numPort94);

            numPort95 = new NumericUpDown { Minimum = 1024, Maximum = 65535, Width = 70 };
            UiTheme.StyleInput(numPort95);
            AddField(flow, "packetized 端口", numPort95);

            chkPort95 = new CheckBox { Text = "启用 41795", AutoSize = true };
            UiTheme.StyleInput(chkPort95);
            flow.Controls.Add(VerticalCenter(chkPort95, ControlRow, 10, 0));

            btnSaveConfig = new FlatButton { Kind = ButtonKind.Secondary, Text = "保存配置", Width = 92 };
            btnSaveConfig.Click += BtnSaveConfig_Click;
            flow.Controls.Add(VerticalCenter(btnSaveConfig, ControlRow, 14, 0));

            btnIpTable = new FlatButton { Kind = ButtonKind.Secondary, Text = "IP 表…", Width = 84 };
            btnIpTable.Click += BtnIpTable_Click;
            flow.Controls.Add(VerticalCenter(btnIpTable, ControlRow, 6, 0));

            // ---- 信息条 ----
            var strip = new CardPanel
            {
                Dock = DockStyle.Fill,
                BackColor = UiTheme.CardBack,
                CardColor = UiTheme.TrackBack,
                Radius = 8,
                ShowCardBorder = false,
                Margin = new Padding(0),
                Padding = new Padding(14, 0, 14, 0)
            };
            lblInfo = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = UiTheme.FontSmall,
                ForeColor = UiTheme.TextSecondary,
                BackColor = UiTheme.TrackBack,
                Text = "已停止 · 点击【启动服务器】开始模拟"
            };
            strip.Controls.Add(lblInfo);

            layout.Controls.Add(head, 0, 0);
            layout.Controls.Add(flow, 0, 1);
            layout.Controls.Add(strip, 0, 2);
            card.Controls.Add(layout);
            return card;
        }

        /// <summary>FlowLayoutPanel 不支持 Anchor，用 Margin 让控件在行内垂直居中</summary>
        static Control VerticalCenter(Control c, int rowHeight, int leftMargin, int rightMargin)
        {
            c.Margin = new Padding(leftMargin, Math.Max(0, (rowHeight - c.Height) / 2), rightMargin, 0);
            return c;
        }

        /// <summary>向控制行追加「标签 + 输入控件」组合（两者垂直居中）</summary>
        void AddField(FlowLayoutPanel flow, string label, Control input)
        {
            var lbl = UiTheme.FieldLabel(label);
            lbl.Margin = new Padding(14, Math.Max(0, (ControlRow - lbl.Height) / 2), 5, 0);
            flow.Controls.Add(lbl);
            flow.Controls.Add(VerticalCenter(input, ControlRow, 0, 0));
        }

        /// <summary>客户端卡片：分节标题 + 客户端表格（每秒刷新）</summary>
        Control BuildClientCard()
        {
            var card = UiTheme.Card(UiTheme.CardBack, UiTheme.WindowBack, 10);
            card.Dock = DockStyle.Fill;
            card.Padding = new Padding(14, 12, 14, 12);
            card.Margin = new Padding(0, 0, 0, 10);

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = UiTheme.CardBack };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var head = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = UiTheme.CardBack };
            head.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            head.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var title = UiTheme.SectionTitle("客户端连接");
            title.Margin = new Padding(0, 3, 0, 0);
            var hint = UiTheme.Caption("Who-Is → Sign-On → 注册回放 · 每秒刷新 · IPID 不在 IP 表内将被拒绝");
            hint.Margin = new Padding(0, 5, 0, 0);
            head.Controls.Add(title, 0, 0);
            head.Controls.Add(hint, 1, 0);

            gridClients = new BufferedGrid { Dock = DockStyle.Fill, Margin = new Padding(0) };
            UiTheme.StyleGrid(gridClients, true);
            gridClients.Columns.Add(Column("remote", "远程地址", 88));
            gridClients.Columns.Add(Column("ipid", "IPID", 52));
            gridClients.Columns.Add(Column("name", "设备名", 130));
            gridClients.Columns.Add(Column("state", "状态", 78));
            gridClients.Columns.Add(Column("mode", "协议模式", 108));
            gridClients.Columns.Add(Column("activity", "最后活动", 92));
            gridClients.Paint += delegate(object s, PaintEventArgs pe)
            {
                if (gridClients.Rows.Count > 0) return;
                TextRenderer.DrawText(pe.Graphics,
                    "暂无客户端连接 · 点击【启动服务器】后等待设备接入（41794 raw / 41795 packetized）",
                    UiTheme.FontBase, gridClients.ClientRectangle, UiTheme.TextMuted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            };

            layout.Controls.Add(head, 0, 0);
            layout.Controls.Add(gridClients, 0, 1);
            card.Controls.Add(layout);
            return card;
        }

        static DataGridViewTextBoxColumn Column(string name, string header, int fillWeight)
        {
            return new DataGridViewTextBoxColumn
            {
                Name = name,
                HeaderText = header,
                FillWeight = fillWeight,
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
        }

        /// <summary>主体区：左栏 Join 监视分段选项卡卡片 · 右栏深色报文日志卡片</summary>
        Control BuildBody()
        {
            var body = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = UiTheme.WindowBack,
                Margin = new Padding(0)
            };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 64));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36));

            body.Controls.Add(BuildJoinCard(), 0, 0);
            body.Controls.Add(BuildLogCard(), 1, 0);
            return body;
        }

        /// <summary>左栏卡片：分段式选项卡（数字 / 模拟 / 串口）</summary>
        Control BuildJoinCard()
        {
            var card = UiTheme.Card(UiTheme.CardBack, UiTheme.WindowBack, 10);
            card.Dock = DockStyle.Fill;
            card.Padding = new Padding(14, 12, 14, 12);
            card.Margin = new Padding(0, 0, 8, 0);

            // 左栏卡片：分段式选项卡（数字 / 模拟 / 串口）
            var tabs = new SegmentedTabs { Dock = DockStyle.Fill };
            tabs.AddTab("数字 Digital", pnlDigital);
            tabs.AddTab("模拟 Analog", pnlAnalog);
            tabs.AddTab("串口 Serial", pnlSerial);

            card.Controls.Add(tabs);
            return card;
        }

        /// <summary>右栏卡片：深色日志终端（标题 + 扁平工具条 + 等宽日志框）</summary>
        Control BuildLogCard()
        {
            var card = UiTheme.Card(UiTheme.LogBack, UiTheme.WindowBack, 10);
            card.CardBorder = UiTheme.LogBorder;
            card.Dock = DockStyle.Fill;
            card.Padding = new Padding(14, 12, 14, 12);
            card.Margin = new Padding(0);

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = UiTheme.LogBack };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var head = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = UiTheme.LogBack };
            head.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            head.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var title = new Label
            {
                Text = "报文日志",
                Font = UiTheme.FontBold,
                ForeColor = UiTheme.LogText,
                AutoSize = true,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 5, 0, 0)
            };
            var hint = new Label
            {
                Text = "TX/RX 分色 · 同时写入 cip.log",
                Font = UiTheme.FontSmall,
                ForeColor = UiTheme.LogMuted,
                AutoSize = true,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 7, 0, 0)
            };
            head.Controls.Add(title, 0, 0);
            head.Controls.Add(hint, 1, 0);

            var logBar = new ToolStrip { Dock = DockStyle.Top };
            btnHexToggle = new ToolStripButton { Text = "十六进制", CheckOnClick = true, Checked = true };
            btnHexToggle.Click += delegate { UpdateHexDisplay(); };
            btnScrollToggle = new ToolStripButton { Text = "自动滚动", CheckOnClick = true, Checked = true };
            var btnClear = new ToolStripButton { Text = "清空" };
            btnClear.Click += delegate { rtbLog.Clear(); };
            logBar.Items.AddRange(new ToolStripItem[] { btnHexToggle, btnScrollToggle, new ToolStripSeparator(), btnClear });
            UiTheme.StyleToolStrip(logBar, true);
            var barHost = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.LogBack };
            barHost.Controls.Add(logBar);

            rtbLog = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                BackColor = UiTheme.LogBack,
                ForeColor = UiTheme.LogText,
                Font = UiTheme.FontMono,
                DetectUrls = false,
                WordWrap = true
            };
            var logHost = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.LogBack, Padding = new Padding(2, 6, 2, 0) };
            logHost.Controls.Add(rtbLog);

            layout.Controls.Add(head, 0, 0);
            layout.Controls.Add(barHost, 0, 1);
            layout.Controls.Add(logHost, 0, 2);
            card.Controls.Add(layout);
            return card;
        }

        // ===================== 配置 =====================

        /// <summary>把内存配置反映到界面控件</summary>
        void LoadConfigToUi()
        {
            cboModel.Text = config.Model;
            txtFirmware.Text = config.Firmware;
            txtHostName.Text = config.HostName;
            numPort94.Value = ClampPort(config.Port41794);
            numPort95.Value = ClampPort(config.Port41795);
            chkPort95.Checked = config.Enable41795;
        }

        /// <summary>把端口收敛到 NumericUpDown 允许的范围（1024-65535）</summary>
        static int ClampPort(int port)
        {
            if (port < DeviceConfig.MinPort) return DeviceConfig.MinPort;
            if (port > DeviceConfig.MaxPort) return DeviceConfig.MaxPort;
            return port;
        }

        /// <summary>校验并回写界面上的服务器参数；校验不通过时弹提示并返回 false</summary>
        bool ApplyConfigFromUi()
        {
            if (string.IsNullOrWhiteSpace(cboModel.Text))
            {
                MessageBox.Show("型号不能为空", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            config.Model = cboModel.Text.Trim();
            config.Firmware = txtFirmware.Text.Trim();
            config.HostName = txtHostName.Text.Trim();
            config.Port41794 = (int)numPort94.Value;
            config.Port41795 = (int)numPort95.Value;
            config.Enable41795 = chkPort95.Checked;
            if (config.Enable41795 && config.Port41794 == config.Port41795)
            {
                MessageBox.Show("两个端口不能相同", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            return true;
        }

        void BtnSaveConfig_Click(object sender, EventArgs e)
        {
            if (!ApplyConfigFromUi()) return;
            try
            {
                config.Save(DeviceConfig.DefaultPath);
                MessageBox.Show("配置已保存到 config.json\n(型号/固件/IP表在重启服务器后生效；join 数量需重启程序)", "保存成功",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("保存失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>编辑 IP 表：确定后立即回写内存配置并落盘（编辑结果不应因忘记点保存而丢失）</summary>
        void BtnIpTable_Click(object sender, EventArgs e)
        {
            using (var dlg = new IpTableDialog())
            {
                dlg.LoadTable(config.IpTable != null ? config.IpTable.ToArray() : new IpEntry[0]);
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                config.IpTable = new List<IpEntry>(dlg.CollectTable());
                Log.Info(string.Format("IP 表已更新({0} 项): {1}", config.IpTable.Count, config.IpTableSummary()));
                try
                {
                    config.Save(DeviceConfig.DefaultPath);
                }
                catch (Exception ex)
                {
                    Log.Info("× IP 表写入 config.json 失败: " + ex.Message);
                }
                MessageBox.Show("IP 表已更新并保存到 config.json，重启服务器后生效", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        // ===================== 服务器启停 =====================

        void BtnStartStop_Click(object sender, EventArgs e)
        {
            if (server != null && server.Running) { StopServer(); return; }
            StartServer();
        }

        void StartServer()
        {
            if (!ApplyConfigFromUi()) return;
            try
            {
                config.Save(DeviceConfig.DefaultPath);   // 顺手持久化界面上的设置
            }
            catch { /* 保存失败不阻塞启动 */ }

            var newJoins = new JoinState(config);
            var newServer = new CipServer(config, newJoins);
            newServer.SessionChanged += OnSessionChanged;
            newServer.SessionRemoved += OnSessionChanged;
            try
            {
                newServer.Start();
            }
            catch (Exception ex)
            {
                // 启动失败（如端口被占用）：退订并丢弃对象，界面保持“已停止”，不留半初始化状态
                newServer.SessionChanged -= OnSessionChanged;
                newServer.SessionRemoved -= OnSessionChanged;
                MessageBox.Show("启动失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            joins = newJoins;
            server = newServer;
            pnlDigital.Init(joins, config);
            pnlAnalog.Init(joins, config);
            pnlSerial.Init(joins, config);
            clientSignature = "";
            btnStartStop.Text = "停止服务器";
            btnStartStop.Kind = ButtonKind.Danger;
            pillStatus.SetState(true, "运行中");
            lblInfo.Text = string.Format("运行中 · 型号 {0} · 固件 {1} · raw:{2}{3}",
                config.Model, config.Firmware, config.Port41794,
                config.Enable41795 ? string.Format(" · packetized:{0}", config.Port41795) : "");
        }

        void StopServer()
        {
            if (server != null)
            {
                server.SessionChanged -= OnSessionChanged;
                server.SessionRemoved -= OnSessionChanged;
                server.Stop();
            }
            server = null;
            joins = null;
            clientSignature = "";
            pnlDigital.Shutdown();
            pnlAnalog.Shutdown();
            pnlSerial.Shutdown();
            btnStartStop.Text = "启动服务器";
            btnStartStop.Kind = ButtonKind.Primary;
            pillStatus.SetState(false, "已停止");
            lblInfo.Text = "已停止 · 点击【启动服务器】开始模拟";
            RefreshClients();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (uiTimer != null)
            {
                uiTimer.Stop();
                uiTimer.Dispose();   // 定时器持续引用窗体，不释放会阻止窗体回收
            }
            if (server != null && server.Running) server.Stop();
            Log.Line -= OnLogLine;   // 先退订，避免关闭过程中仍有日志回调
            base.OnFormClosing(e);
        }

        // ===================== 客户端列表 =====================

        void OnSessionChanged(CipSession s)
        {
            if (!IsHandleCreated) return;
            try { BeginInvoke((MethodInvoker)delegate { RefreshClients(); }); }
            catch (ObjectDisposedException) { }
        }

        /// <summary>刷新客户端表格：先比对快照签名，无变化直接返回，避免每秒重绘造成闪烁</summary>
        void RefreshClients()
        {
            if (gridClients.IsDisposed) return;
            CipSession[] sessions = (server != null && server.Running)
                ? server.GetSessionsSnapshot() : new CipSession[0];

            var builder = new StringBuilder();
            foreach (var s in sessions)
                builder.Append(s.RemoteAddr).Append('|').Append(s.HasIpid ? s.Ipid : -1).Append('|')
                       .Append((int)s.State).Append('|').Append((int)s.FramingMode).Append('|')
                       .Append(s.LastActivity.ToString("HH:mm:ss")).Append(';');
            string signature = builder.ToString();
            if (signature == clientSignature) return;
            clientSignature = signature;

            gridClients.Rows.Clear();
            foreach (var s in sessions)
            {
                var entry = s.HasIpid ? server.Config.FindIpEntry(s.Ipid) : null;
                int index = gridClients.Rows.Add(s.RemoteAddr,
                    s.HasIpid ? string.Format("0x{0:X2}", s.Ipid) : "-",
                    entry != null ? (entry.Name ?? "") : (s.HasIpid ? "未在 IP 表" : "-"),
                    StateText(s.State),
                    s.FramingMode == Framing.Packetized ? "packetized 41795" : "raw 41794",
                    s.LastActivity.ToString("HH:mm:ss"));
                var row = gridClients.Rows[index];
                row.Cells[3].Style.ForeColor = StateColor(s.State);
                row.Cells[3].Style.Font = UiTheme.FontBold;
                if (s.State == SessionState.Refused) row.DefaultCellStyle.BackColor = UiTheme.DangerSoft;
            }
        }

        static string StateText(SessionState st)
        {
            switch (st)
            {
                case SessionState.Connecting: return "等待注册";
                case SessionState.Registered: return "已注册";
                case SessionState.Refused: return "被拒绝";
                default: return "已断开";
            }
        }

        static Color StateColor(SessionState st)
        {
            switch (st)
            {
                case SessionState.Registered: return UiTheme.Success;
                case SessionState.Refused: return UiTheme.Danger;
                case SessionState.Connecting: return UiTheme.Warning;
                default: return UiTheme.TextMuted;
            }
        }

        // ===================== 日志 =====================

        void OnLogLine(string line)
        {
            if (!IsHandleCreated || IsDisposed) return;
            try { BeginInvoke((MethodInvoker)delegate { AppendLog(line); }); }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }

        void AppendLog(string line)
        {
            if (rtbLog.IsDisposed) return;
            Color color = UiTheme.LogText;
            if (line.Contains("TX→")) color = UiTheme.LogTx;
            else if (line.Contains("RX←")) color = UiTheme.LogRx;
            else if (line.Contains("×")) color = UiTheme.LogError;
            else if (line.Contains("√")) color = UiTheme.LogOk;
            rtbLog.SelectionStart = rtbLog.TextLength;
            rtbLog.SelectionColor = color;
            rtbLog.AppendText(line + Environment.NewLine);
            if (btnScrollToggle.Checked)
            {
                rtbLog.SelectionStart = rtbLog.TextLength;
                rtbLog.ScrollToCaret();
            }
            if (rtbLog.TextLength > 400000)
            {
                int cut = rtbLog.GetFirstCharIndexFromLine(200);
                if (cut > 0)
                {
                    rtbLog.ReadOnly = false;
                    rtbLog.Select(0, cut);
                    rtbLog.SelectedText = "";
                    rtbLog.ReadOnly = true;
                }
            }
        }

        void UpdateHexDisplay()
        {
            Log.ShowHex = btnHexToggle.Checked;
        }
    }
}
