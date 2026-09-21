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
            if (h != null) h(line);
        }
    }
}
