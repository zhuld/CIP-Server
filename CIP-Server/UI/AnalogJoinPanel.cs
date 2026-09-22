// ============================================================================
//  CIP-Server — Crestron CIP 协议模拟服务器（MIT License · 代码由 AI 辅助生成）
//  文件: UI/AnalogJoinPanel.cs
//  说明: 模拟 join 监视面板。进度条列不使用控件，而是在 CellPainting 事件中
//        手绘（圆角轨道 + 主色填充，填充比例 = 值/65535），性能远优于每行放一个
//        ProgressBar 控件；数值文字在填充过半时转白以保证对比度。
// ============================================================================

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using CipServerSim.State;

namespace CipServerSim.UI
{
    /// <summary>模拟 join 监视面板：数值 + 进度条可视化</summary>
    sealed class AnalogJoinPanel : JoinPanelBase
    {
        NumericUpDown numJoin, numValue;
        const int ProgressCol = 2;

        /// <summary>进度条轨道底色（比 UiTheme.Border 更浅，避免轨道压过填充色）</summary>
        static readonly Color TrackColor = Color.FromArgb(237, 242, 247);

        public AnalogJoinPanel()
        {
            var bar = new ToolStrip { Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden };
            numJoin = new NumericUpDown { Minimum = 1, Maximum = 65535, Width = 70 };
            numValue = new NumericUpDown { Minimum = 0, Maximum = 65535, Width = 80 };
            UiTheme.StyleInput(numJoin);
            UiTheme.StyleInput(numValue);
            var btn = new ToolStripButton { Text = "设置模拟值" };
            btn.Click += delegate { SetSelected(); };
            bar.Items.AddRange(new ToolStripItem[]
            {
                new ToolStripLabel { Text = "Join 号:" },
                new ToolStripControlHost(numJoin),
                new ToolStripLabel { Text = "  值 (0-65535):" },
                new ToolStripControlHost(numValue),
                btn
            });
            StyleBar(bar);
            Controls.Add(bar);
            grid.CellPainting += Grid_CellPainting;
        }

        protected override void OnConfigApplied()
        {
            if (config == null) return;
            numJoin.Maximum = Math.Max(1, config.AnalogJoins);
            numJoin.Value = 1;
        }

        void SetSelected()
        {
            if (joins == null) return;
            joins.SetAnalog((int)numJoin.Value, (int)numValue.Value, JoinSource.Local, "UI 手动");
        }

        protected override void BuildColumns()
        {
            grid.Columns.Clear();
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "join", HeaderText = "Join", FillWeight = 10 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "value", HeaderText = "数值", FillWeight = 12 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "progress", HeaderText = "进度 (0-65535)", FillWeight = 40 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "changed", HeaderText = "最后变化", FillWeight = 24 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "source", HeaderText = "来源", FillWeight = 14 });
        }

        protected override void PopulateRows()
        {
            grid.Rows.Clear();
            for (int j = 1; j <= config.AnalogJoins; j++)
            {
                int v = joins.GetAnalog(j);
                grid.Rows.Add(j, v, "", joins.GetAnalogTime(j).ToString("HH:mm:ss.fff"), joins.GetAnalogInfo(j));
            }
        }

        protected override void Attach() { joins.AnalogChanged += OnChanged; }
        protected override void Detach() { if (joins != null) joins.AnalogChanged -= OnChanged; }

        void OnChanged(int join, int value, JoinSource src, string info)
        {
            if (!IsHandleCreated) return;
            BeginInvoke((MethodInvoker)delegate { Mark(join); });
        }

        protected override void ApplyUpdate(int join)
        {
            if (config == null || join < 1 || join > config.AnalogJoins) return;
            var row = grid.Rows[join - 1];
            row.Cells[1].Value = joins.GetAnalog(join);
            row.Cells[3].Value = joins.GetAnalogTime(join).ToString("HH:mm:ss.fff");
            row.Cells[4].Value = joins.GetAnalogInfo(join);
            grid.InvalidateCell(ProgressCol, join - 1);
        }

        void Grid_CellPainting(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex != ProgressCol || joins == null) return;
            e.PaintBackground(e.CellBounds, true);
            e.Handled = true;
            int v = joins.GetAnalog(e.RowIndex + 1);
            var track = new Rectangle(e.CellBounds.X + 8, e.CellBounds.Y + 8, e.CellBounds.Width - 16, e.CellBounds.Height - 16);
            if (track.Width < 8 || track.Height < 6) return;

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int radius = track.Height / 2;
            UiTheme.FillRounded(e.Graphics, track, radius, TrackColor);

            int filled = (int)Math.Round(track.Width * (v / 65535.0));
            if (filled > 0)
            {
                if (filled < track.Height) filled = track.Height;      // 保证圆角可见
                var fill = new Rectangle(track.X, track.Y, Math.Min(filled, track.Width), track.Height);
                UiTheme.FillRounded(e.Graphics, fill, radius, v > 0 ? UiTheme.Accent : UiTheme.BorderStrong);
            }

            // 数值：填充过半时用白字，否则用深灰字，保证任何比例下都可读
            bool overHalf = filled > track.Width / 2;
            TextRenderer.DrawText(e.Graphics, v.ToString(), UiTheme.FontSmall, track,
                overHalf ? Color.White : UiTheme.TextSecondary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }
}
