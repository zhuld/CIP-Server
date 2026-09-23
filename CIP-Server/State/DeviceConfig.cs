// ============================================================================
//  CIP-Server — Crestron CIP 协议模拟服务器（MIT License · 代码由 AI 辅助生成）
//  文件: State/DeviceConfig.cs
//  说明: 模拟器的全部配置（型号/固件/主机名/端口/IP 表/join 数量）。
//        使用 .NET 内置 JavaScriptSerializer (System.Web.Extensions) 读写
//        config.json，键名与属性名保持一致（PascalCase）。
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace CipServerSim.State
{
    /// <summary>IP 表条目：客户端 Sign-On 时的 IPID → 设备名映射</summary>
    public sealed class IpEntry
    {
        /// <summary>IPID（十进制存储，1-253 有效；0xFF 等为 Crestron 保留值）</summary>
        public int Ipid { get; set; }

        /// <summary>设备名称（仅用于显示与日志）</summary>
        public string Name { get; set; }
    }

    /// <summary>模拟器配置：型号 / 固件 / 端口 / IP 表 / join 数量</summary>
    public sealed class DeviceConfig
    {
        /// <summary>合法端口下限</summary>
        public const int MinPort = 1024;

        /// <summary>合法端口上限</summary>
        public const int MaxPort = 65535;

        public string Model { get; set; }
        public string Firmware { get; set; }
        public string HostName { get; set; }
        public int Port41794 { get; set; }
        public int Port41795 { get; set; }
        public bool Enable41795 { get; set; }
        public bool CompactMode { get; set; }
        public int DigitalJoins { get; set; }
        public int AnalogJoins { get; set; }
        public int SerialJoins { get; set; }
        public List<IpEntry> IpTable { get; set; }

        /// <summary>
        /// config.json 加载失败时的说明，由主窗体在日志系统就绪后补记（正常为 null）。
        /// internal + 只读：不会被 JavaScriptSerializer 写进 config.json。
        /// </summary>
        internal string LoadWarning { get; private set; }

        public DeviceConfig()
        {
            Model = "CP4";
            Firmware = "v4.007.0014";
            HostName = "CIP-SIM";
            Port41794 = 41794;
            Port41795 = 41795;
            Enable41795 = true;
            DigitalJoins = 256;
            AnalogJoins = 128;
            SerialJoins = 64;
            IpTable = new List<IpEntry>();
        }

        /// <summary>可执行文件同目录下的 config.json</summary>
        public static string DefaultPath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json"); }
        }

        /// <summary>读取 config.json；文件不存在或损坏时退回默认配置（失败原因记入 LoadWarning）</summary>
        public static DeviceConfig LoadDefault()
        {
            var cfg = new DeviceConfig();
            try
            {
                if (File.Exists(DefaultPath))
                {
                    var ser = new JavaScriptSerializer();
                    var loaded = ser.Deserialize<DeviceConfig>(File.ReadAllText(DefaultPath));
                    if (loaded != null) cfg = loaded;
                }
            }
            catch (Exception ex)
            {
                // 此处日志文件尚未初始化，先记在 LoadWarning 上由主窗体补记（WinExe 无控制台）
                cfg.LoadWarning = "config.json 加载失败，已改用默认配置: " + ex.Message;
            }
            cfg.Normalize();
            if (cfg.IpTable.Count == 0)
            {
                // 空 IP 表会导致所有客户端注册被拒，给几个常用 IPID 便于开箱即用
                cfg.IpTable.Add(new IpEntry { Ipid = 3, Name = "TSW-1070 触摸屏" });
                cfg.IpTable.Add(new IpEntry { Ipid = 4, Name = "XPanel 2.0" });
            }
            return cfg;
        }

        /// <summary>
        /// 把缺失/越界的配置项收敛为可用值：config.json 缺字段或被手工改坏时兜底，
        /// 避免出现端口 0（随机端口）、两端口相同（第二个无法绑定）、
        /// join 数量 0（界面无行可显示，而 JoinState 内部另有默认值）等情况。
        /// </summary>
        void Normalize()
        {
            if (string.IsNullOrEmpty(Model)) Model = "CP4";
            if (string.IsNullOrEmpty(Firmware)) Firmware = "v4.007.0014";
            if (string.IsNullOrEmpty(HostName)) HostName = "CIP-SIM";
            if (Port41794 < MinPort || Port41794 > MaxPort) Port41794 = 41794;
            if (Port41795 < MinPort || Port41795 > MaxPort) Port41795 = 41795;
            if (Enable41795 && Port41795 == Port41794)
                Port41795 = Port41794 < MaxPort ? Port41794 + 1 : Port41794 - 1;
            if (DigitalJoins <= 0) DigitalJoins = 256;
            if (AnalogJoins <= 0) AnalogJoins = 128;
            if (SerialJoins <= 0) SerialJoins = 64;
            if (IpTable == null) IpTable = new List<IpEntry>();
        }

        public void Save(string path)
        {
            var ser = new JavaScriptSerializer();
            File.WriteAllText(path, ser.Serialize(this));
        }

        public IpEntry FindIpEntry(int ipid)
        {
            if (IpTable == null) return null;
            foreach (var e in IpTable) if (e.Ipid == ipid) return e;
            return null;
        }

        public string IpTableSummary()
        {
            var parts = new List<string>();
            if (IpTable != null)
                foreach (var e in IpTable)
                    parts.Add(string.Format("0x{0:X2}({1})", e.Ipid, e.Name ?? ""));
            return string.Join(", ", parts.ToArray());
        }
    }
}
