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
        public string Model { get; set; }
        public string Firmware { get; set; }
        public string HostName { get; set; }
        public int Port41794 { get; set; }
        public int Port41795 { get; set; }
        public bool Enable41795 { get; set; }
        public int DigitalJoins { get; set; }
        public int AnalogJoins { get; set; }
        public int SerialJoins { get; set; }
        public List<IpEntry> IpTable { get; set; }

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

        public static string DefaultPath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json"); }
        }

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
                Console.WriteLine("config.json 加载失败，使用默认配置: " + ex.Message);
            }
            if (cfg.IpTable == null) cfg.IpTable = new List<IpEntry>();
            if (cfg.IpTable.Count == 0)
            {
                cfg.IpTable.Add(new IpEntry { Ipid = 3, Name = "TSW-1070 触摸屏" });
                cfg.IpTable.Add(new IpEntry { Ipid = 4, Name = "XPanel 2.0" });
            }
            return cfg;
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
