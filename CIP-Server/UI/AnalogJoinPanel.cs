// ============================================================================
//  CIP-Server — Crestron CIP 协议模拟服务器（MIT License · 代码由 AI 辅助生成）
//  文件: UI/AnalogJoinPanel.cs
//  说明: 模拟 join 监视面板。进度条列不使用控件，而是在 CellPainting 事件中
//        手绘（填充比例 = 值/65535），性能远优于每行放一个 ProgressBar 控件。
// ============================================================================

using System;
using System.Drawing;
using System.Windows.Forms;
using CipServerSim.State;

namespace CipServerSim.UI
{
    /// <summary>模拟 join 监视面板：数值 + 进度条可视化</summary>
    sealed class AnalogJoinPanel : JoinPanelBase
    {
        NumericUpDown numJoin, numValue;
        const int ProgressCol = 2;

        public AnalogJoinPanel()
        {
            var bar = new ToolStrip { Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden };
            numJoin = new NumericUpDown { Minimum = 1, Maximum = 65535, Width = 70 };
            numValue = new NumericUpDown { Minimum = 0, Maximum = 65535, Width = 80 };
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
            var rect = new Rectangle(e.CellBounds.X + 6, e.CellBounds.Y + 7, e.CellBounds.Width - 12, e.CellBounds.Height - 14);
            if (rect.Width > 0 && rect.Height > 0)
            {
                using (var back = new SolidBrush(Color.FromArgb(238, 238, 238)))
                    e.Graphics.FillRectangle(back, rect);
                int w = (int)(rect.Width * (v / 65535.0));
                if (w > 0)
                    using (var fill = new SolidBrush(Color.FromArgb(0, 122, 204)))
                        e.Graphics.FillRectangle(fill, rect.X, rect.Y, w, rect.Height);
                using (var pen = new Pen(Color.FromArgb(190, 190, 190)))
                    e.Graphics.DrawRectangle(pen, rect);
                TextRenderer.DrawText(e.Graphics, v.ToString(), grid.Font, rect,
                    Color.FromArgb(40, 40, 40), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }
    }
}
