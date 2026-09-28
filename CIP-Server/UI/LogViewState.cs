// ============================================================================
//  CIP-Server — Crestron CIP 协议模拟服务器（MIT License · 代码由 AI 辅助生成）
//  文件: UI/LogViewState.cs
//  说明: 报文日志的界面侧缓冲。日志行进入固定容量环形缓冲（最旧记录自动淘汰），
//        界面再按「暂停 / 类型过滤 / 关键字搜索」条件从缓冲重建显示文本，因此
//        暂停期间到达的日志不会丢失，继续后可按原顺序完整回放；导出也复用同一
//        筛选结果，所见即所得。
//  注意: 本类只服务界面显示；Logging/Log.cs 的文件写入逻辑与之无关，日志行的
//        时间戳已由 Log.Emit 生成，这里不再重复添加。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Text;

namespace CipServerSim.UI
{
    /// <summary>日志行类型（决定显示颜色与过滤分组）</summary>
    enum LogLineKind
    {
        /// <summary>普通信息 / 运行状态</summary>
        Info,
        /// <summary>发往设备的报文（TX→）</summary>
        Tx,
        /// <summary>设备发来的报文（RX←）</summary>
        Rx,
        /// <summary>成功（√）</summary>
        Ok,
        /// <summary>失败 / 错误（×）</summary>
        Error
    }

    /// <summary>单条日志：类型 + 原始文本（文本已含 Log 生成的时间戳）</summary>
    sealed class LogRecord
    {
        public LogLineKind Kind;
        public string Text;
    }

    /// <summary>日志环形缓冲：容量固定，写入 O(1)，用于暂停/过滤/搜索后重建显示</summary>
    sealed class LogViewState
    {
        readonly LogRecord[] buffer;
        int head;    // 下一个写入位置
        int count;   // 当前记录数（≤ capacity）
        LogLineKind? matchFilter;   // 统计匹配数所用的筛选条件
        string matchSearch = "";

        public LogViewState(int capacity)
        {
            if (capacity < 1) capacity = 1;
            buffer = new LogRecord[capacity];
            for (int i = 0; i < capacity; i++) buffer[i] = new LogRecord();
        }

        public int Capacity { get { return buffer.Length; } }
        public int Count { get { return count; } }

        /// <summary>当前筛选条件下的记录数（随 Add 增量维护，高频日志下无需全量扫描）</summary>
        public int MatchCount { get; private set; }

        /// <summary>切换筛选条件并重算匹配数（仅在条件变化时调用一次）</summary>
        public void SetCriteria(LogLineKind? filter, string search)
        {
            matchFilter = filter;
            matchSearch = search ?? "";
            int n = 0;
            foreach (var r in Records)
                if (Match(r.Kind, r.Text, matchFilter, matchSearch)) n++;
            MatchCount = n;
        }

        /// <summary>按文本特征分类（与主窗体分色规则保持一致：TX→ / RX← / × / √）</summary>
        public static LogLineKind Classify(string line)
        {
            if (string.IsNullOrEmpty(line)) return LogLineKind.Info;
            if (line.Contains("TX→")) return LogLineKind.Tx;
            if (line.Contains("RX←")) return LogLineKind.Rx;
            if (line.Contains("×")) return LogLineKind.Error;
            if (line.Contains("√")) return LogLineKind.Ok;
            return LogLineKind.Info;
        }

        /// <summary>追加一行并返回其分类（复用记录对象，避免高频日志产生 GC 压力）</summary>
        public LogLineKind Add(string line)
        {
            LogLineKind kind = Classify(line);
            bool full = count == buffer.Length;
            if (full)
            {
                // 环已满：先扣掉即将被覆盖记录所占的匹配数，保证 MatchCount 始终精确
                LogRecord old = buffer[head];
                if (Match(old.Kind, old.Text, matchFilter, matchSearch)) MatchCount--;
            }
            LogRecord rec = buffer[head];
            rec.Kind = kind;
            rec.Text = line;
            if (Match(kind, line, matchFilter, matchSearch)) MatchCount++;
            head = (head + 1) % buffer.Length;
            if (!full) count++;
            return kind;
        }

        public void Clear()
        {
            head = 0;
            count = 0;
            MatchCount = 0;
        }

        /// <summary>按时间顺序（最旧 → 最新）遍历记录</summary>
        public IEnumerable<LogRecord> Records
        {
            get
            {
                int start = (head - count + buffer.Length) % buffer.Length;
                for (int i = 0; i < count; i++) yield return buffer[(start + i) % buffer.Length];
            }
        }

        /// <summary>判断一行是否符合当前过滤条件（filter 为 null 表示不限类型）</summary>
        public static bool Match(LogLineKind kind, string text, LogLineKind? filter, string search)
        {
            if (filter.HasValue && kind != filter.Value) return false;
            if (!string.IsNullOrEmpty(search) &&
                (text == null || text.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)) return false;
            return true;
        }

        /// <summary>把符合条件的记录拼成文本（重建显示与导出共用；行尾统一 \n 便于 RichTextBox 处理）</summary>
        public string BuildText(LogLineKind? filter, string search)
        {
            var sb = new StringBuilder(count * 64);
            foreach (var r in Records)
            {
                if (!Match(r.Kind, r.Text, filter, search)) continue;
                sb.Append(r.Text).Append('\n');
            }
            return sb.ToString();
        }
    }
}
