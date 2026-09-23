// ============================================================================
//  CIP-Server — Crestron CIP 协议模拟服务器（MIT License · 代码由 AI 辅助生成）
//  文件: UI/SegmentedTabs.cs
//  说明: 分段式选项卡。顶部为一体化分段按钮（Fluent / iOS 风格的分段控件），
//        选中项为白色圆角"滑块"+ 主色文字，替代系统 TabControl 的凸起页签；
//        下方内容区按 Dock 填充，切换时只切换内容控件的 Visible，不重建控件。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CipServerSim.UI
{
    /// <summary>分段式选项卡：分段按钮头 + 内容区</summary>
    sealed class SegmentedTabs : UserControl
    {
        sealed class TabEntry
        {
            public string Title;
            public Control Content;
        }

        const int HeaderHeight = 36;
        const int HeaderGap = 10;

        readonly List<TabEntry> entries = new List<TabEntry>();
        readonly Panel contentHost;
        Rectangle[] itemRects = new Rectangle[0];
        int selectedIndex = -1;
        int hoverIndex = -1;
        bool headerVisible = true;

        public SegmentedTabs()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = UiTheme.CardBack;
            Font = UiTheme.FontBase;
            TabStop = false;
            // 内容区不使用 Dock=Fill：Dock 填充会连页签区域一起盖住（子控件后绘制），
            // 因此改为按 Bounds 定位到页签下方。
            contentHost = new Panel { BackColor = UiTheme.CardBack, TabStop = false };
            Controls.Add(contentHost);
        }

        /// <summary>页签区域总高度（页签高 + 与内容区的间距），内容区据此定位</summary>
        public int HeaderAreaHeight { get { return HeaderHeight + HeaderGap; } }

        /// <summary>隐藏/显示页签头：隐藏时内容区上移占满整个控件（精简模式用）</summary>
        public void SetHeaderVisible(bool visible)
        {
            if (headerVisible == visible) return;
            headerVisible = visible;
            LayoutContent();
            Invalidate();
        }

        /// <summary>添加页签并承载内容控件（首个页签自动选中）</summary>
        public void AddTab(string title, Control content)
        {
            if (content == null) return;
            entries.Add(new TabEntry { Title = title, Content = content });
            content.Dock = DockStyle.Fill;
            content.BackColor = UiTheme.CardBack;
            content.Visible = false;
            contentHost.Controls.Add(content);
            LayoutRects();
            if (selectedIndex < 0) Select(0);
            Invalidate();
        }

        /// <summary>切换选中页：仅切换内容控件的 Visible，不重建控件</summary>
        void Select(int index)
        {
            if (index < 0 || index >= entries.Count || index == selectedIndex) return;
            selectedIndex = index;
            for (int i = 0; i < entries.Count; i++)
                entries[i].Content.Visible = (i == index);
            Invalidate();
        }

        void LayoutRects()
        {
            int count = entries.Count;
            itemRects = new Rectangle[count];
            for (int i = 0; i < count; i++)
            {
                int left = i * Width / count;
                int right = (i + 1) * Width / count;
                itemRects[i] = new Rectangle(left, 0, Math.Max(1, right - left), HeaderHeight);
            }
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            LayoutRects();
            LayoutContent();
        }

        /// <summary>把内容区放到页签下方（页签头隐藏时直接占满）；子控件绘制顺序在父控件之后，重叠会盖住页签</summary>
        void LayoutContent()
        {
            if (contentHost == null) return;
            int top = headerVisible ? HeaderAreaHeight : 0;
            contentHost.Bounds = new Rectangle(0, top, Math.Max(0, Width), Math.Max(0, Height - top));
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            LayoutContent();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);
            if (!headerVisible || entries.Count == 0) return;
            if (itemRects.Length != entries.Count) LayoutRects();

            UiTheme.FillRounded(g, new Rectangle(0, 0, Width, HeaderHeight), 8, UiTheme.TrackBack);

            for (int i = 0; i < entries.Count; i++)
            {
                var inner = new Rectangle(itemRects[i].X + 3, 3, itemRects[i].Width - 6, HeaderHeight - 6);
                if (inner.Width <= 0) continue;
                bool selected = (i == selectedIndex);
                if (selected)
                {
                    UiTheme.FillRounded(g, inner, 6, UiTheme.CardBack);
                    UiTheme.StrokeRounded(g, inner, 6, UiTheme.Border);
                }
                else if (i == hoverIndex)
                {
                    UiTheme.FillRounded(g, inner, 6, UiTheme.HoverBack);
                }
                TextRenderer.DrawText(g, entries[i].Title, selected ? UiTheme.FontBold : UiTheme.FontBase, inner,
                    selected ? UiTheme.Accent : UiTheme.TextSecondary,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            }
        }

        int IndexAt(Point p)
        {
            for (int i = 0; i < itemRects.Length; i++)
                if (itemRects[i].Contains(p)) return i;
            return -1;
        }

        void SetHover(int index)
        {
            if (index == hoverIndex) return;
            hoverIndex = index;
            Cursor = index >= 0 ? Cursors.Hand : Cursors.Default;
            Invalidate(new Rectangle(0, 0, Width, HeaderHeight), false);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            SetHover(e.Y <= HeaderHeight ? IndexAt(e.Location) : -1);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            SetHover(-1);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            int index = IndexAt(e.Location);
            if (index >= 0) Select(index);
        }
    }
}
