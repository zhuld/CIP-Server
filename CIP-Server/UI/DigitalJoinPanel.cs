// ============================================================================
//  CIP-Server — Crestron CIP 协议模拟服务器（MIT License · 代码由 AI 辅助生成）
//  文件: UI/DigitalJoinPanel.cs
//  说明: 数字 join 监视面板。高电平行绿色底色高亮；双击行直接切换状态；
//        工具栏支持对选中行置高/置低/脉冲（脉冲时长可选，到期自动回落低电平），
//        支持 Ctrl/Shift 多选后批量操作，以及「仅高电平」过滤与「定位 Join」跳转。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Windows.Forms;
using CipServerSim.State;

namespace CipServerSim.UI
{
    /// <summary>数字 join 监视面板：状态高亮，双击切换，支持批量置位/脉冲与仅高电平过滤</summary>
    sealed class DigitalJoinPanel : JoinPanelBase
    {
        /// <summary>脉冲时长下拉项（毫秒），与 cboPulse 显示文本一一对应</summary>
        static readonly int[] PulseChoices = { 100, 200, 500, 1000, 2000 };

        System.Windows.Forms.Timer pulseTimer;
        readonly HashSet<int> pulseJoins = new HashSet<int>();   // 正在脉冲中的 join，到期统一回落
        ComboBox cboPulse;
        ToolStripButton btnHighOnly;
        ToolStripLabel lblTip;

        public DigitalJoinPanel()
        {
            grid.MultiSelect = true;   // 允许 Ctrl/Shift 多选后批量置高/置低/脉冲

            var bar = new ToolStrip { Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden };
            var btnHigh = new ToolStripButton { Text = "置高" };
            var btnLow = new ToolStripButton { Text = "置低" };
            var btnPulse = new ToolStripButton { Text = "脉冲" };
            cboPulse = new ComboBox { Width = 84, DropDownStyle = ComboBoxStyle.DropDownList };
            cboPulse.Items.AddRange(new object[] { "100 ms", "200 ms", "500 ms", "1 s", "2 s" });
            UiTheme.StyleInput(cboPulse);
            cboPulse.SelectedIndex = 2;                                   // 默认 500ms，与旧行为一致
            btnHighOnly = new ToolStripButton { Text = "仅高电平", CheckOnClick = true };
            btnHighOnly.Click += delegate { ApplyHighFilter(); };
            btnHigh.Click += delegate { SetSelected(true); };
            btnLow.Click += delegate { SetSelected(false); };
            btnPulse.Click += delegate { PulseSelected(); };
            bar.Items.AddRange(new ToolStripItem[]
            {
                btnHigh, new ToolStripSeparator(), btnLow, new ToolStripSeparator(),
                btnPulse, UiTheme.HostControl(cboPulse), new ToolStripSeparator(), btnHighOnly
            });
            AddGotoItems(bar);
            lblTip = new ToolStripLabel { Text = "   选中行后操作 · 双击行可直接切换状态" };
            bar.Items.Add(lblTip);   // 提示语放最后：窗口变窄时优先被裁剪，不影响按钮可用
            StyleBar(bar);
            Controls.Add(bar);
            grid.CellDoubleClick += Grid_CellDoubleClick;
            pulseTimer = new System.Windows.Forms.Timer { Interval = 500 };
            pulseTimer.Tick += PulseTimer_Tick;
        }

        /// <summary>取所有选中行的 join 号（无选中行时回退到当前行）</summary>
        List<int> SelectedJoins()
        {
            var list = new List<int>();
            foreach (DataGridViewRow row in grid.SelectedRows)
            {
                object v = row.Cells[0].Value;
                if (v != null) list.Add(Convert.ToInt32(v));
            }
            if (list.Count == 0 && grid.CurrentRow != null)
            {
                object v = grid.CurrentRow.Cells[0].Value;
                if (v != null) list.Add(Convert.ToInt32(v));
            }
            return list;
        }

        void SetSelected(bool state)
        {
            if (joins == null) return;
            foreach (int join in SelectedJoins())
                joins.SetDigital(join, state, JoinSource.Local, "UI 手动");
        }

        void PulseSelected()
        {
            if (joins == null) return;
            var targets = SelectedJoins();
            if (targets.Count == 0) return;
            pulseTimer.Interval = PulseInterval();
            foreach (int join in targets)
            {
                joins.SetDigital(join, true, JoinSource.Local, "UI 脉冲");
                pulseJoins.Add(join);
            }
            pulseTimer.Start();   // 重复点击会重置计时，保证最后一次点击的时长生效
        }

