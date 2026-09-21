using System;
using System.Text;

namespace CipServerSim.Protocol
{
    /// <summary>CIP 编解码（基于对真实 Crestron 设备流量的逆向工程结果）</summary>
    public static partial class CipCodec
    {
        /// <summary>3 字节头包装：[type][0x00][payloadLen]</summary>
        public static byte[] Wrapper(byte type, byte[] payload)
        {
            payload = payload ?? new byte[0];
            byte[] msg = new byte[payload.Length + 3];
            msg[0] = type;
            msg[1] = 0x00;
            msg[2] = (byte)payload.Length;
            Array.Copy(payload, 0, msg, 3, payload.Length);
            return msg;
        }

        /// <summary>Who-Is：服务器接受 TCP 连接后发送，要求客户端上报 IPID</summary>
        public static byte[] WhoIs()
        {
            return new byte[] { 0x0F, 0x00, 0x01, 0x02 };
        }

        /// <summary>注册成功应答（与真实中控一致：payload 00 00 00 1F）</summary>
        public static byte[] AcceptRegistration()
        {
            return Wrapper(CipMessageTypes.ConnAccepted, new byte[] { 0x00, 0x00, 0x00, 0x1F });
        }

        /// <summary>注册失败应答（payload FF FF 02 = IPID 不存在）</summary>
        public static byte[] RefuseRegistration()
        {
            return Wrapper(CipMessageTypes.ConnAccepted, new byte[] { 0xFF, 0xFF, 0x02 });
        }

        public static byte[] Ping()
        {
            return new byte[] { 0x0D, 0x00, 0x02, 0x00, 0x00 };
        }

        public static byte[] Pong()
        {
            return new byte[] { 0x0E, 0x00, 0x02, 0x00, 0x00 };
        }

        /// <summary>中控断开通知</summary>
        public static byte[] DisconnectNotice()
        {
            return Wrapper(CipMessageTypes.ClientDisconnect, new byte[0]);
        }

        /// <summary>Join 事件内层：[00 00 (dataLen+1) joinType data...]</summary>
        public static byte[] JoinEvent(byte joinType, byte[] data)
        {
            data = data ?? new byte[0];
            byte[] inner = new byte[data.Length + 4];
            inner[2] = (byte)(data.Length + 1);
            inner[3] = joinType;
            Array.Copy(data, 0, inner, 4, data.Length);
            return Wrapper(CipMessageTypes.JoinEvent, inner);
        }

        /// <summary>数字 join（data = [低8位, 高7位|状态位]，状态位=0x80 表示低电平）</summary>
        public static byte[] DigitalJoinEvent(int join, bool state)
        {
            int j = join - 1;
            byte[] data = new byte[]
            {
                (byte)(j & 0xFF),
                (byte)(((j >> 8) & 0x7F) | (state ? 0x00 : 0x80))
            };
            return JoinEvent(CipJoinTypes.Digital, data);
        }

        /// <summary>模拟 join（data = [join-1 高,低][值 高,低]，均大端）</summary>
        public static byte[] AnalogJoinEvent(int join, int value)
        {
            int j = join - 1;
            byte[] data = new byte[]
            {
                (byte)((j >> 8) & 0xFF),
                (byte)(j & 0xFF),
                (byte)((value >> 8) & 0xFF),
                (byte)(value & 0xFF)
            };
            return JoinEvent(CipJoinTypes.AnalogAlt, data);
        }

        /// <summary>串口 join（data = [join-1 高,低][0x03][文本]）</summary>
        public static byte[] SerialJoinEvent(int join, string text)
        {
            int j = join - 1;
            byte[] tb = Encoding.UTF8.GetBytes(text ?? string.Empty);
            if (tb.Length > 250)
            {
                byte[] cut = new byte[250];
                Array.Copy(tb, cut, 250);
                tb = cut;
            }
            byte[] data = new byte[3 + tb.Length];
            data[0] = (byte)((j >> 8) & 0xFF);
            data[1] = (byte)(j & 0xFF);
            data[2] = 0x03;
            Array.Copy(tb, 0, data, 3, tb.Length);
            return JoinEvent(CipJoinTypes.SerialUtf8, data);
        }

        public static byte[] UpdateRequest()
        {
            return JoinEvent(CipJoinTypes.Update, new byte[] { 0x00 });
        }

        public static byte[] UpdateEnded()
        {
            return JoinEvent(CipJoinTypes.Update, new byte[] { 0x16 });
        }

        public static byte[] UpdateReady()
        {
            return JoinEvent(CipJoinTypes.Update, new byte[] { 0x1C });
        }
    }
}
