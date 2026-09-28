// ============================================================================
//  CIP-Server — Crestron CIP 协议模拟服务器（MIT License · 代码由 AI 辅助生成）
//  文件: UI/UiTheme.cs
//  说明: 全局界面主题（深色卡片 + 深色日志终端）。集中定义配色、字体、圆角绘制
//        与控件风格工厂，避免各面板各自硬编码颜色。
//  配色: 窗体底 #1E1E2E · 卡片 #252537 · 边框 #3A3A4E · 主色 #3B82F6 · 日志底 #0F172A
//        正文 #DEE2EC · 次要 #A2A6B4 · 弱化 #8C92A5（弱化色与卡片底对比度 ≈4.8:1，达 WCAG AA）
// ============================================================================

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CipServerSim.UI
{
    /// <summary>统一主题：颜色、字体、圆角绘制与控件风格工厂</summary>
    static class UiTheme
    {
        // ---------------- 基础色 ----------------
        public static readonly Color WindowBack = Color.FromArgb(30, 30, 46);
        public static readonly Color CardBack = Color.FromArgb(37, 37, 55);
        public static readonly Color Border = Color.FromArgb(58, 58, 78);
        public static readonly Color BorderStrong = Color.FromArgb(72, 72, 95);
        public static readonly Color TextPrimary = Color.FromArgb(222, 226, 236);
        public static readonly Color TextSecondary = Color.FromArgb(162, 166, 180);
        // 弱化文字：与卡片底(37,37,55)对比度 ≈4.8:1（原 (110,114,130) 仅 ≈2.6:1，小字难以辨认）
        public static readonly Color TextMuted = Color.FromArgb(140, 146, 165);

        public static readonly Color Accent = Color.FromArgb(59, 130, 246);
        public static readonly Color AccentHover = Color.FromArgb(75, 147, 255);
        public static readonly Color AccentPress = Color.FromArgb(45, 105, 200);
        public static readonly Color AccentSoft = Color.FromArgb(49, 78, 135);

        public static readonly Color HoverBack = Color.FromArgb(45, 45, 65);
        public static readonly Color PressBack = Color.FromArgb(52, 52, 75);
        public static readonly Color TrackBack = Color.FromArgb(42, 42, 60);
        /// <summary>进度条轨道底色（模拟 join 进度列）；与 GridAltRow 数值相同但语义不同，勿互相替换</summary>
        public static readonly Color ProgressTrack = Color.FromArgb(50, 50, 70);
        public static readonly Color DisabledBack = Color.FromArgb(32, 32, 48);

        public static readonly Color Success = Color.FromArgb(34, 197, 94);
        public static readonly Color SuccessText = Color.FromArgb(74, 222, 128);
        public static readonly Color SuccessSoft = Color.FromArgb(35, 55, 42);
        public static readonly Color Danger = Color.FromArgb(239, 68, 68);
        public static readonly Color DangerHover = Color.FromArgb(220, 50, 50);
        public static readonly Color DangerPress = Color.FromArgb(185, 40, 40);
        public static readonly Color DangerSoft = Color.FromArgb(55, 35, 35);
        public static readonly Color Warning = Color.FromArgb(245, 158, 11);
        public static readonly Color RowHigh = Color.FromArgb(40, 55, 45);

        // ---------------- 网格 ----------------
        public static readonly Color GridHeaderBack = Color.FromArgb(45, 45, 65);
        public static readonly Color GridAltRow = Color.FromArgb(42, 42, 60);
        public static readonly Color GridLine = Color.FromArgb(50, 50, 70);
        public static readonly Color GridSelection = Color.FromArgb(49, 78, 135);

        // ---------------- 日志终端 ----------------
        public static readonly Color LogBack = Color.FromArgb(15, 23, 42);
        public static readonly Color LogBorder = Color.FromArgb(30, 41, 59);
        public static readonly Color LogBarBack = Color.FromArgb(17, 27, 46);
        public static readonly Color LogText = Color.FromArgb(226, 232, 240);
        public static readonly Color LogMuted = Color.FromArgb(148, 163, 184);
        public static readonly Color LogTx = Color.FromArgb(74, 222, 128);
        public static readonly Color LogRx = Color.FromArgb(96, 165, 250);
        public static readonly Color LogError = Color.FromArgb(248, 113, 113);
        public static readonly Color LogOk = Color.FromArgb(134, 239, 172);

        // ---------------- 字体 ----------------
        public const string FontFamily = "Microsoft YaHei UI";

        public static readonly Font FontBase = new Font(FontFamily, 9F);
        public static readonly Font FontSmall = new Font(FontFamily, 8.25F);
        public static readonly Font FontBold = new Font(FontFamily, 9F, FontStyle.Bold);
        public static readonly Font FontTitle = new Font(FontFamily, 13.5F, FontStyle.Bold);
        public static readonly Font FontMono = new Font("Consolas", 9.5F);

        // ---------------- 圆角绘制 ----------------
        /// <summary>圆角矩形路径（半径自动收敛到短边一半以内）</summary>
        public static GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            var path = new GraphicsPath();
            if (bounds.Width <= 0 || bounds.Height <= 0) return path;
            int r = Math.Max(1, Math.Min(radius, Math.Min(bounds.Width, bounds.Height) / 2));
            int d = r * 2;
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        /// <summary>圆角填充</summary>
        /// <summary>圆角填充（半径按 DPI 缩放；绘制时半径自动收敛到短边一半以内）</summary>
        public static void FillRounded(Graphics g, Rectangle bounds, int radius, Color color)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0) return;
            using (var path = RoundedRect(bounds, Px(radius)))
            using (var brush = new SolidBrush(color))
                g.FillPath(brush, path);
        }

        /// <summary>圆角描边（线宽随 DPI 缩放，并按线宽内缩，避免外侧半个线宽被裁掉）</summary>
        public static void StrokeRounded(Graphics g, Rectangle bounds, int radius, Color color)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0) return;
            int width = Math.Max(1, Px(1));
            var rect = width > 1 ? Rectangle.Inflate(bounds, -(width / 2), -(width / 2)) : bounds;
            using (var path = RoundedRect(rect, Px(radius)))
            using (var pen = new Pen(color, width))
                g.DrawPath(pen, path);
        }

        /// <summary>创建圆角卡片（parentColor 用于填充圆角外区域，应与父容器底色一致）</summary>
        public static CardPanel Card(Color cardColor, Color parentColor, int radius)
        {
            return new CardPanel { CardColor = cardColor, BackColor = parentColor, Radius = radius };
        }
        // ---------------- 文本标签 ----------------
        /// <summary>卡片大标题</summary>
        public static Label Title(string text)
        {
            return new Label { Text = text, Font = FontTitle, ForeColor = TextPrimary, AutoSize = true, BackColor = Color.Transparent };
        }

        /// <summary>分节小标题</summary>
        public static Label SectionTitle(string text)
        {
            return new Label { Text = text, Font = FontBold, ForeColor = TextPrimary, AutoSize = true, BackColor = Color.Transparent };
        }

        /// <summary>次要说明文字</summary>
        public static Label Caption(string text)
        {
            return new Label { Text = text, Font = FontSmall, ForeColor = TextMuted, AutoSize = true, BackColor = Color.Transparent };
        }

        /// <summary>输入项标题</summary>
        public static Label FieldLabel(string text)
        {
            return new Label { Text = text, Font = FontBase, ForeColor = TextSecondary, AutoSize = true, BackColor = Color.Transparent };
        }

        // ---------------- 输入控件 ----------------
        /// <summary>输入控件扁平风格（TextBox / ComboBox / NumericUpDown / CheckBox）</summary>
        public static void StyleInput(Control c)
        {
            if (c == null) return;
            c.Font = FontBase;
            var tb = c as TextBox;
            if (tb != null)
            {
                tb.BorderStyle = BorderStyle.FixedSingle;
                tb.BackColor = CardBack;
                tb.ForeColor = TextPrimary;
                return;
            }
            var cb = c as ComboBox;
            if (cb != null)
            {
                cb.FlatStyle = FlatStyle.Flat;
                cb.BackColor = CardBack;
                cb.ForeColor = TextPrimary;
                return;
            }
            var num = c as NumericUpDown;
            if (num != null)
            {
                num.BorderStyle = BorderStyle.FixedSingle;
                num.BackColor = CardBack;
                num.ForeColor = TextPrimary;
                return;
            }
            var ck = c as CheckBox;
            if (ck != null)
            {
                ck.FlatStyle = FlatStyle.Flat;
                ck.ForeColor = TextSecondary;
                ck.BackColor = Color.Transparent;
            }
        }

        /// <summary>
        /// 生成工具条宿主项（配合 ToolStrip.Items 使用），并修正两个 ToolStrip 宿主的已知坑：
        /// 1) TextBox / NumericUpDown 的 GetPreferredSize 会忽略设定宽度（前者按"内容宽度"、空文本时
        ///    仅约 4px；后者按编辑区最优宽度），工具条按该结果布局会把控件压窄甚至压成一条竖线
        ///    → 关闭宿主项自动尺寸并显式指定控件尺寸；
        /// 2) Enter/Esc 属于"对话框键"，ToolStrip 会先行处理（切换条目/取消），宿主控件收不到 KeyDown
        ///    → 在 PreviewKeyDown 中声明为输入键，回车提交、Esc 清空等交互才能生效。
        /// </summary>
        public static ToolStripControlHost HostControl(Control c)
        {
            c.PreviewKeyDown += HostPreviewKeyDown;
            // 统一用控件自身尺寸：TextBox / NumericUpDown 的 GetPreferredSize 都忽略设定宽度
            // （前者按内容宽度、后者按编辑区最优宽度），交给工具条自动布局会被压窄。
            return new ToolStripControlHost(c) { AutoSize = false, Size = c.Size };
        }

        static void HostPreviewKeyDown(object sender, PreviewKeyDownEventArgs e)
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Escape) e.IsInputKey = true;
        }

        // ---------------- 高 DPI 换算 ----------------
        // 本 UI 全部以 96dpi 的"设计像素"书写尺寸。进程按 app.manifest 声明的 SystemAware
        // DPI 感知运行后，字体以磅为单位会自动按物理 DPI 清晰渲染，而控件尺寸/自绘像素仍是
        // 96dpi 数值，必须自行换算，否则 200% 缩放屏上文字会比控件大一倍（实测现象）。
        // 主窗体在构建完成后调用 Control.Scale(DpiScale) 统一放大全部控件边界、Padding、Margin
        // 与表格行列；本类的 Px() 则用于框架不处理的度量（DataGridView 行高、ToolStrip 内边距、
        // StatusPill/FlatButton/CardPanel/SegmentedTabs 等自绘尺寸）。
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern uint GetDpiForSystem();

        static float dpiScale;

        /// <summary>
        /// 当前显示器的 DPI 缩放系数（96dpi = 1.0；进程运行期间固定）。
        /// 优先 GetDpiForSystem（不随进程感知模式虚拟化，SystemAware/PerMonitor 下均返回
        /// 真实系统 DPI），失败时退回 Graphics.DpiX（SystemAware 下同样正确）。
        /// </summary>
        public static float DpiScale
        {
            get
            {
                if (dpiScale <= 0f)
                {
                    float f = 1f;
                    try
                    {
                        uint sysDpi = GetDpiForSystem();
                        if (sysDpi >= 96) f = sysDpi / 96f;
                        else throw new EntryPointNotFoundException();
                    }
                    catch (Exception)
                    {
                        try
                        {
                            using (var g = Graphics.FromHwnd(IntPtr.Zero)) f = g.DpiX / 96f;
                        }
                        catch (Exception) { f = 1f; }
                    }
                    dpiScale = f > 0f ? f : 1f;
                }
                return dpiScale;
            }
        }

        /// <summary>把 96dpi 设计像素换算为当前 DPI 下的像素</summary>
        public static int Px(int designPixels)
        {
            return (int)Math.Round(designPixels * DpiScale);
        }

        /// <summary>把 96dpi 设计内边距换算为当前 DPI 下的内边距</summary>
        public static Padding Px(int left, int top, int right, int bottom)
        {
            return new Padding(Px(left), Px(top), Px(right), Px(bottom));
        }

        /// <summary>
        /// 按显示器 DPI 等比放大窗体及其全部子控件：Control.Scale 会递归缩放边界、Padding、
        /// Margin 与 TableLayoutPanel 的绝对行列尺寸，且不改动字体（磅为单位本身即分辨率无关）。
        /// 说明: .NET Framework 下 AutoScaleMode=Dpi 对"纯代码构建"的窗体不生效（已实测：
        /// AutoScaleDimensions 会被对齐到当前 DPI，缩放系数恒为 1），故显式调用 Control.Scale。
        /// </summary>
        public static void ScaleToDpi(Form form)
        {
            if (form == null) return;
            float f = DpiScale;
            if (f <= 0f || Math.Abs(f - 1f) < 0.01f) return;
            form.SuspendLayout();
            form.Scale(new SizeF(f, f));
            form.ResumeLayout(true);
        }

        // ---------------- 表格 ----------------
        /// <summary>DataGridView 现代扁平风格：无立体表头、行高 28、交替行浅底、无竖线。
        /// 同时禁用点击表头排序（行序与 join 号一一对应，排序会破坏该映射）。</summary>
        public static void StyleGrid(DataGridView g, bool readOnly)
        {
            g.ColumnAdded -= GridColumnAdded;
            g.ColumnAdded += GridColumnAdded;
            g.BackgroundColor = CardBack;
            g.BorderStyle = BorderStyle.None;
            g.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            g.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            g.GridColor = GridLine;
            g.EnableHeadersVisualStyles = false;
            g.AllowUserToAddRows = false;
            g.AllowUserToDeleteRows = false;
            g.AllowUserToResizeRows = false;
            g.AllowUserToResizeColumns = false;
            g.RowHeadersVisible = false;
            g.ReadOnly = readOnly;
            g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            g.MultiSelect = false;
            g.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            g.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            g.ColumnHeadersHeight = Px(32);
            g.RowTemplate.Height = Px(28);
            g.EditMode = readOnly ? DataGridViewEditMode.EditProgrammatically : DataGridViewEditMode.EditOnKeystrokeOrF2;

            var head = g.ColumnHeadersDefaultCellStyle;
            head.BackColor = GridHeaderBack;
            head.ForeColor = TextSecondary;
            head.SelectionBackColor = GridHeaderBack;
            head.SelectionForeColor = TextSecondary;
            head.Font = FontBold;
            head.Padding = Px(10, 0, 0, 0);
            head.Alignment = DataGridViewContentAlignment.MiddleLeft;
            head.WrapMode = DataGridViewTriState.False;

            var cell = g.DefaultCellStyle;
            cell.BackColor = CardBack;
            cell.ForeColor = TextPrimary;
            cell.SelectionBackColor = GridSelection;
            cell.SelectionForeColor = TextPrimary;
            cell.Padding = Px(10, 0, 6, 0);
            cell.Font = FontBase;
            cell.WrapMode = DataGridViewTriState.False;

            g.AlternatingRowsDefaultCellStyle.BackColor = GridAltRow;
            g.AlternatingRowsDefaultCellStyle.SelectionBackColor = GridSelection;
            g.AlternatingRowsDefaultCellStyle.SelectionForeColor = TextPrimary;
        }

        /// <summary>新建列默认不可排序（行序与 join 号一一对应，避免误点表头打乱映射）</summary>
        static void GridColumnAdded(object sender, DataGridViewColumnEventArgs e)
        {
            e.Column.SortMode = DataGridViewColumnSortMode.NotSortable;
        }

        // ---------------- 工具条 ----------------
        /// <summary>ToolStrip 扁平风格</summary>
        public static void StyleToolStrip(ToolStrip bar, bool dark)
        {
            bar.GripStyle = ToolStripGripStyle.Hidden;
            bar.RenderMode = ToolStripRenderMode.Professional;
            // 高 DPI 下 ToolStrip 只按字体放大高度，若不放大内边距会和设计比例不符
            bar.Padding = Px(2, 3, 2, 3);
            bar.BackColor = dark ? LogBarBack : CardBack;
            bar.ForeColor = dark ? LogText : TextPrimary;
            bar.Renderer = new FlatToolStripRenderer(
                dark ? LogBarBack : CardBack,
                dark ? LogText : TextSecondary,
                dark ? Color.FromArgb(30, 41, 59) : HoverBack,
                dark ? Color.FromArgb(37, 99, 235) : AccentSoft,
                dark ? Color.White : Accent,
                dark ? Color.FromArgb(51, 65, 85) : Border);
            foreach (ToolStripItem item in bar.Items) StyleToolStripItem(item, dark);
        }

        /// <summary>ToolStrip 子项扁平风格</summary>
        public static void StyleToolStripItem(ToolStripItem item, bool dark)
        {
            item.Font = FontBase;
            var btn = item as ToolStripButton;
            if (btn != null)
            {
                btn.Padding = Px(8, 0, 8, 0);
                btn.AutoSize = true;
                return;
            }
            var label = item as ToolStripLabel;
            if (label != null)
            {
                label.ForeColor = dark ? LogMuted : TextSecondary;
                label.Padding = Px(6, 0, 2, 0);
            }
        }
    }
}
