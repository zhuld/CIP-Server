// ============================================================================
//  CIP-Server — Crestron CIP 协议模拟服务器（MIT License · 代码由 AI 辅助生成）
//  文件: Logging/Log.cs
//  说明: 全局静态日志器。每条日志同时:
//        1) 追加写入 cip.log（UTF-8，打开失败时静默降级）；
//        2) 通过 Line 事件推送给 UI（主窗体再 BeginInvoke 到 UI 线程渲染）。
//  线程安全: 写文件由 gate 锁串行化；事件在调用线程上同步触发。
// ============================================================================

using System;
using System.IO;
using System.Text;

namespace CipServerSim.Logging
{
    /// <summary>日志：控制台 UI 事件 + cip.log 文件（报文十六进制 + 语义）</summary>
    public static class Log
    {
        static readonly object gate = new object();
        static StreamWriter writer;

        /// <summary>是否在日志中附加报文十六进制</summary>
        public static bool ShowHex = true;

        /// <summary>UI 订阅：每条日志行触发一次</summary>
        public static event Action<string> Line;

        /// <summary>打开日志文件（追加模式，UTF-8）。失败时静默降级为仅界面输出</summary>
        public static void InitFile(string path)
        {
            try
            {
                writer = new StreamWriter(path, true, Encoding.UTF8) { AutoFlush = true };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("日志文件打开失败: " + ex.Message);
            }
        }

        /// <summary>普通文本日志</summary>
        public static void Info(string text)
        {
            Emit(text, null);
        }

        /// <summary>带方向的报文日志；semantic 为语义描述，data 为完整报文</summary>
        public static void Hex(bool tx, string semantic, byte[] data)
        {
            if (!ShowHex)
            {
                Emit(semantic, null);
                return;
            }
            Emit(semantic, (tx ? "TX " : "RX ") + ToHex(data));
        }

        public static string ToHex(byte[] data)
        {
            if (data == null || data.Length == 0) return "(空)";
            var sb = new StringBuilder(data.Length * 3);
            for (int i = 0; i < data.Length; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(data[i].ToString("X2"));
            }
            return sb.ToString();
        }

        /// <summary>把一行日志写入文件并推送给 UI 订阅者</summary>
        static void Emit(string text, string hex)
        {
            var line = string.Format("[{0:HH:mm:ss.fff}] {1}", DateTime.Now, text);
            if (!string.IsNullOrEmpty(hex)) line += "   | " + hex;
            lock (gate)
            {
                try
                {
                    if (writer != null) writer.WriteLine(line);
                }
                catch
                {
                    // 文件日志失败不影响运行
                }
            }
            var h = Line;
            if (h == null) return;
            try
            {
                h(line);
            }
            catch (Exception ex)
            {
                // 订阅者（UI）异常不得回传到协议收发线程
                System.Diagnostics.Debug.WriteLine("日志订阅者异常: " + ex.Message);
            }
        }
    }
}
