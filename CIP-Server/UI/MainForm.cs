// ============================================================================
//  CIP-Server — Crestron CIP 协议模拟服务器（MIT License · 代码由 AI 辅助生成）
//  文件: UI/MainForm.cs
//  说明: 主窗体（纯代码构建，无 Designer 文件），深色卡片布局：
//        1) 顶部卡片: 标题/副标题 + 状态胶囊 + 参数控制（两行：
//                     启停/型号/固件/主机名；端口/启用开关/保存/IP 表/精简模式）
//                     + 信息条（运行状态详情）
//        2) 客户端卡片: 每秒刷新（uiTimer）的客户端表格
//                      （远程地址 / IPID / 设备名 / 状态 / 协议模式 / 最后活动）
//        3) 主体区左右两栏:
//           左 — 分段式选项卡卡片: 数字（高亮切换）/模拟（进度条）/串口（文本）
//           右 — 深色日志卡片: TX/RX 分色渲染，可开关十六进制与自动滚动
//        精简模式: 一键隐藏日志/副标题/配置参数/信息条/客户端列表，窗口缩至完整模式的 1/4 面积
//                  (680x400)，Join 页签与启停/保存等按钮保留；状态持久化在 config.json (CompactMode)。
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
        // 顶部卡片高度：标题行 34 + 参数控制两行 40×2 + 信息条
        const int HeaderHeight = 172;
        // 精简模式：标题行 34 + 参数控制两行 40×2（参数控件已隐藏，仅剩按钮），无信息条
        const int CompactHeaderHeight = 138;
        const int ClientHeight = 174;
        const int ControlRow = 40;

        // 日志框字符数上限与裁剪后保留行数（超出即丢弃最旧文本，避免长时间运行后界面卡顿）
        const int LogMaxChars = 400000;
        const int LogTrimKeepLines = 200;
        // 界面日志缓冲条数（暂停/过滤/搜索/导出均基于该缓冲，超出后丢弃最旧记录）
        const int LogRingCapacity = 2000;

        // 精简模式窗口尺寸：面积约为完整模式 (1360x800) 的 1/4（按当前 DPI 换算后的物理像素）
        static readonly Size FullMinimumSize = new Size(UiTheme.Px(1160), UiTheme.Px(700));
        static readonly Size CompactClientSize = new Size(UiTheme.Px(680), UiTheme.Px(400));
        static readonly Size CompactMinimumSize = new Size(UiTheme.Px(520), UiTheme.Px(340));

        readonly DeviceConfig config = DeviceConfig.LoadDefault();
        JoinState joins;
        CipServer server;
        Timer uiTimer;

        /// <summary>客户端列表快照签名：与上次相同则跳过重建行，避免每秒重绘闪烁</summary>
        string clientSignature = "";

        // ---- 报文日志：界面侧缓冲与筛选状态（暂停期间日志仍进入缓冲，继续后可完整回放） ----
        readonly LogViewState logView = new LogViewState(LogRingCapacity);
        bool logPaused;                 // 暂停显示
        LogLineKind? logFilter;         // 类型过滤，null = 全部
        string logSearchText = "";      // 关键字过滤（不区分大小写）
        Timer logSearchTimer;           // 搜索防抖：停止输入 250ms 后重建

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
        ToolStripButton btnLogPause;
        ToolStripButton btnLogWrap;
        ComboBox cboLogFilter;
        TextBox txtLogSearch;
        Label lblLogStat;
        Label lblClientStat;
        RichTextBox rtbLog;

        // ---- 精简模式：需要整体显示/隐藏的控件引用 ----
        TableLayoutPanel rootLayout;
        TableLayoutPanel bodyLayout;
        Control cardClients;
        Control cardJoin;
        Control cardLog;
        Control infoStrip;
        Label lblSubtitle;
        FlatButton btnCompact;
        readonly List<Control> configFields = new List<Control>();
        Rectangle preCompactBounds;   // 进入精简模式前的窗口外框，切回完整模式时还原

        public MainForm()
        {
            Text = "Crestron CIP 协议模拟服务器 · CIP-Server";
            Font = UiTheme.FontBase;
            BackColor = UiTheme.WindowBack;
            DoubleBuffered = true;
            // 应用图标：取 exe 内嵌图标（csproj ApplicationIcon → app.ico，构建时嵌入）
            try { Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch (Exception) { /* 无图标资源时保持系统默认 */ }
            // 高 DPI: 尺寸仍以 96dpi 设计像素书写，构建完成后由 ApplyDpiScale() 统一等比放大。
            // AutoScaleMode 固定为 None —— 实测 .NET Framework 的 Dpi 模式对代码构建的窗体
            // 不会缩放（设计尺寸被对齐到当前 DPI），若保留会与手动缩放叠加导致双重放大。
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1360, 800);
            // 注意: 此处必须用设计像素（1160x700）——静态 FullMinimumSize 里的 Px() 在
            // 200% DPI 下立即返回物理值(2320x1400)，在缩放前设置会被 WinForms 用于
            // 强制窗体最小尺寸，把设计态布局顶大（实测: 卡片宽 4540 = 2294x2）。
            // ApplyDpiScale() 缩放完成后会重新以物理像素设置真正的最小尺寸。
            MinimumSize = new Size(1160, 700);

            BuildUi();
            LoadConfigToUi();
            ApplyDpiScale();
            ApplyCompactMode(config.CompactMode);

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
            cardClients = BuildClientCard();
            root.Controls.Add(cardClients, 0, 1);
            root.Controls.Add(BuildBody(), 0, 2);

            rootLayout = root;
            Controls.Add(root);
        }

        /// <summary>
        /// 按显示器 DPI 等比放大界面（96dpi 设计像素 → 物理像素）。
        /// Control.Scale 负责全部控件的边界/Padding/Margin 与表格绝对行列；DataGridView 行高、
        /// ToolStrip 内边距、自绘尺寸等由 UiTheme.Px 处理。缩放后重新指定最小尺寸与客户区尺寸，
        /// 避免窗体最小尺寸（含非客户区）换算带来的误差。
        /// </summary>
        void ApplyDpiScale()
        {
            UiTheme.ScaleToDpi(this);
            MinimumSize = FullMinimumSize;
            ClientSize = new Size(UiTheme.Px(1360), UiTheme.Px(800));
        }

        /// <summary>句柄创建后启用深色标题栏（DWM，Win10 1809+ 生效，其他系统静默忽略）</summary>
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            DarkTitleBar.Apply(this);
        }

        /// <summary>顶部卡片：标题/副标题 + 状态胶囊 + 两行参数控制 + 信息条</summary>
        Control BuildHeaderCard()
        {
            var card = UiTheme.Card(UiTheme.CardBack, UiTheme.WindowBack, 10);
            card.Dock = DockStyle.Fill;
            card.Padding = new Padding(16, 12, 16, 12);
            card.Margin = new Padding(0, 0, 0, 10);

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, BackColor = UiTheme.CardBack };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, ControlRow));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, ControlRow));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            // ---- 标题行 ----
            // 显式绝对行高（设计像素，随 Scale 一起放大）：不加 RowStyle 时该行会按内容膨胀
            // （实测到 200px 高），把状态胶囊挤出行外、压在 flow1 背景之下导致不可见。
            // Margin=0 让 head 占满 layout 行，与 flow1/卡片内边距左对齐。
            var head = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = UiTheme.CardBack, Margin = new Padding(0) };
            head.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            head.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            head.RowStyles.Add(new RowStyle(SizeType.Absolute, ControlRow));

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
            lblSubtitle = subtitle;
            titleBox.Controls.Add(title);
            titleBox.Controls.Add(subtitle);

            // Anchor 必须含 Top：仅 Right 时控件在单元格内垂直居中，行高异常时会掉到行外被遮挡
            pillStatus = new StatusPill { Anchor = AnchorStyles.Top | AnchorStyles.Right, Margin = new Padding(12, 0, 0, 0) };
            pillStatus.SetState(false, "已停止");

            head.Controls.Add(titleBox, 0, 0);
            head.Controls.Add(pillStatus, 1, 0);

            // ---- 参数控制行 1：启停 + 型号/固件/主机名 ----
            var flow1 = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                WrapContents = false,
                BackColor = UiTheme.CardBack,
                Margin = new Padding(0)
            };
            btnStartStop = new FlatButton { Kind = ButtonKind.Primary, Text = "启动服务器", Width = 116 };
            btnStartStop.Height = 34;
            btnStartStop.Click += BtnStartStop_Click;
            flow1.Controls.Add(VerticalCenter(btnStartStop, ControlRow, 4, 0));

            cboModel = new ComboBox { Width = 116, DropDownStyle = ComboBoxStyle.DropDown };
            cboModel.Items.AddRange(new object[] { "CP4", "CP3", "CP3N", "MC4", "MC3", "MC2S", "PRO3", "RMC4", "RMC3", "DINAP3" });
            UiTheme.StyleInput(cboModel);
            AddField(flow1, "型号", cboModel);

            txtFirmware = new TextBox { Width = 116 };
            UiTheme.StyleInput(txtFirmware);
            AddField(flow1, "固件", txtFirmware);

            txtHostName = new TextBox { Width = 100 };
            UiTheme.StyleInput(txtHostName);
            AddField(flow1, "主机名", txtHostName);

            // ---- 参数控制行 2：端口 + 启用开关 + 保存配置/IP 表/精简模式 ----
            var flow2 = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                WrapContents = false,
                BackColor = UiTheme.CardBack,
                Margin = new Padding(0)
            };

            numPort94 = new NumericUpDown { Minimum = 1024, Maximum = 65535, Width = 70 };
            UiTheme.StyleInput(numPort94);
            AddField(flow2, "raw 端口", numPort94);

            numPort95 = new NumericUpDown { Minimum = 1024, Maximum = 65535, Width = 70 };
            UiTheme.StyleInput(numPort95);
            AddField(flow2, "packetized 端口", numPort95);

            chkPort95 = new CheckBox { Text = "启用 41795", AutoSize = true };
            UiTheme.StyleInput(chkPort95);
            configFields.Add(chkPort95);
            flow2.Controls.Add(VerticalCenter(chkPort95, ControlRow, 10, 0));

            btnSaveConfig = new FlatButton { Kind = ButtonKind.Secondary, Text = "保存配置", Width = 92 };
            btnSaveConfig.Click += BtnSaveConfig_Click;
            flow2.Controls.Add(VerticalCenter(btnSaveConfig, ControlRow, 14, 0));

            btnIpTable = new FlatButton { Kind = ButtonKind.Secondary, Text = "IP 表…", Width = 84 };
            btnIpTable.Click += BtnIpTable_Click;
            flow2.Controls.Add(VerticalCenter(btnIpTable, ControlRow, 6, 0));

            btnCompact = new FlatButton { Kind = ButtonKind.Secondary, Text = "精简模式", Width = 92 };
            btnCompact.Click += BtnCompact_Click;
            flow2.Controls.Add(VerticalCenter(btnCompact, ControlRow, 6, 0));

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
            infoStrip = strip;

            layout.Controls.Add(head, 0, 0);
            layout.Controls.Add(flow1, 0, 1);
            layout.Controls.Add(flow2, 0, 2);
            layout.Controls.Add(strip, 0, 3);
            card.Controls.Add(layout);
            return card;
        }

        /// <summary>FlowLayoutPanel 不支持 Anchor，用 Margin 让控件在行内垂直居中</summary>
        static Control VerticalCenter(Control c, int rowHeight, int leftMargin, int rightMargin)
        {
            c.Margin = new Padding(leftMargin, Math.Max(0, (rowHeight - c.Height) / 2), rightMargin, 0);
            return c;
        }

        /// <summary>向控制行追加「标签 + 输入控件」组合（两者垂直居中，同时登记到精简模式隐藏列表）</summary>
        void AddField(FlowLayoutPanel flow, string label, Control input)
        {
            var lbl = UiTheme.FieldLabel(label);
            lbl.Margin = new Padding(14, Math.Max(0, (ControlRow - lbl.Height) / 2), 5, 0);
            configFields.Add(lbl);
            configFields.Add(input);
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

            var head = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, BackColor = UiTheme.CardBack };
            head.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));       // 标题
            head.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));   // 说明（右对齐）
            head.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));       // 在线客户端数
            var title = UiTheme.SectionTitle("客户端连接");
            title.Margin = new Padding(0, 3, 12, 0);
            var hint = UiTheme.Caption("Who-Is → Sign-On → 注册回放 · 每秒刷新 · IPID 不在 IP 表内将被拒绝");
            hint.Dock = DockStyle.Fill;
            hint.TextAlign = ContentAlignment.MiddleRight;
            hint.Margin = new Padding(0, 5, 14, 0);
            lblClientStat = UiTheme.Caption("在线 0");
            lblClientStat.Font = UiTheme.FontBold;
            lblClientStat.Margin = new Padding(0, 4, 0, 0);
            head.Controls.Add(title, 0, 0);
            head.Controls.Add(hint, 1, 0);
            head.Controls.Add(lblClientStat, 2, 0);

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
            cardLog = BuildLogCard();
            body.Controls.Add(cardLog, 1, 0);
            bodyLayout = body;
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

            cardJoin = card;
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

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, BackColor = UiTheme.LogBack };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));    // 标题 + 状态
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));    // 工具条：显示开关
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));    // 工具条：过滤/搜索/导出
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));    // 日志文本

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
            lblLogStat = hint;    // 右上角状态：显示条数 / 过滤 / 暂停，随日志实时刷新
            head.Controls.Add(title, 0, 0);
            head.Controls.Add(hint, 1, 0);

            // ---- 工具条 1：显示开关（十六进制 / 自动滚动 / 自动换行 / 暂停 / 清空） ----
            var barToggle = new ToolStrip { Dock = DockStyle.Top };
            btnHexToggle = new ToolStripButton { Text = "十六进制", CheckOnClick = true, Checked = true };
            btnHexToggle.Click += delegate { UpdateHexDisplay(); };
            btnScrollToggle = new ToolStripButton { Text = "自动滚动", CheckOnClick = true, Checked = true };
            btnLogWrap = new ToolStripButton { Text = "自动换行", CheckOnClick = true, Checked = false };
            btnLogWrap.Click += delegate { rtbLog.WordWrap = btnLogWrap.Checked; };
            btnLogPause = new ToolStripButton { Text = "暂停", CheckOnClick = true };
            btnLogPause.Click += delegate { SetLogPaused(btnLogPause.Checked); };
            var btnClear = new ToolStripButton { Text = "清空" };
            btnClear.Click += delegate { ClearLog(); };
            barToggle.Items.AddRange(new ToolStripItem[]
            {
                btnHexToggle, btnScrollToggle, btnLogWrap, new ToolStripSeparator(),
                btnLogPause, new ToolStripSeparator(), btnClear
            });
            UiTheme.StyleToolStrip(barToggle, true);
            var barToggleHost = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.LogBack };
            barToggleHost.Controls.Add(barToggle);

            // ---- 工具条 2：类型过滤 / 关键字搜索 / 导出 ----
            var barFilter = new ToolStrip { Dock = DockStyle.Top };
            cboLogFilter = new ComboBox { Width = 96, DropDownStyle = ComboBoxStyle.DropDownList };
            cboLogFilter.Items.AddRange(new object[] { "全部", "TX→", "RX←", "成功 √", "失败 ×", "其他" });
            UiTheme.StyleInput(cboLogFilter);
            cboLogFilter.SelectedIndex = 0;
            cboLogFilter.SelectedIndexChanged += delegate
            {
                logFilter = FilterFromIndex(cboLogFilter.SelectedIndex);
                RebuildLogText();
            };
            txtLogSearch = new TextBox { Width = 126 };
            UiTheme.StyleInput(txtLogSearch);
            txtLogSearch.TextChanged += delegate
            {
                logSearchText = txtLogSearch.Text.Trim();
                logSearchTimer.Stop();     // 防抖：连续输入只在停止 250ms 后重建一次
                logSearchTimer.Start();
            };
            txtLogSearch.KeyDown += OnLogSearchKeyDown;
            var btnExport = new ToolStripButton { Text = "导出…" };
            btnExport.Click += delegate { ExportLog(); };
            barFilter.Items.AddRange(new ToolStripItem[]
            {
                new ToolStripLabel { Text = "类型:" },
                UiTheme.HostControl(cboLogFilter),
                new ToolStripLabel { Text = "搜索:" },
                UiTheme.HostControl(txtLogSearch),
                btnExport
            });
            UiTheme.StyleToolStrip(barFilter, true);
            var barFilterHost = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.LogBack };
            barFilterHost.Controls.Add(barFilter);

            rtbLog = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                BackColor = UiTheme.LogBack,
                ForeColor = UiTheme.LogText,
                Font = UiTheme.FontMono,
                DetectUrls = false,
                WordWrap = false     // 默认不换行：报文/JSON 一行一条，长行用水平滚动查看
            };
            var logHost = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.LogBack, Padding = new Padding(2, 6, 2, 0) };
            logHost.Controls.Add(rtbLog);

            logSearchTimer = new Timer { Interval = 250 };
            logSearchTimer.Tick += delegate { logSearchTimer.Stop(); RebuildLogText(); };

            layout.Controls.Add(head, 0, 0);
            layout.Controls.Add(barToggleHost, 0, 1);
            layout.Controls.Add(barFilterHost, 0, 2);
            layout.Controls.Add(logHost, 0, 3);
            card.Controls.Add(layout);
            return card;
        }

        // ===================== 配置 =====================

        /// <summary>精简模式切换：隐藏副标题/配置参数/信息条/IP 表/客户端列表，仅保留启停、Join 与日志</summary>
        void BtnCompact_Click(object sender, EventArgs e)
        {
            bool compact = !config.CompactMode;
            ApplyCompactMode(compact);
            config.CompactMode = compact;
            try
            {
                config.Save(DeviceConfig.DefaultPath);   // 状态持久化，重启后保持
            }
            catch (Exception ex)
            {
                Log.Info("× 精简模式状态保存失败: " + ex.Message);
            }
            Log.Info(compact ? "已切换到精简模式（窗口 680x400，隐藏日志/客户端列表/配置参数）" : "已恢复完整模式");
        }

        /// <summary>应用精简模式：隐藏日志/配置参数/信息条/客户端列表，窗口缩至 1/4；Join 页签保留可切换</summary>
        void ApplyCompactMode(bool compact)
        {
            if (lblSubtitle != null) lblSubtitle.Visible = !compact;
            if (infoStrip != null) infoStrip.Visible = !compact;
            if (btnIpTable != null) btnIpTable.Visible = !compact;
            foreach (var c in configFields) c.Visible = !compact;
            if (cardClients != null) cardClients.Visible = !compact;
            if (cardLog != null) cardLog.Visible = !compact;                  // 精简模式不显示报文日志
            // 注意: 此方法在 ApplyDpiScale() 之后执行，属"缩放后重设"，
            // 行高与外边距必须用 Px 换算，否则会退回设计像素导致头部行被裁半。
            if (cardJoin != null) cardJoin.Margin = compact ? new Padding(0) : new Padding(0, 0, UiTheme.Px(8), 0);
            if (bodyLayout != null)
            {
                bodyLayout.ColumnStyles[0] = new ColumnStyle(SizeType.Percent, compact ? 100 : 64);
                bodyLayout.ColumnStyles[1] = new ColumnStyle(SizeType.Percent, compact ? 0 : 36);
            }
            if (rootLayout != null)
            {
                rootLayout.RowStyles[0] = new RowStyle(SizeType.Absolute,
                    UiTheme.Px((compact ? CompactHeaderHeight : HeaderHeight) + 10));
                rootLayout.RowStyles[1] = new RowStyle(SizeType.Absolute,
                    compact ? 0 : UiTheme.Px(ClientHeight + 10));
            }
            if (btnCompact != null) btnCompact.Text = compact ? "完整模式" : "精简模式";

            // ---- 窗口尺寸：精简 = 完整模式的 1/4 面积；完整 = 还原进入精简前的窗口外框 ----
            if (compact)
            {
                if (IsHandleCreated && preCompactBounds.IsEmpty) preCompactBounds = Bounds;
                MinimumSize = CompactMinimumSize;
                ClientSize = CompactClientSize;
                if (IsHandleCreated && !preCompactBounds.IsEmpty)
                {
                    // 在原窗口范围内居中，并夹到屏幕工作区内
                    var area = Screen.GetWorkingArea(preCompactBounds);
                    int x = Math.Max(area.Left, Math.Min(area.Right - Width,
                        preCompactBounds.X + (preCompactBounds.Width - Width) / 2));
                    int y = Math.Max(area.Top, Math.Min(area.Bottom - Height,
                        preCompactBounds.Y + (preCompactBounds.Height - Height) / 2));
                    Location = new Point(x, y);
                }
                // 启动时直接进入精简模式：不设置 Location，交给 StartPosition = CenterScreen 居中
            }
            else
            {
                MinimumSize = FullMinimumSize;
                if (!preCompactBounds.IsEmpty)
                {
                    Bounds = preCompactBounds;
                    preCompactBounds = Rectangle.Empty;
                }
            }
        }

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
            if (logSearchTimer != null)
            {
                logSearchTimer.Stop();
                logSearchTimer.Dispose();   // 同上：日志搜索防抖定时器
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
            UpdateClientStat(sessions.Length);

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

        /// <summary>更新客户端卡片右上角的在线数（0 个时用弱化色，避免与"未连接"混淆）</summary>
        void UpdateClientStat(int online)
        {
            if (lblClientStat == null || lblClientStat.IsDisposed) return;
            string text = string.Format("在线 {0}", online);
            if (lblClientStat.Text != text)
            {
                lblClientStat.Text = text;
                lblClientStat.ForeColor = online > 0 ? UiTheme.SuccessText : UiTheme.TextMuted;
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

        /// <summary>收到一行日志：先入环形缓冲，再按「未暂停 + 命中筛选」决定是否追加显示</summary>
        void AppendLog(string line)
        {
            if (rtbLog == null || rtbLog.IsDisposed) return;
            LogLineKind kind = logView.Add(line);
            if (!logPaused && LogViewState.Match(kind, line, logFilter, logSearchText))
            {
                AppendLogLine(line, LineColor(kind));
                if (btnScrollToggle.Checked)
                {
                    rtbLog.SelectionStart = rtbLog.TextLength;
                    rtbLog.ScrollToCaret();
                }
                TrimLogText();
            }
            UpdateLogStat();
        }

        /// <summary>TX→ / RX← / √ / × 分色（分类规则见 LogViewState.Classify）</summary>
        static Color LineColor(LogLineKind kind)
        {
            switch (kind)
            {
                case LogLineKind.Tx: return UiTheme.LogTx;
                case LogLineKind.Rx: return UiTheme.LogRx;
                case LogLineKind.Ok: return UiTheme.LogOk;
                case LogLineKind.Error: return UiTheme.LogError;
                default: return UiTheme.LogText;
            }
        }

        /// <summary>按指定颜色追加一行（必须在 UI 线程调用）</summary>
        void AppendLogLine(string line, Color color)
        {
            rtbLog.SelectionStart = rtbLog.TextLength;
            rtbLog.SelectionColor = color;
            rtbLog.AppendText(line + Environment.NewLine);
        }

        /// <summary>显示文本超过上限时丢弃最旧若干行（缓冲另由环形容量控制，两者互不影响）</summary>
        void TrimLogText()
        {
            if (rtbLog.TextLength <= LogMaxChars) return;
            int cut = rtbLog.GetFirstCharIndexFromLine(LogTrimKeepLines);
            if (cut <= 0) return;
            rtbLog.ReadOnly = false;
            rtbLog.Select(0, cut);
            rtbLog.SelectedText = "";
            rtbLog.ReadOnly = true;
        }

        /// <summary>按当前过滤/搜索条件从缓冲重建显示（切换条件、搜索、继续时调用）</summary>
        void RebuildLogText()
        {
            if (rtbLog == null || rtbLog.IsDisposed) return;
            logView.SetCriteria(logFilter, logSearchText);
            bool wasVisible = rtbLog.Visible;
            if (wasVisible) rtbLog.Visible = false;    // 隐藏后整块重建，避免逐段刷新闪烁
            try
            {
                rtbLog.Clear();
                foreach (var rec in logView.Records)
                {
                    if (!LogViewState.Match(rec.Kind, rec.Text, logFilter, logSearchText)) continue;
                    AppendLogLine(rec.Text, LineColor(rec.Kind));
                }
                if (btnScrollToggle.Checked)
                {
                    rtbLog.SelectionStart = rtbLog.TextLength;
                    rtbLog.ScrollToCaret();
                }
            }
            finally
            {
                if (wasVisible) rtbLog.Visible = true;   // 精简模式下本就不可见，保持隐藏
            }
            UpdateLogStat();
        }

        /// <summary>刷新日志标题行右侧状态：显示条数 + 过滤/暂停提示</summary>
        void UpdateLogStat()
        {
            if (lblLogStat == null || lblLogStat.IsDisposed) return;
            var sb = new StringBuilder();
            sb.Append("显示 ").Append(logView.MatchCount).Append('/').Append(logView.Count).Append(" 条");
            if (logFilter.HasValue) sb.Append(" · 已按类型过滤");
            if (!string.IsNullOrEmpty(logSearchText)) sb.Append(" · 关键字 ").Append(logSearchText);
            if (logPaused) sb.Append(" · 已暂停（仍在记录）");
            sb.Append(" · 同时写入 cip.log");
            lblLogStat.Text = sb.ToString();
            lblLogStat.ForeColor = logPaused ? UiTheme.Warning : UiTheme.LogMuted;
        }

        /// <summary>暂停/继续日志显示：暂停期间日志仍进入缓冲，继续时按当前条件补齐</summary>
        void SetLogPaused(bool paused)
        {
            if (logPaused == paused) return;
            logPaused = paused;
            if (btnLogPause != null) btnLogPause.Text = paused ? "继续" : "暂停";
            if (paused) UpdateLogStat();
            else RebuildLogText();
        }

        /// <summary>清空日志缓冲与显示（cip.log 文件本身不受影响）</summary>
        void ClearLog()
        {
            logView.Clear();
            if (rtbLog != null && !rtbLog.IsDisposed) rtbLog.Clear();
            UpdateLogStat();
        }

        /// <summary>把当前筛选条件下的日志导出为文本文件（UTF-8 无 BOM，文件名带时间戳）</summary>
        void ExportLog()
        {
            string text = logView.BuildText(logFilter, logSearchText);
            if (text.Length == 0)
            {
                MessageBox.Show("当前筛选条件下没有可导出的日志", "导出日志",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using (var dlg = new SaveFileDialog())
            {
                dlg.Title = "导出报文日志";
                dlg.Filter = "文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*";
                dlg.FileName = string.Format("cip-log-{0:yyyyMMdd-HHmmss}.txt", DateTime.Now);
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    File.WriteAllText(dlg.FileName, text, new UTF8Encoding(false));
                    MessageBox.Show(string.Format("已导出 {0} 条日志：\n{1}", logView.MatchCount, dlg.FileName),
                        "导出成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("导出失败: " + ex.Message, "错误",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        /// <summary>搜索框按键：Enter 立即重建显示，Esc 清空关键字</summary>
        void OnLogSearchKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                txtLogSearch.Clear();     // 触发 TextChanged → 防抖后重建
                return;
            }
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;    // 阻止输入框发出提示音
            logSearchTimer.Stop();
            RebuildLogText();
        }

        /// <summary>过滤下拉项索引 → 日志类型（0 = 全部，5 = 其他信息）</summary>
        static LogLineKind? FilterFromIndex(int index)
        {
            switch (index)
            {
                case 1: return LogLineKind.Tx;
                case 2: return LogLineKind.Rx;
                case 3: return LogLineKind.Ok;
                case 4: return LogLineKind.Error;
                case 5: return LogLineKind.Info;
                default: return null;
            }
        }

        void UpdateHexDisplay()
        {
            Log.ShowHex = btnHexToggle.Checked;
        }
    }
}
