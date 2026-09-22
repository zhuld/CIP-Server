// ============================================================================
//  CIP-Server — Crestron CIP 协议模拟服务器（MIT License · 代码由 AI 辅助生成）
//  文件: UI/FlatButton.cs
//  说明: 圆角扁平按钮。自绘填充/边框/文字，支持 Primary（实心主色）、
//        Secondary（白底描边）、Ghost（无底色）三种样式与悬停/按下/禁用状态。
// ============================================================================

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CipServerSim.UI
{
    /// <summary>按钮样式</summary>
    enum ButtonKind
    {
        /// <summary>主按钮：实心主色 + 白字</summary>
        Primary,
        /// <summary>次按钮：白底 + 细边框</summary>
        Secondary,
        /// <summary>文字按钮：无底色，悬停显示浅底</summary>
        Ghost,
        /// <summary>危险按钮：实心红（用于停止服务器）</summary>
        Danger
    }

    /// <summary>圆角扁平按钮（替代系统 3D 按钮）</summary>
    sealed class FlatButton : Button
    {
        /// <summary>圆角半径（像素）</summary>
        const int Radius = 6;

        ButtonKind kind = ButtonKind.Secondary;
        bool hovered;
        bool pressed;

        public FlatButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            BackColor = UiTheme.CardBack;
            ForeColor = UiTheme.TextPrimary;
            Font = UiTheme.FontBase;
            Cursor = Cursors.Hand;
            Height = 32;
        }

        /// <summary>按钮样式</summary>
        public ButtonKind Kind
        {
            get { return kind; }
            set { kind = value; Invalidate(); }
        }

        /// <summary>自绘：填充 + 可选边框 + 居中文字</summary>
        protected override void OnPaint(PaintEventArgs pevent)
        {
            var g = pevent.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);

            Color fill, fore, border;
            ResolveColors(out fill, out fore, out border);
            UiTheme.FillRounded(g, rect, Radius, fill);
            if (border != Color.Empty) UiTheme.StrokeRounded(g, rect, Radius, border);
            TextRenderer.DrawText(g, Text, Font, rect, fore,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }

        void ResolveColors(out Color fill, out Color fore, out Color border)
        {
            border = Color.Empty;
            if (!Enabled)
            {
                fore = UiTheme.TextMuted;
                switch (kind)
                {
                    case ButtonKind.Primary:
                        fill = UiTheme.AccentSoft;
                        break;
                    case ButtonKind.Danger:
                        fill = UiTheme.DangerSoft;
                        break;
                    case ButtonKind.Ghost:
                        fill = Color.Transparent;
                        break;
                    default:
                        fill = UiTheme.DisabledBack;
                        border = UiTheme.Border;
                        break;
                }
                return;
            }
            switch (kind)
            {
                case ButtonKind.Primary:
                    fill = pressed ? UiTheme.AccentPress : (hovered ? UiTheme.AccentHover : UiTheme.Accent);
                    fore = Color.White;
                    break;
                case ButtonKind.Danger:
                    fill = pressed ? UiTheme.DangerPress : (hovered ? UiTheme.DangerHover : UiTheme.Danger);
                    fore = Color.White;
                    break;
                case ButtonKind.Ghost:
                    fill = pressed ? UiTheme.PressBack : (hovered ? UiTheme.HoverBack : Color.Transparent);
                    fore = hovered ? UiTheme.Accent : UiTheme.TextSecondary;
                    break;
                default:
                    fill = pressed ? UiTheme.PressBack : (hovered ? UiTheme.HoverBack : UiTheme.CardBack);
                    fore = UiTheme.TextPrimary;
                    border = UiTheme.Border;
                    break;
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            hovered = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hovered = false;
            pressed = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs mevent)
        {
            pressed = true;
            Invalidate();
            base.OnMouseDown(mevent);
        }

        protected override void OnMouseUp(MouseEventArgs mevent)
        {
            pressed = false;
            Invalidate();
            base.OnMouseUp(mevent);
        }

        protected override void OnTextChanged(EventArgs e)
        {
            Invalidate();
            base.OnTextChanged(e);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            Invalidate();
            base.OnEnabledChanged(e);
        }
    }
}
