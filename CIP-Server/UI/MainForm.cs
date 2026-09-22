// ============================================================================
//  CIP-Server — Crestron CIP 协议模拟服务器（MIT License · 代码由 AI 辅助生成）
//  文件: UI/MainForm.cs
//  说明: 主窗体（纯代码构建，无 Designer 文件），自上而下:
//        1) 服务器控制条: 启动/停止、型号/固件/主机名/端口、IP 表、保存配置（整行宽度）
//        2) 客户端列表: 每秒刷新（uiTimer），显示 IPID/状态/协议模式等（整行宽度）
//        3) 主体区（左右两栏）:
//           左 — Join 监视选项卡: 数字（高亮切换）/模拟（进度条）/串口（文本）
//           右 — 报文日志: TX/RX 分色渲染，可开关十六进制与自动滚动
//  线程约定: 网络线程的日志/会话事件经 BeginInvoke 封送到 UI 线程后再操作控件。
// ============================================================================

using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using CipServerSim.Logging;
using CipServerSim.Server;
using CipServerSim.State;

namespace CipServerSim.UI
{
    /// <summary>主窗体：服务器控制 / 客户端列表 / 数字·模拟·串口状态监视 / 报文日志</summary>
    public sealed class MainForm : Form
    {
        readonly DeviceConfig config = DeviceConfig.LoadDefault();
        JoinState joins;
        CipServer server;
        Timer uiTimer;

        Button btnStartStop;
        ComboBox cboModel;
        TextBox txtFirmware;
        TextBox txtHostName;
        NumericUpDown numPort94;
        NumericUpDown numPort95;
        CheckBox chkPort95;
        Button btnSaveConfig;
        Button btnIpTable;
        Label lblStatus;
        ListView lvClients;
        TabControl tabs;
        DigitalJoinPanel pnlDigital;
        AnalogJoinPanel pnlAnalog;
        SerialJoinPanel pnlSerial;
        ToolStripButton btnHexToggle;
        ToolStripButton btnScrollToggle;
        RichTextBox rtbLog;

        public MainForm()
        {
            Text = "Crestron CIP 协议模拟服务器 · CIP-Server";
            Font = new Font("Microsoft YaHei UI", 9F);
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1280, 780);
            MinimumSize = new Size(1000, 660);

            BuildUi();
            LoadConfigToUi();

