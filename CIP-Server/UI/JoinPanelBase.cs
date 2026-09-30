// ============================================================================
//  CIP-Server — Crestron CIP 协议模拟服务器（MIT License · 代码由 AI 辅助生成）
//  文件: UI/JoinPanelBase.cs
//  说明: 三个 join 监视面板（数字/模拟/串口）的公共基类。
//  视觉: 使用 UiTheme.StyleGrid 统一现代表格风格（无立体表头、行高 28，
//        交替行浅底、无竖线），并使用双缓冲网格（BufferedGrid）消除刷新闪烁。
//        精简模式经 SetCompact() 切换到更矮的密度档（行高 24 / 表头 26 / 内边距收紧）。
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
        NumericUpDown numGoto;   // 「定位 Join」输入框；未调用 AddGotoItems 的子类为 null
        protected bool compact;  // 精简模式密度档（子类手绘尺寸据此收紧）

        /// <summary>
        /// 精简模式密度切换：行高 28→24、列表头 32→26、单元格与工具条内边距收紧，
        /// 让小窗口内可见行更多。未绑定数据源时同样生效（先改 RowTemplate，后续建行沿用）。
        /// 退出精简模式时全部还原为 StyleGrid 的原始尺寸。
        /// </summary>
        public void SetCompact(bool value)
        {
            compact = value;
            int rowH = UiTheme.Px(value ? 24 : 28);
            grid.RowTemplate.Height = rowH;
            grid.ColumnHeadersHeight = UiTheme.Px(value ? 26 : 32);
            grid.DefaultCellStyle.Padding = value ? UiTheme.Px(8, 0, 4, 0) : UiTheme.Px(10, 0, 6, 0);
            grid.ColumnHeadersDefaultCellStyle.Padding = value ? UiTheme.Px(8, 0, 4, 0) : UiTheme.Px(10, 0, 0, 0);
            // 已存在的行不会跟随 RowTemplate，需逐行同步（行序 = join 号，索引器访问会取消共享行）
            grid.SuspendLayout();
            try
            {
                for (int i = 0; i < grid.Rows.Count; i++) grid.Rows[i].Height = rowH;
            }
            finally { grid.ResumeLayout(); }
            foreach (Control c in Controls)
            {
                var bar = c as ToolStrip;
                if (bar != null) bar.Padding = value ? UiTheme.Px(2, 2, 2, 2) : UiTheme.Px(2, 3, 2, 3);
            }
            grid.Invalidate();
        }

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

        /// <summary>空状态提示：无可见行（未启动服务器或被过滤隐藏）时居中显示灰字说明</summary>
        void Grid_Paint(object sender, PaintEventArgs e)
        {
            if (grid.Rows.Count > 0 && HasVisibleRow()) return;
            string text = EmptyText;
            if (string.IsNullOrEmpty(text)) return;
            TextRenderer.DrawText(e.Graphics, text, UiTheme.FontBase, grid.ClientRectangle,
                UiTheme.TextMuted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
        }

        /// <summary>表格无可见行时显示的说明文字（子类可覆盖，返回空串表示不显示）</summary>
        protected virtual string EmptyText
        {
            get { return "未启动服务器 · 启动后将在此显示 join 状态"; }
        }

        /// <summary>是否存在可见行（通常第一行即可判定，开销可忽略）</summary>
        bool HasVisibleRow()
        {
            foreach (DataGridViewRow row in grid.Rows) if (row.Visible) return true;
            return false;
        }

        /// <summary>给工具条应用扁平主题（请在添加完所有子项后调用）</summary>
        protected static void StyleBar(ToolStrip bar)
        {
            UiTheme.StyleToolStrip(bar, false);
        }

        /// <summary>
        /// 在工具条尾部追加「定位 Join: [号] [定位]」（join 数量多时无需滚动查找）。
        /// 必须在 StyleBar 之前调用，否则新增项不会被应用扁平主题。
        /// </summary>
        protected void AddGotoItems(ToolStrip bar)
        {
            numGoto = new NumericUpDown { Minimum = 1, Maximum = 65535, Width = 64 };
            UiTheme.StyleInput(numGoto);
            numGoto.KeyDown += OnGotoKeyDown;
            var btn = new ToolStripButton { Text = "定位" };
            btn.Click += delegate { GotoRow((int)numGoto.Value); };
            bar.Items.AddRange(new ToolStripItem[]
            {
                new ToolStripSeparator(),
                new ToolStripLabel { Text = "定位 Join:" },
                UiTheme.HostControl(numGoto),
                btn
            });
        }

        /// <summary>定位框范围随 join 数量刷新（未添加定位控件时无操作）</summary>
        void SyncGotoRange()
        {
            if (numGoto == null) return;
            int max = Math.Max(1, grid.Rows.Count);
            numGoto.Maximum = max;
            if (numGoto.Value > max) numGoto.Value = max;
        }

        /// <summary>定位框回车即跳转，省去每次点击按钮</summary>
        void OnGotoKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;    // 阻止输入框发出提示音
            GotoRow((int)numGoto.Value);
        }

        /// <summary>滚动并选中指定 join 对应的行；越界或被过滤隐藏时静默忽略</summary>
        protected void GotoRow(int join)
        {
            if (join < 1 || join > grid.Rows.Count) return;
            DataGridViewRow row = grid.Rows[join - 1];
            if (!row.Visible) return;    // 隐藏行不能作为当前行，强行设置会抛异常
            try
            {
                grid.CurrentCell = row.Cells[0];
                grid.FirstDisplayedScrollingRowIndex = row.Index;
            }
            catch (InvalidOperationException) { }        // 网格尚未完成布局时忽略
            catch (ArgumentOutOfRangeException) { }
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
            SyncGotoRange();
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
