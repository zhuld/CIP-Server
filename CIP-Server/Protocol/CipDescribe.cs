using System;
using System.Collections.Generic;
using System.Text;

namespace CipServerSim.Protocol
{
    /// <summary>报文语义描述（用于日志）</summary>
    public static partial class CipCodec
    {
        public static string Describe(CipMessage m)
        {
            switch (m.Type)
            {
                case CipMessageTypes.Ack: return "ACK";
                case CipMessageTypes.WhoIs: return "Who-Is(请求客户端 Sign-On)";
                case CipMessageTypes.ServerSignOn:
                    return m.Payload.Length >= 6
                        ? string.Format("Sign-On 长格式(IPID 0x{0:X2})", m.Payload[5])
                        : "Sign-On 长格式(无 IPID)";
                case CipMessageTypes.ClientSignOn:
                    return m.Payload.Length >= 2
                        ? string.Format("Sign-On 短格式(IPID 0x{0:X2})", m.Payload[1])
                        : "Sign-On 短格式(无 IPID)";
                case CipMessageTypes.ConnAccepted:
                    if (m.Payload.Length == 4 && m.Payload[3] == 0x1F) return "注册成功";
                    if (m.Payload.Length == 3 && m.Payload[0] == 0xFF && m.Payload[1] == 0xFF && m.Payload[2] == 0x02)
                        return "注册失败(IPID 不存在)";
                    return "注册应答";
                case CipMessageTypes.ConnRefused: return "注册被拒";
                case CipMessageTypes.ClientDisconnect: return "中控断开通知";
                case CipMessageTypes.Ping: return "Ping(心跳)";
                case CipMessageTypes.Pong: return "Pong(心跳应答)";
                case CipMessageTypes.TimeSync: return "时间同步";
                case CipMessageTypes.Unicode: return "Unicode 串口消息";
                case CipMessageTypes.JoinEvent:
                    {
                        byte jt;
                        byte[] data;
                        if (!TryParseJoinEvent(m.Payload, out jt, out data)) return "Join 事件(无法解析)";
                        return DescribeJoinData(jt, data);
                    }
                default: return string.Format("未知消息(0x{0:X2})", m.Type);
            }
        }

        public static string DescribeJoinData(byte joinType, byte[] data)
        {
            switch (joinType)
            {
                case CipJoinTypes.Digital:
                    if (data.Length >= 2)
                    {
                        int join = (data[0] | ((data[1] & 0x7F) << 8)) + 1;
                        bool state = (data[1] & 0x80) == 0;
                        return string.Format("数字 join {0} = {1}", join, state ? "高" : "低");
                    }
                    return "数字 join(数据不足)";
                case CipJoinTypes.Analog:
                case CipJoinTypes.AnalogAlt:
                    {
                        var parts = new List<string>();
                        for (int i = 0; i + 3 < data.Length; i += 4)
                        {
                            int join = ((data[i] << 8) | data[i + 1]) + 1;
                            int value = (data[i + 2] << 8) | data[i + 3];
                            parts.Add(string.Format("模拟 join {0} = {1}", join, value));
                        }
                        return parts.Count > 0 ? string.Join("; ", parts.ToArray()) : "模拟 join(数据不足)";
                    }
                case CipJoinTypes.Serial:
                case CipJoinTypes.SerialAlt:
                case CipJoinTypes.SerialUtf8:
                case CipJoinTypes.SerialUnicode:
                    if (data.Length >= 3)
                    {
                        int join = ((data[0] << 8) | data[1]) + 1;
                        string text = Encoding.UTF8.GetString(data, 3, data.Length - 3);
                        return string.Format("串口 join {0} = \"{1}\"", join, text);
                    }
                    return "串口 join(数据不足)";
                case CipJoinTypes.Update:
                    if (data.Length >= 1)
                    {
                        if (data[0] == 0x00) return "Update 请求(要求回放全部 join 状态)";
                        if (data[0] == 0x16) return "Update 结束";
                        if (data[0] == 0x1C) return "Update 就绪(客户端可再次请求)";
                    }
                    return "Update 消息";
                case CipJoinTypes.TimeSync: return "时间同步";
                case CipJoinTypes.SmartObject:
                    {
                        string d = DescribeSmartObject(data);
                        return d ?? string.Format("SmartObject 事件 {0}", Logging.Log.ToHex(data));
                    }
                default:
                    return string.Format("未知 join 类型(0x{0:X2}) {1}", joinType, Logging.Log.ToHex(data));
            }
        }

        static string DescribeSmartObject(byte[] data)
        {
            if (data == null || data.Length < 6) return null;
            int objectId = data[3];
            int subtype = data[4];
            int type = data[5];
            if (subtype == 0x03 && type == CipJoinTypes.DigitalSmart && data.Length >= 8)
            {
                int join = (((data[7] & 0x7F) << 8) + data[6]) + 1;
                bool state = (data[7] & 0x80) == 0;
                return string.Format("SmartObject #{0} 数字 join {1} = {2}", objectId, join, state ? "高" : "低");
            }
            if (subtype == 0x05 && type == CipJoinTypes.AnalogAlt && data.Length >= 10)
            {
                int join = data[7] + 1;
                int value = (data[8] << 8) | data[9];
                return string.Format("SmartObject #{0} 模拟 join {1} = {2}", objectId, join, value);
            }
            if (type == CipJoinTypes.SerialUtf8 && data.Length >= 9)
            {
                int join = data[7] + 1;
                string text = Encoding.UTF8.GetString(data, 9, data.Length - 9);
                return string.Format("SmartObject #{0} 串口 join {1} = \"{2}\"", objectId, join, text);
            }
            return null;
        }
    }
}
