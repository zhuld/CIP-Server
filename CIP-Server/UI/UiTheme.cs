// ============================================================================
//  CIP-Server — Crestron CIP 协议模拟服务器（MIT License · 代码由 AI 辅助生成）
//  文件: UI/UiTheme.cs
//  说明: 全局界面主题（Fluent 风格浅色卡片 + 深色日志终端）。集中定义配色、
//        字体、圆角绘制与控件风格工厂，避免各面板各自硬编码颜色。
//  配色: 窗体底 #F4F6F9 · 卡片 #FFFFFF · 边框 #E2E6ED · 主色 #2563EB · 日志底 #0F172A
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
        public static readonly Color TextMuted = Color.FromArgb(110, 114, 130);

        public static readonly Color Accent = Color.FromArgb(59, 130, 246);
        public static readonly Color AccentHover = Color.FromArgb(75, 147, 255);
        public static readonly Color AccentPress = Color.FromArgb(45, 105, 200);
        public static readonly Color AccentSoft = Color.FromArgb(49, 78, 135);

        public static readonly Color HoverBack = Color.FromArgb(45, 45, 65);
        public static readonly Color PressBack = Color.FromArgb(52, 52, 75);
        public static readonly Color TrackBack = Color.FromArgb(42, 42, 60);
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
        public static void FillRounded(Graphics g, Rectangle bounds, int radius, Color color)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0) return;
            using (var path = RoundedRect(bounds, radius))
            using (var brush = new SolidBrush(color))
                g.FillPath(brush, path);
        }

        /// <summary>圆角描边</summary>
        public static void StrokeRounded(Graphics g, Rectangle bounds, int radius, Color color)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0) return;
            using (var path = RoundedRect(bounds, radius))
            using (var pen = new Pen(color))
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
            g.ColumnHeadersHeight = 32;
            g.RowTemplate.Height = 28;
            g.EditMode = readOnly ? DataGridViewEditMode.EditProgrammatically : DataGridViewEditMode.EditOnKeystrokeOrF2;

            var head = g.ColumnHeadersDefaultCellStyle;
            head.BackColor = GridHeaderBack;
            head.ForeColor = TextSecondary;
            head.SelectionBackColor = GridHeaderBack;
            head.SelectionForeColor = TextSecondary;
            head.Font = FontBold;
            head.Padding = new Padding(10, 0, 0, 0);
            head.Alignment = DataGridViewContentAlignment.MiddleLeft;
            head.WrapMode = DataGridViewTriState.False;

            var cell = g.DefaultCellStyle;
            cell.BackColor = CardBack;
            cell.ForeColor = TextPrimary;
            cell.SelectionBackColor = GridSelection;
            cell.SelectionForeColor = TextPrimary;
            cell.Padding = new Padding(10, 0, 6, 0);
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
            bar.Padding = new Padding(2, 3, 2, 3);
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
                btn.Padding = new Padding(8, 0, 8, 0);
                btn.AutoSize = true;
                return;
            }
            var label = item as ToolStripLabel;
            if (label != null)
            {
                label.ForeColor = dark ? LogMuted : TextSecondary;
                label.Padding = new Padding(6, 0, 2, 0);
            }
        }
    }
}
