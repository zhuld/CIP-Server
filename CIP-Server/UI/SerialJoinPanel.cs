// ============================================================================
//  CIP-Server — Crestron CIP 协议模拟服务器（MIT License · 代码由 AI 辅助生成）
//  文件: UI/SerialJoinPanel.cs
//  说明: 串口 join 监视面板。显示每个 join 最后收发的文本内容、长度与时间，
//        鼠标悬停可看完整内容（ToolTip），顶部工具栏可向指定 join 发送文本。
// ============================================================================

using System;
using System.Windows.Forms;
using CipServerSim.State;

namespace CipServerSim.UI
{
    /// <summary>串口 join 监视面板：显示最后收发文本并可手动发送</summary>
    sealed class SerialJoinPanel : JoinPanelBase
    {
        NumericUpDown numJoin;
        TextBox txtText;

        public SerialJoinPanel()
        {
            var bar = new ToolStrip { Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden };
            numJoin = new NumericUpDown { Minimum = 1, Maximum = 65535, Width = 70 };
            txtText = new TextBox { Width = 300 };
            UiTheme.StyleInput(numJoin);
            UiTheme.StyleInput(txtText);
            var btn = new ToolStripButton { Text = "发送串口文本" };
            btn.Click += delegate { SendSelected(); };
            bar.Items.AddRange(new ToolStripItem[]
            {
                new ToolStripLabel { Text = "Join 号:" },
                new ToolStripControlHost(numJoin),
                new ToolStripLabel { Text = "  文本:" },
                new ToolStripControlHost(txtText),
                btn
            });
            StyleBar(bar);
            Controls.Add(bar);
        }

        protected override void OnConfigApplied()
        {
            if (config == null) return;
            numJoin.Maximum = Math.Max(1, config.SerialJoins);
            numJoin.Value = 1;
        }

        void SendSelected()
        {
            if (joins == null) return;
            joins.SetSerial((int)numJoin.Value, txtText.Text, JoinSource.Local, "UI 手动");
        }

        protected override void BuildColumns()
        {
            grid.Columns.Clear();
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "join", HeaderText = "Join", FillWeight = 10 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "text", HeaderText = "最后收发内容", FillWeight = 54 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "len", HeaderText = "长度", FillWeight = 10 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "changed", HeaderText = "最后变化", FillWeight = 16 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "source", HeaderText = "来源", FillWeight = 10 });
        }

        protected override void PopulateRows()
        {
            grid.Rows.Clear();
            for (int j = 1; j <= config.SerialJoins; j++)
            {
                string s = joins.GetSerial(j);
                int idx = grid.Rows.Add(j, s, s.Length,
                    joins.GetSerialTime(j).ToString("HH:mm:ss.fff"), joins.GetSerialInfo(j));
                grid.Rows[idx].Cells[1].ToolTipText = s;
            }
        }

        protected override void Attach() { joins.SerialChanged += OnChanged; }
        protected override void Detach() { if (joins != null) joins.SerialChanged -= OnChanged; }

        void OnChanged(int join, string text, JoinSource src, string info)
        {
            if (!IsHandleCreated) return;
            BeginInvoke((MethodInvoker)delegate { Mark(join); });
        }

        protected override void ApplyUpdate(int join)
        {
            if (config == null || join < 1 || join > config.SerialJoins) return;
            var row = grid.Rows[join - 1];
            string s = joins.GetSerial(join);
            row.Cells[1].Value = s;
            row.Cells[1].ToolTipText = s;
            row.Cells[2].Value = s.Length;
            row.Cells[3].Value = joins.GetSerialTime(join).ToString("HH:mm:ss.fff");
            row.Cells[4].Value = joins.GetSerialInfo(join);
        }
    }
}