        /// <summary>当前下拉选择对应的脉冲时长（毫秒）</summary>
        int PulseInterval()
        {
            int i = cboPulse.SelectedIndex;
            if (i < 0 || i >= PulseChoices.Length) i = 2;   // 异常时回退 500ms
            return PulseChoices[i];
        }

        /// <summary>脉冲到期：所有脉冲中的 join 一并回落低电平</summary>
        void PulseTimer_Tick(object sender, EventArgs e)
        {
            pulseTimer.Stop();
            if (pulseJoins.Count == 0) return;
            int[] done = new int[pulseJoins.Count];
            pulseJoins.CopyTo(done);
            pulseJoins.Clear();          // 先清空再回调，避免 SetDigital 期间集合被改动
            if (joins == null) return;
            foreach (int join in done)
                joins.SetDigital(join, false, JoinSource.Local, "UI 脉冲结束");
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
                ApplyRowStyle(grid.Rows[idx], st);
            }
        }

        /// <summary>行样式：高电平 = 浅绿底 + 绿色加粗状态，低电平 = 常规白底</summary>
        static void ApplyRowStyle(DataGridViewRow row, bool state)
        {
            row.DefaultCellStyle.BackColor = state ? UiTheme.RowHigh : UiTheme.CardBack;
            row.Cells[1].Style.ForeColor = state ? UiTheme.SuccessText : UiTheme.TextMuted;
            row.Cells[1].Style.Font = state ? UiTheme.FontBold : UiTheme.FontBase;
        }

        protected override void Attach() { joins.DigitalChanged += OnChanged; }
        protected override void Detach() { if (joins != null) joins.DigitalChanged -= OnChanged; }

        void OnChanged(int join, bool state, JoinSource src, string info)
        {
            if (!IsHandleCreated) return;
            BeginInvoke((MethodInvoker)delegate { Mark(join); });
        }

        protected override void OnConfigApplied() { ApplyHighFilter(); }

        /// <summary>过滤开启且无任何高电平行时给出说明，避免表格一片空白让人误判为故障</summary>
        protected override string EmptyText
        {
            get
            {
                if (joins == null) return "未启动服务器 · 启动后将在此显示 join 状态";
                return btnHighOnly.Checked ? "当前没有高电平 join · 关闭【仅高电平】可查看全部" : "";
            }
        }

        /// <summary>
        /// 「仅高电平」过滤：用行 Visible 隐藏低电平行（行索引仍与 join 号一一对应）。
        /// 隐藏当前行会抛异常，因此先清除当前单元格。
        /// </summary>
        void ApplyHighFilter()
        {
            bool onlyHigh = btnHighOnly.Checked;
            try { grid.CurrentCell = null; }
            catch (InvalidOperationException) { }
            grid.SuspendLayout();
            try
            {
                foreach (DataGridViewRow row in grid.Rows)
                    row.Visible = !onlyHigh || IsHigh(row);
            }
            finally
            {
                grid.ResumeLayout();
            }
            UpdateTip();
        }

        /// <summary>行索引 ↔ join 号一一对应，状态一律以 JoinState 为准</summary>
        bool IsHigh(DataGridViewRow row)
        {
            return joins != null && joins.GetDigital(row.Index + 1);
        }

        /// <summary>刷新工具栏提示：过滤开启时显示可见行数</summary>
        void UpdateTip()
        {
            if (lblTip == null) return;
            if (!btnHighOnly.Checked)
            {
                lblTip.Text = "   选中行后操作 · 双击行可直接切换状态";
                return;
            }
            int shown = 0;
            foreach (DataGridViewRow row in grid.Rows) if (row.Visible) shown++;
            lblTip.Text = string.Format("   仅显示高电平 {0}/{1} · 双击行可切换状态", shown, grid.Rows.Count);
        }

        protected override void ApplyUpdate(int join)
        {
            if (config == null || join < 1 || join > config.DigitalJoins) return;
            var row = grid.Rows[join - 1];
            bool st = joins.GetDigital(join);
            row.Cells[1].Value = st ? "高" : "低";
            row.Cells[2].Value = joins.GetDigitalTime(join).ToString("HH:mm:ss.fff");
            row.Cells[3].Value = joins.GetDigitalInfo(join);
            ApplyRowStyle(row, st);
            // 过滤开启时：高电平的行随时出现、回落低电平的行随时消失（row.Visible 应与状态一致）
            if (btnHighOnly.Checked && row.Visible != st)
            {
                try { if (!st && grid.CurrentRow == row) grid.CurrentCell = null; }
                catch (InvalidOperationException) { }
                row.Visible = st;
                UpdateTip();
            }
        }
    }
}
