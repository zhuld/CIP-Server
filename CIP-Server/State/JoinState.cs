using System;
using System.Collections.Generic;
using CipServerSim.Logging;

namespace CipServerSim.State
{
    public enum JoinSource { Local, Remote }

    /// <summary>
    /// 全部 join 的当前状态（数字/模拟/串口），线程安全。
    /// 状态变化通过事件通知 UI；服务器订阅事件并向客户端广播。
    /// </summary>
    public sealed class JoinState
    {
        readonly bool[] digital;
        readonly int[] analog;
        readonly string[] serial;
        readonly DateTime[] digitalTime, analogTime, serialTime;
        readonly string[] digitalInfo, analogInfo, serialInfo;
        readonly HashSet<int> digitalTouched = new HashSet<int>();
        readonly HashSet<int> analogTouched = new HashSet<int>();
        readonly HashSet<int> serialTouched = new HashSet<int>();
        readonly object sync = new object();

        public int DigitalCount { get; private set; }
        public int AnalogCount { get; private set; }
        public int SerialCount { get; private set; }

        public event Action<int, bool, JoinSource, string> DigitalChanged;
        public event Action<int, int, JoinSource, string> AnalogChanged;
        public event Action<int, string, JoinSource, string> SerialChanged;

        public JoinState(DeviceConfig cfg)
        {
            DigitalCount = cfg.DigitalJoins > 0 ? cfg.DigitalJoins : 256;
            AnalogCount = cfg.AnalogJoins > 0 ? cfg.AnalogJoins : 128;
            SerialCount = cfg.SerialJoins > 0 ? cfg.SerialJoins : 64;
            digital = new bool[DigitalCount];
            analog = new int[AnalogCount];
            serial = new string[SerialCount];
            digitalTime = new DateTime[DigitalCount];
            analogTime = new DateTime[AnalogCount];
            serialTime = new DateTime[SerialCount];
            digitalInfo = new string[DigitalCount];
            analogInfo = new string[AnalogCount];
            serialInfo = new string[SerialCount];
        }

        // ---------------- 数字 ----------------

        public bool SetDigital(int join, bool value, JoinSource src, string info)
        {
            if (join < 1 || join > DigitalCount)
            {
                Log.Info(string.Format("数字 join {0} 超出范围 (1-{1})", join, DigitalCount));
                return false;
            }
            lock (sync)
            {
                digital[join - 1] = value;
                digitalTime[join - 1] = DateTime.Now;
                digitalInfo[join - 1] = info ?? "";
                if (!digitalTouched.Contains(join)) digitalTouched.Add(join);
            }
            var h = DigitalChanged;
            if (h != null) h(join, value, src, info);
            return true;
        }

        public bool GetDigital(int join)
        {
            lock (sync) { return join >= 1 && join <= DigitalCount && digital[join - 1]; }
        }

        public DateTime GetDigitalTime(int join)
        {
            lock (sync) { return join >= 1 && join <= DigitalCount ? digitalTime[join - 1] : DateTime.MinValue; }
        }

        public string GetDigitalInfo(int join)
        {
            lock (sync) { return join >= 1 && join <= DigitalCount ? digitalInfo[join - 1] : ""; }
        }

        // ---------------- 模拟 ----------------

        public bool SetAnalog(int join, int value, JoinSource src, string info)
        {
            if (join < 1 || join > AnalogCount)
            {
                Log.Info(string.Format("模拟 join {0} 超出范围 (1-{1})", join, AnalogCount));
                return false;
            }
            if (value < 0) value = 0;
            if (value > 65535) value = 65535;
            lock (sync)
            {
                analog[join - 1] = value;
                analogTime[join - 1] = DateTime.Now;
                analogInfo[join - 1] = info ?? "";
                if (!analogTouched.Contains(join)) analogTouched.Add(join);
            }
            var h = AnalogChanged;
            if (h != null) h(join, value, src, info);
            return true;
        }

        public int GetAnalog(int join)
        {
            lock (sync) { return join >= 1 && join <= AnalogCount ? analog[join - 1] : 0; }
        }

        public DateTime GetAnalogTime(int join)
        {
            lock (sync) { return join >= 1 && join <= AnalogCount ? analogTime[join - 1] : DateTime.MinValue; }
        }

        public string GetAnalogInfo(int join)
        {
            lock (sync) { return join >= 1 && join <= AnalogCount ? analogInfo[join - 1] : ""; }
        }

        // ---------------- 串口 ----------------

        public bool SetSerial(int join, string text, JoinSource src, string info)
        {
            if (join < 1 || join > SerialCount)
            {
                Log.Info(string.Format("串口 join {0} 超出范围 (1-{1})", join, SerialCount));
                return false;
            }
            lock (sync)
            {
                serial[join - 1] = text ?? "";
                serialTime[join - 1] = DateTime.Now;
                serialInfo[join - 1] = info ?? "";
                if (!serialTouched.Contains(join)) serialTouched.Add(join);
            }
            var h = SerialChanged;
            if (h != null) h(join, text ?? "", src, info);
            return true;
        }

        public string GetSerial(int join)
        {
            lock (sync) { return join >= 1 && join <= SerialCount ? (serial[join - 1] ?? "") : ""; }
        }

        public DateTime GetSerialTime(int join)
        {
            lock (sync) { return join >= 1 && join <= SerialCount ? serialTime[join - 1] : DateTime.MinValue; }
        }

        public string GetSerialInfo(int join)
        {
            lock (sync) { return join >= 1 && join <= SerialCount ? serialInfo[join - 1] : ""; }
        }

        // ---------------- 更新回放快照 ----------------

        public int[] GetTouchedDigital() { lock (sync) { return ToArray(digitalTouched); } }
        public int[] GetTouchedAnalog() { lock (sync) { return ToArray(analogTouched); } }
        public int[] GetTouchedSerial() { lock (sync) { return ToArray(serialTouched); } }

        static int[] ToArray(HashSet<int> set)
        {
            var list = new List<int>(set);
            list.Sort();
            return list.ToArray();
        }
    }
}
