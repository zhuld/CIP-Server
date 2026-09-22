// ============================================================================
//  CIP-Server — Crestron CIP 协议模拟服务器（MIT License · 代码由 AI 辅助生成）
//  文件: UI/CardPanel.cs
//  说明: 圆角卡片面板。自绘圆角背景 + 1px 细边框；圆角外的四个角填充 BackColor，
//        因此 BackColor 必须设置为父容器底色（浅色卡片用窗体底、嵌套卡片用卡片白），
//        子控件需通过 Padding 内缩，避免方角盖住卡片圆角。
// ============================================================================

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CipServerSim.UI
{
    /// <summary>圆角卡片面板：圆角白底 + 细边框（Fluent 卡片风格）</summary>
    sealed class CardPanel : Panel
    {
        int radius = 10;
        Color cardColor = UiTheme.CardBack;
        Color cardBorder = UiTheme.Border;
        bool showBorder = true;

        public CardPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = UiTheme.WindowBack;
            Margin = new Padding(0);
        }

        /// <summary>圆角半径（像素）</summary>
        public int Radius
        {
            get { return radius; }
            set { radius = value; Invalidate(); }
        }

        /// <summary>卡片填充色</summary>
        public Color CardColor
        {
            get { return cardColor; }
            set { cardColor = value; Invalidate(); }
        }

        /// <summary>卡片边框色</summary>
        public Color CardBorder
        {
            get { return cardBorder; }
            set { cardBorder = value; Invalidate(); }
        }

        /// <summary>是否绘制边框</summary>
        public bool ShowCardBorder
        {
            get { return showBorder; }
            set { showBorder = value; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(BackColor);
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            UiTheme.FillRounded(e.Graphics, rect, radius, cardColor);
            if (showBorder) UiTheme.StrokeRounded(e.Graphics, rect, radius, cardBorder);
            base.OnPaint(e);
        }
    }
}
