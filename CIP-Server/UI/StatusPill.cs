// ============================================================================
//  CIP-Server — Crestron CIP 协议模拟服务器（MIT License · 代码由 AI 辅助生成）
//  文件: UI/StatusPill.cs
//  说明: 状态胶囊控件。浅底圆角胶囊 + 状态圆点 + 加粗文本，用于在卡片标题右侧
//        显示服务器运行状态（运行中=绿色胶囊 / 已停止=灰色胶囊）。
// ============================================================================

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CipServerSim.UI
{
    /// <summary>状态胶囊：圆点 + 文本（配色随状态切换，宽度自适应文本）</summary>
    sealed class StatusPill : Control
    {
        const int DotLeft = 12;
        const int DotSize = 8;
        const int TextLeft = 26;
        const int RightPad = 14;

        Color pillBack = UiTheme.HoverBack;
        Color pillFore = UiTheme.TextSecondary;
        Color dotColor = UiTheme.TextMuted;

        public StatusPill()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = UiTheme.CardBack;
            Font = UiTheme.FontBold;
            Height = 26;
            TabStop = false;
        }

        /// <summary>按运行状态设置配色与文本</summary>
        public void SetState(bool online, string text)
        {
            if (online)
            {
                pillBack = UiTheme.SuccessSoft;
                pillFore = UiTheme.SuccessText;
                dotColor = UiTheme.Success;
            }
            else
            {
                pillBack = UiTheme.HoverBack;
                pillFore = UiTheme.TextSecondary;
                dotColor = UiTheme.TextMuted;
            }
            Text = text ?? "";
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            // 文本宽度由字体实测（本身随 DPI），左右留白需按 DPI 换算才能保持视觉比例
            int w = UiTheme.Px(TextLeft) + TextRenderer.MeasureText(Text, Font).Width + UiTheme.Px(RightPad);
            int min = UiTheme.Px(76);
            Width = w < min ? min : w;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            UiTheme.FillRounded(g, rect, Height / 2, pillBack);
            int dot = UiTheme.Px(DotSize);
            using (var brush = new SolidBrush(dotColor))
                g.FillEllipse(brush, UiTheme.Px(DotLeft), (Height - dot) / 2, dot, dot);
            TextRenderer.DrawText(g, Text, Font,
                new Rectangle(UiTheme.Px(TextLeft), 0, Width - UiTheme.Px(TextLeft) - UiTheme.Px(6), Height), pillFore,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }
}