            Log.InitFile(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cip.log"));
            Log.Line += OnLogLine;
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

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 84));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 172));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            // ---- 顶部控制区 ----
            var topPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 6, 8, 2) };
            var flow = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 48, WrapContents = false };
            btnStartStop = new Button
            {
                Text = "启动服务器",
                Width = 116,
                Height = 32,
                BackColor = Color.FromArgb(0, 122, 204),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnStartStop.FlatAppearance.BorderSize = 0;
            btnStartStop.Click += BtnStartStop_Click;
            flow.Controls.Add(btnStartStop);
            flow.Controls.Add(MakeLabel(" 型号:"));
            cboModel = new ComboBox { Width = 130, DropDownStyle = ComboBoxStyle.DropDown };
            cboModel.Items.AddRange(new object[] { "CP4", "CP3", "CP3N", "MC4", "MC3", "MC2S", "PRO3", "RMC4", "RMC3", "DINAP3" });
            flow.Controls.Add(cboModel);
            flow.Controls.Add(MakeLabel(" 固件:"));
            txtFirmware = new TextBox { Width = 130 };
            flow.Controls.Add(txtFirmware);
            flow.Controls.Add(MakeLabel(" 主机名:"));
            txtHostName = new TextBox { Width = 110 };
            flow.Controls.Add(txtHostName);
            flow.Controls.Add(MakeLabel(" 端口41794:"));
            numPort94 = new NumericUpDown { Minimum = 1024, Maximum = 65535, Width = 74 };
            flow.Controls.Add(numPort94);
            flow.Controls.Add(MakeLabel(" 端口41795:"));
            numPort95 = new NumericUpDown { Minimum = 1024, Maximum = 65535, Width = 74 };
            flow.Controls.Add(numPort95);
            chkPort95 = new CheckBox { Text = "启用", AutoSize = true, Checked = true, Margin = new Padding(4, 10, 4, 0) };
            flow.Controls.Add(chkPort95);
            btnSaveConfig = new Button { Text = "保存配置", Width = 92, Height = 32 };
            btnSaveConfig.Click += BtnSaveConfig_Click;
            flow.Controls.Add(btnSaveConfig);
            btnIpTable = new Button { Text = "IP 表…", Width = 76, Height = 32 };
            btnIpTable.Click += BtnIpTable_Click;
            flow.Controls.Add(btnIpTable);

            lblStatus = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.FromArgb(230, 240, 255),
                Padding = new Padding(8, 0, 0, 0),
                Text = "已停止 · 点击【启动服务器】开始模拟"
            };
            topPanel.Controls.Add(lblStatus);
            topPanel.Controls.Add(flow);
            root.Controls.Add(topPanel, 0, 0);

            // ---- 客户端列表 ----
            var clientPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 2, 8, 4) };
            var clientLabel = new Label { Dock = DockStyle.Top, Height = 22, Text = "客户端连接:" };
            lvClients = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true };
            lvClients.Columns.Add("远程地址", 150);
            lvClients.Columns.Add("IPID", 60);
            lvClients.Columns.Add("设备名", 150);
            lvClients.Columns.Add("状态", 90);
            lvClients.Columns.Add("协议模式", 130);
            lvClients.Columns.Add("最后活动", 140);
            clientPanel.Controls.Add(lvClients);
            clientPanel.Controls.Add(clientLabel);
            root.Controls.Add(clientPanel, 0, 1);

            // ---- Join 监视选项卡（放入主体区左栏）----
            tabs = new TabControl { Dock = DockStyle.Fill };
            tabs.TabPages.Add(MakeTab("数字 (Digital)", pnlDigital));
            tabs.TabPages.Add(MakeTab("模拟 (Analog)", pnlAnalog));
            tabs.TabPages.Add(MakeTab("串口 (Serial)", pnlSerial));

            // ---- 日志区（放入主体区右栏）----
            var logPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 4, 8, 8) };
            var logBar = new ToolStrip { Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden };
            btnHexToggle = new ToolStripButton { Text = "显示报文十六进制", CheckOnClick = true, Checked = true };
            btnHexToggle.Click += delegate { UpdateHexDisplay(); };
            btnScrollToggle = new ToolStripButton { Text = "自动滚动", CheckOnClick = true, Checked = true };
            var btnClear = new ToolStripButton { Text = "清空日志" };
            btnClear.Click += delegate { rtbLog.Clear(); };
            logBar.Items.AddRange(new ToolStripItem[] { btnHexToggle, btnScrollToggle, new ToolStripSeparator(), btnClear });
            rtbLog = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.FromArgb(200, 200, 200),
                Font = new Font("Consolas", 9F),
                DetectUrls = false,
                WordWrap = true
            };
            logPanel.Controls.Add(rtbLog);
            logPanel.Controls.Add(logBar);

            // ---- 主体区：左栏 Join 监视选项卡 · 右栏 报文日志 ----
            var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 64));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36));
            body.Controls.Add(tabs, 0, 0);
            body.Controls.Add(logPanel, 1, 0);
            root.Controls.Add(body, 0, 2);

            Controls.Add(root);
        }

        static TabPage MakeTab(string title, Control content)
        {
            var tp = new TabPage(title);
            content.Dock = DockStyle.Fill;
            tp.Controls.Add(content);
            return tp;
        }

        static Label MakeLabel(string text)
        {
            return new Label { Text = text, AutoSize = true, Margin = new Padding(12, 10, 2, 0) };
        }

        // ===================== 配置 =====================

        void LoadConfigToUi()
        {
            cboModel.Text = config.Model;
            txtFirmware.Text = config.Firmware;
            txtHostName.Text = config.HostName;
            numPort94.Value = ClampPort(config.Port41794);
            numPort95.Value = ClampPort(config.Port41795);
            chkPort95.Checked = config.Enable41795;
        }

        int ClampPort(int port)
        {
            if (port < 1024) return 1024;
            if (port > 65535) return 65535;
            return port;
        }

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
            if (config.Port41794 == config.Port41795)
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

        void BtnIpTable_Click(object sender, EventArgs e)
        {
            using (var dlg = new IpTableDialog())
            {
                dlg.LoadTable(config.IpTable != null ? config.IpTable.ToArray() : new IpEntry[0]);
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    config.IpTable = new System.Collections.Generic.List<IpEntry>(dlg.CollectTable());
                    Log.Info(string.Format("IP 表已更新({0} 项): {1}", config.IpTable.Count, config.IpTableSummary()));
                    MessageBox.Show("IP 表已更新，重启服务器后生效", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
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
                config.Save(DeviceConfig.DefaultPath);
            }
            catch { /* 保存失败不阻塞启动 */ }

            joins = new JoinState(config);
            server = new CipServer(config, joins);
            server.SessionChanged += OnSessionChanged;
            server.SessionRemoved += OnSessionChanged;
            try
            {
                server.Start();
            }
            catch (Exception ex)
            {
                server = null;
                MessageBox.Show("启动失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            pnlDigital.Init(joins, config);
            pnlAnalog.Init(joins, config);
            pnlSerial.Init(joins, config);
            btnStartStop.Text = "停止服务器";
            lblStatus.Text = string.Format("运行中 · 型号 {0} · 固件 {1} · raw:{2}{3}",
                config.Model, config.Firmware, config.Port41794,
                config.Enable41795 ? string.Format(" · packetized:{0}", config.Port41795) : "");
        }

        void StopServer()
        {
            if (server != null) server.Stop();
            server = null;
            joins = null;
            pnlDigital.Shutdown();
            pnlAnalog.Shutdown();
            pnlSerial.Shutdown();
            btnStartStop.Text = "启动服务器";
            lblStatus.Text = "已停止";
            RefreshClients();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (server != null && server.Running) server.Stop();
            Log.Line -= OnLogLine;
            base.OnFormClosing(e);
        }

        // ===================== 客户端列表 =====================

        void OnSessionChanged(CipSession s)
        {
            if (!IsHandleCreated) return;
            try { BeginInvoke((MethodInvoker)delegate { RefreshClients(); }); }
            catch (ObjectDisposedException) { }
        }

        void RefreshClients()
        {
            if (lvClients.IsDisposed) return;
            lvClients.BeginUpdate();
            lvClients.Items.Clear();
            if (server != null && server.Running)
            {
                foreach (var s in server.GetSessionsSnapshot())
                {
                    var entry = s.HasIpid ? server.Config.FindIpEntry(s.Ipid) : null;
                    var item = new ListViewItem(s.RemoteAddr);
                    item.SubItems.Add(s.HasIpid ? string.Format("0x{0:X2}", s.Ipid) : "-");
                    item.SubItems.Add(entry != null ? (entry.Name ?? "") : (s.HasIpid ? "未在 IP 表" : "-"));
                    item.SubItems.Add(StateText(s.State));
                    item.SubItems.Add(s.FramingMode == Framing.Packetized ? "packetized(41795)" : "raw(41794)");
                    item.SubItems.Add(s.LastActivity.ToString("HH:mm:ss"));
                    lvClients.Items.Add(item);
                }
            }
            lvClients.EndUpdate();
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
            Color color = Color.FromArgb(200, 200, 200);
            if (line.Contains("TX→")) color = Color.FromArgb(120, 200, 120);
            else if (line.Contains("RX←")) color = Color.FromArgb(120, 170, 240);
            else if (line.Contains("×")) color = Color.FromArgb(240, 120, 120);
            else if (line.Contains("√")) color = Color.FromArgb(140, 220, 140);
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
                rtbLog.Select(0, cut);
                rtbLog.ReadOnly = false;
                rtbLog.SelectedText = "";
                rtbLog.ReadOnly = true;
            }
        }

        void UpdateHexDisplay()
        {
            Log.ShowHex = btnHexToggle.Checked;
        }
    }
}
