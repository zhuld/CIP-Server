using System;

namespace CipServerSim.Protocol
{
    /// <summary>CIP 消息（对应 41794 raw 框架：[type][0x00][len][payload]）</summary>
    public sealed class CipMessage
    {
        public byte Type;
        public byte[] Payload;

        public CipMessage(byte type, byte[] payload)
        {
            Type = type;
            Payload = payload ?? new byte[0];
        }
    }

    /// <summary>CIP 消息类型码（41794）</summary>
    public static class CipMessageTypes
    {
        public const byte Ack = 0x00;
        public const byte ServerSignOn = 0x01;
        public const byte ConnAccepted = 0x02;
        public const byte ClientDisconnect = 0x03;
        public const byte ConnRefused = 0x04;
        public const byte JoinEvent = 0x05;
        public const byte TimeSync = 0x08;
        public const byte ClientSignOn = 0x0A;
        public const byte Ping = 0x0D;
        public const byte Pong = 0x0E;
        public const byte WhoIs = 0x0F;
        public const byte Unicode = 0x12;
    }

    /// <summary>Join 事件内部的 join 类型码</summary>
    public static class CipJoinTypes
    {
        public const byte Digital = 0x00;
        public const byte Analog = 0x01;
        public const byte Serial = 0x02;
        public const byte Update = 0x03;
        public const byte TimeSync = 0x08;
        public const byte AnalogAlt = 0x14;
        public const byte SerialAlt = 0x12;
        public const byte SerialUtf8 = 0x15;
        public const byte SerialUnicode = 0x34;
        public const byte DigitalSmart = 0x27;
        public const byte SmartObject = 0x38;
    }
}
