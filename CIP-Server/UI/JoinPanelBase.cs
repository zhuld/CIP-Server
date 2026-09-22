// ============================================================================
//  CIP-Server — Crestron CIP 协议模拟服务器（MIT License · 代码由 AI 辅助生成）
//  文件: UI/JoinPanelBase.cs
//  说明: 三个 join 监视面板（数字/模拟/串口）的公共基类。
//  刷新策略: 状态变更事件先经 BeginInvoke 封送到 UI 线程并记入 pending 集合，
//  再由 80ms 定时器批量应用（ApplyUpdate），把高频模拟 join 变化合并为
//  少量网格重绘，避免界面卡顿。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using CipServerSim.State;

namespace CipServerSim.UI
{
    /// <summary>Join 监视面板基类：DataGridView + 变更节流刷新</summary>
    abstract class JoinPanelBase : UserControl
    {
        protected JoinState joins;
        protected DeviceConfig config;
        protected DataGridView grid;
        System.Windows.Forms.Timer flushTimer;
        readonly HashSet<int> pending = new HashSet<int>();

        protected JoinPanelBase()
        {
            grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.None,
                GridColor = Color.FromArgb(220, 220, 220),
                EnableHeadersVisualStyles = false
            };
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(236, 240, 246);
            grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(4);
            grid.ColumnHeadersHeight = 28;
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(204, 228, 247);
            grid.DefaultCellStyle.SelectionForeColor = Color.Black;
            grid.RowTemplate.Height = 26;
            Controls.Add(grid);

            flushTimer = new System.Windows.Forms.Timer { Interval = 80 };
            flushTimer.Tick += FlushPending;
        }

        public void Init(JoinState newJoins, DeviceConfig cfg)
        {
            if (joins != null) Detach();
            joins = newJoins;
            config = cfg;
            BuildColumns();
            PopulateRows();
            Attach();
            OnConfigApplied();
            flushTimer.Start();
        }

        public void Shutdown()
        {
            flushTimer.Stop();
            if (joins != null) { Detach(); joins = null; }
        }

        protected virtual void OnConfigApplied() { }
        protected abstract void BuildColumns();
        protected abstract void PopulateRows();
        protected abstract void Attach();
        protected abstract void Detach();
        protected abstract void ApplyUpdate(int join);

        protected void Mark(int join)
        {
            lock (pending) { pending.Add(join); }
        }

        void FlushPending(object sender, EventArgs e)
        {
            int[] list;
            lock (pending)
            {
                if (pending.Count == 0) return;
                list = new int[pending.Count];
                pending.CopyTo(list);
                pending.Clear();
            }
            foreach (int j in list) ApplyUpdate(j);
        }
    }
}
