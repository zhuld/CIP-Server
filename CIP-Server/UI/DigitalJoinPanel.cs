using System;
using System.Drawing;
using System.Windows.Forms;
using CipServerSim.State;

namespace CipServerSim.UI
{
    /// <summary>数字 join 监视面板：状态高亮，双击行切换，支持脉冲</summary>
    sealed class DigitalJoinPanel : JoinPanelBase
    {
        System.Windows.Forms.Timer pulseTimer;
        int pulseJoin;

        public DigitalJoinPanel()
        {
            var bar = new ToolStrip { Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden };
            var btnHigh = new ToolStripButton { Text = "置高" };
            var btnLow = new ToolStripButton { Text = "置低" };
            var btnPulse = new ToolStripButton { Text = "脉冲 500ms" };
            var tip = new ToolStripLabel { Text = "   选中行后操作 · 双击行可直接切换状态" };
            btnHigh.Click += delegate { SetSelected(true); };
            btnLow.Click += delegate { SetSelected(false); };
            btnPulse.Click += delegate { PulseSelected(); };
            bar.Items.AddRange(new ToolStripItem[] { btnHigh, new ToolStripSeparator(), btnLow, new ToolStripSeparator(), btnPulse, tip });
            Controls.Add(bar);
            grid.CellDoubleClick += Grid_CellDoubleClick;
            pulseTimer = new System.Windows.Forms.Timer { Interval = 500 };
            pulseTimer.Tick += PulseTimer_Tick;
        }

        void SetSelected(bool state)
        {
            if (grid.CurrentRow == null || joins == null) return;
            int join = Convert.ToInt32(grid.CurrentRow.Cells[0].Value);
            joins.SetDigital(join, state, JoinSource.Local, "UI 手动");
        }

        void PulseSelected()
        {
            if (grid.CurrentRow == null || joins == null) return;
            int join = Convert.ToInt32(grid.CurrentRow.Cells[0].Value);
            joins.SetDigital(join, true, JoinSource.Local, "UI 脉冲");
            pulseJoin = join;
            pulseTimer.Start();
        }

        void PulseTimer_Tick(object sender, EventArgs e)
        {
            pulseTimer.Stop();
            if (joins != null && pulseJoin >= 1)
                joins.SetDigital(pulseJoin, false, JoinSource.Local, "UI 脉冲结束");
        }

        void Grid_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || joins == null) return;
            int join = Convert.ToInt32(grid.Rows[e.RowIndex].Cells[0].Value);
            bool cur = joins.GetDigital(join);
            joins.SetDigital(join, !cur, JoinSource.Local, "UI 手动");
        }

        protected override void BuildColumns()
        {
            grid.Columns.Clear();
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "join", HeaderText = "Join", FillWeight = 14 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "state", HeaderText = "状态", FillWeight = 14 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "changed", HeaderText = "最后变化", FillWeight = 40 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "source", HeaderText = "来源", FillWeight = 32 });
        }

        protected override void PopulateRows()
        {
            grid.Rows.Clear();
            for (int j = 1; j <= config.DigitalJoins; j++)
            {
                bool st = joins.GetDigital(j);
                int idx = grid.Rows.Add(j, st ? "高" : "低",
                    joins.GetDigitalTime(j).ToString("HH:mm:ss.fff"), joins.GetDigitalInfo(j));
                grid.Rows[idx].DefaultCellStyle.BackColor = st ? Color.FromArgb(198, 239, 206) : Color.White;
            }
        }

        protected override void Attach() { joins.DigitalChanged += OnChanged; }
        protected override void Detach() { if (joins != null) joins.DigitalChanged -= OnChanged; }

        void OnChanged(int join, bool state, JoinSource src, string info)
        {
            if (!IsHandleCreated) return;
            BeginInvoke((MethodInvoker)delegate { Mark(join); });
        }

        protected override void ApplyUpdate(int join)
        {
            if (config == null || join < 1 || join > config.DigitalJoins) return;
            var row = grid.Rows[join - 1];
            bool st = joins.GetDigital(join);
            row.Cells[1].Value = st ? "高" : "低";
            row.Cells[2].Value = joins.GetDigitalTime(join).ToString("HH:mm:ss.fff");
            row.Cells[3].Value = joins.GetDigitalInfo(join);
            row.DefaultCellStyle.BackColor = st ? Color.FromArgb(198, 239, 206) : Color.White;
        }
    }
}
