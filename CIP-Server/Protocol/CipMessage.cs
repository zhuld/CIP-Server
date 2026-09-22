using System;

namespace CipServerSim.Protocol
{
    /// <summary>
    /// 一条已解包的 CIP 消息（对应 41794 raw 框架：[type][0x00][len][payload]）。
    /// 41795 packetized 模式下同样使用该结构，只是收发时外面再包一层帧头。
    /// </summary>
    public sealed class CipMessage
    {
        /// <summary>消息类型码，见 <see cref="CipMessageTypes"/></summary>
        public byte Type;

        /// <summary>消息负载（不含 3 字节头）</summary>
        public byte[] Payload;

        public CipMessage(byte type, byte[] payload)
        {
            Type = type;
            Payload = payload ?? new byte[0];
        }
    }

    /// <summary>
    /// CIP 消息类型码（41794 端口协议，逆向工程结果）。
    /// 完整消息 = [type][0x00][len(1B)][payload]，len 不含 3 字节头。
    /// </summary>
    public static class CipMessageTypes
    {
        public const byte Ack = 0x00;             // 通用确认
        public const byte ServerSignOn = 0x01;    // Sign-On 长格式，IPID 位于 payload[5]
        public const byte ConnAccepted = 0x02;    // 注册应答：00 00 00 1F=成功 / FF FF 02=IPID 不存在
        public const byte ClientDisconnect = 0x03;// 中控主动断开通知
        public const byte ConnRefused = 0x04;     // 注册被拒（部分客户端实现使用）
        public const byte JoinEvent = 0x05;       // Join 事件/Update，负载内含 join 类型与数据
        public const byte TimeSync = 0x08;        // 日期/时间同步
        public const byte ClientSignOn = 0x0A;    // Sign-On 短格式，IPID 位于 payload[1]
        public const byte Ping = 0x0D;            // 心跳请求（0D 00 02 00 00）
        public const byte Pong = 0x0E;            // 心跳应答（0E 00 02 00 00）
        public const byte WhoIs = 0x0F;           // Who-Is：服务器要求客户端上报 IPID（0F 00 01 02）
        public const byte Unicode = 0x12;         // Unicode 串口消息
    }

    /// <summary>
    /// Join 事件（消息类型 0x05）负载内部的 join 类型码。
    /// 内层格式：[00 00 (dataLen+1) joinType data...]，dataLen 不含前 4 字节。
    /// </summary>
    public static class CipJoinTypes
    {
        public const byte Digital = 0x00;       // 数字 join：data=[低8位][高7位|0x80(低电平)]
        public const byte Analog = 0x01;        // 模拟 join（旧格式）
        public const byte Serial = 0x02;        // 串口 join（旧格式）
        public const byte Update = 0x03;        // Update：0x00=请求回放 / 0x16=结束 / 0x1C=就绪
        public const byte TimeSync = 0x08;      // 时间同步
        public const byte AnalogAlt = 0x14;     // 模拟 join（现代格式）：data=[join-1 BE16][值 BE16]
        public const byte SerialAlt = 0x12;     // 串口 join（变体 0x12）
        public const byte SerialUtf8 = 0x15;    // 串口 join（现代格式）：data=[join-1 BE16][0x03][文本]
        public const byte SerialUnicode = 0x34; // 串口 join（Unicode 变体，内层结构不同）
        public const byte DigitalSmart = 0x27;  // SmartObject 数字（作为 subtype 内的类型码）
        public const byte SmartObject = 0x38;   // SmartObject 事件（本模拟器仅记录日志）
    }
}
