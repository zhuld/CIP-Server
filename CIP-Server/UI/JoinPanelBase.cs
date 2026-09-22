// ============================================================================
//  CIP-Server — Crestron CIP 协议模拟服务器（MIT License · 代码由 AI 辅助生成）
//  文件: UI/JoinPanelBase.cs
//  说明: 三个 join 监视面板（数字/模拟/串口）的公共基类。
//  视觉: 使用 UiTheme.StyleGrid 统一现代表格风格（无立体表头、行高 28、
//        交替行浅底、无竖线），并使用双缓冲网格（BufferedGrid）消除刷新闪烁。
//  刷新策略: 状态变更事件先经 BeginInvoke 封送到 UI 线程并记入 pending 集合，
//  再由 80ms 定时器批量应用（ApplyUpdate），把高频模拟 join 变化合并为
//  少量网格重绘，避免界面卡顿。
// ============================================================================

using System;
using System.Collections.Generic;
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
            BackColor = UiTheme.CardBack;
            grid = new BufferedGrid { Dock = DockStyle.Fill };
            UiTheme.StyleGrid(grid, true);
            grid.Paint += Grid_Paint;
            Controls.Add(grid);

            flushTimer = new System.Windows.Forms.Timer { Interval = 80 };
            flushTimer.Tick += FlushPending;
        }

        /// <summary>空状态提示：未启动服务器（表格无行）时居中显示灰字说明</summary>
        void Grid_Paint(object sender, PaintEventArgs e)
        {
            if (grid.Rows.Count > 0) return;
            TextRenderer.DrawText(e.Graphics, "未启动服务器 · 启动后将在此显示 join 状态",
                UiTheme.FontBase, grid.ClientRectangle, UiTheme.TextMuted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        /// <summary>给工具条应用扁平主题（请在添加完所有子项后调用）</summary>
        protected static void StyleBar(ToolStrip bar)
        {
            UiTheme.StyleToolStrip(bar, false);
        }

        /// <summary>
        /// 绑定 join 状态数据源并填充网格（由主窗体在服务器启动后调用）。
        /// 重复调用会先解绑旧数据源，因此可用于“停止 → 再次启动”的循环。
        /// </summary>
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

        /// <summary>停止刷新并解绑数据源（由主窗体在服务器停止时调用）</summary>
        public void Shutdown()
        {
            flushTimer.Stop();
            lock (pending) pending.Clear();       // 丢弃未应用的变更，避免下次 Init 后误刷新旧行
            if (joins != null) { Detach(); joins = null; }
        }

        /// <summary>数据源就绪后的自定义初始化（如设置输入控件范围）</summary>
        protected virtual void OnConfigApplied() { }

        protected abstract void BuildColumns();
        protected abstract void PopulateRows();
        protected abstract void Attach();
        protected abstract void Detach();

        /// <summary>把 join 的最新状态写入表格；row 索引约定为 join - 1（即行序 = join 号升序）</summary>
        protected abstract void ApplyUpdate(int join);

        /// <summary>标记某 join 需要刷新（可在任意线程调用，由 80ms 定时器批量应用）</summary>
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
