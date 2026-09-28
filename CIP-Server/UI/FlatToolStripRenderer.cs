// ============================================================================
//  CIP-Server — Crestron CIP 协议模拟服务器（MIT License · 代码由 AI 辅助生成）
//  文件: UI/FlatToolStripRenderer.cs
//  说明: ToolStrip 扁平渲染器。屏蔽系统 Professional 渲染器的渐变与 3D 边框，
//        改为纯色底 + 圆角悬停/选中胶囊，深浅两套配色由 UiTheme.StyleToolStrip 传入。
// ============================================================================

using System;
using System.Drawing;
using System.Windows.Forms;

namespace CipServerSim.UI
{
    /// <summary>ToolStrip 扁平渲染器</summary>
    sealed class FlatToolStripRenderer : ToolStripProfessionalRenderer
    {
        readonly Color background;
        readonly Color text;
        readonly Color hoverBack;
        readonly Color checkedBack;
        readonly Color checkedText;
        readonly Color separator;

        public FlatToolStripRenderer(Color background, Color textColor, Color hoverBack,
            Color checkedBack, Color checkedText, Color separator)
            : base(new FlatColorTable(background, separator))
        {
            this.background = background;
            this.text = textColor;
            this.hoverBack = hoverBack;
            this.checkedBack = checkedBack;
            this.checkedText = checkedText;
            this.separator = separator;
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using (var brush = new SolidBrush(background))
                e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            // 扁平风格：不绘制工具条边框
        }

        protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
        {
            var btn = e.Item as ToolStripButton;
            if (btn == null) return;
            bool hover = btn.Selected || btn.Pressed;
            if (!btn.Checked && !hover) return;
            var rect = new Rectangle(Point.Empty, btn.Size);
            int pad = UiTheme.Px(2);                 // btn.Size 为物理像素，内缩按 DPI 换算
            rect.Inflate(-pad, -pad);
            UiTheme.FillRounded(e.Graphics, rect, 6, btn.Checked ? checkedBack : hoverBack);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            var btn = e.Item as ToolStripButton;
            if (btn != null && btn.Checked) e.TextColor = checkedText;
            else if (!e.Item.Enabled) e.TextColor = UiTheme.TextMuted;
            else if (!(e.Item is ToolStripLabel)) e.TextColor = text;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            var b = e.Item.Bounds;
            int inset = UiTheme.Px(3);
            using (var pen = new Pen(separator, Math.Max(1, UiTheme.Px(1))))
            {
                if (e.Vertical)
                {
                    int x = b.Width / 2;
                    e.Graphics.DrawLine(pen, x, b.Top + inset, x, b.Bottom - inset);
                }
                else
                {
                    int y = b.Height / 2;
                    e.Graphics.DrawLine(pen, b.Left, y, b.Right, y);
                }
            }
        }
    }

    /// <summary>扁平配色表：把系统渐变/3D 配色统一为纯色</summary>
    sealed class FlatColorTable : ProfessionalColorTable
    {
        readonly Color background;
        readonly Color separator;

        public FlatColorTable(Color background, Color separator)
        {
            this.background = background;
            this.separator = separator;
        }

        public override Color ToolStripDropDownBackground { get { return UiTheme.CardBack; } }
        public override Color MenuBorder { get { return UiTheme.Border; } }
        public override Color MenuItemBorder { get { return UiTheme.AccentSoft; } }
        public override Color MenuItemSelected { get { return UiTheme.HoverBack; } }
        public override Color MenuItemSelectedGradientBegin { get { return UiTheme.HoverBack; } }
        public override Color MenuItemSelectedGradientEnd { get { return UiTheme.HoverBack; } }
        public override Color SeparatorDark { get { return separator; } }
        public override Color SeparatorLight { get { return separator; } }
        public override Color ToolStripBorder { get { return background; } }
        public override Color ToolStripGradientBegin { get { return background; } }
        public override Color ToolStripGradientMiddle { get { return background; } }
        public override Color ToolStripGradientEnd { get { return background; } }
        public override Color ImageMarginGradientBegin { get { return background; } }
        public override Color ImageMarginGradientMiddle { get { return background; } }
        public override Color ImageMarginGradientEnd { get { return background; } }
    }
}
