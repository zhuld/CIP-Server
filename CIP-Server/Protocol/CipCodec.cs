// ============================================================================
//  CIP-Server — Crestron CIP 协议模拟服务器（MIT License · 代码由 AI 辅助生成）
//  文件: Protocol/CipCodec.cs
//  说明: CIP 消息构建（编码方向）。所有报文字节布局均基于对真实设备流量的
//        逆向工程结果，与 README“协议要点”表一一对应。
//  命名: JoinEvent* 生成消息类型 0x05（JOIN_EVENT）；Packetized* 生成
//        41795 端口的 packetized 帧。
// ============================================================================

using System;
using System.Text;

namespace CipServerSim.Protocol
{
    /// <summary>CIP 编解码（基于对真实 Crestron 设备流量的逆向工程结果）</summary>
    public static partial class CipCodec
    {
        /// <summary>41794 raw 帧的长度字段只有 1 字节，负载上限 255</summary>
        public const int MaxPayload = 255;

        /// <summary>串口文本单帧上限：3(外层头) + 4(内层头) + 3(join 头) + 文本 ≤ 255 → 文本最多 248 字节</summary>
        const int MaxSerialTextBytes = 248;

        /// <summary>共享的空负载常量（只读使用，切勿改写）</summary>
        static readonly byte[] Empty = new byte[0];

        /// <summary>
        /// 3 字节头包装：[type][0x00][payloadLen]。
        /// 这是 41794 端口的标准帧；payloadLen 为单字节（负载最大 255）。
        /// 负载超长时在此截断：正常构造路径已按各自上限预截断（如 SerialJoinEvent），
        /// 此处兜底避免长度字段回绕成与实际长度不符的错误值。
        /// </summary>
        public static byte[] Wrapper(byte type, byte[] payload)
        {
            payload = payload ?? Empty;
            if (payload.Length > MaxPayload)
            {
                byte[] limited = new byte[MaxPayload];
                Array.Copy(payload, 0, limited, 0, MaxPayload);
                payload = limited;
            }
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
            return Wrapper(CipMessageTypes.ClientDisconnect, Empty);
        }

        /// <summary>Join 事件内层：[00 00 (dataLen+1) joinType data...]（dataLen 不含前 4 字节）</summary>
        public static byte[] JoinEvent(byte joinType, byte[] data)
        {
            data = data ?? Empty;
            byte[] inner = new byte[data.Length + 4];
            inner[2] = (byte)(data.Length + 1);
            inner[3] = joinType;
            Array.Copy(data, 0, inner, 4, data.Length);
            return Wrapper(CipMessageTypes.JoinEvent, inner);
        }

        /// <summary>数字 join（data = [低8位, 高7位|状态位]，状态位=0x80 表示低电平）</summary>
        /// <remarks>
        /// 编码示例：join 5 置高 → data=[0x04, 0x00]，完整报文 05 00 06 00 00 03 00 04 00。
        /// 解析方向：join = (data[0] | ((data[1] &amp; 0x7F) &lt;&lt; 8)) + 1，高电平 = (data[1] &amp; 0x80) == 0。
        /// 注意：join 内部以 join-1 表示（1 基 join 号 → 0 基线缆编号），有效范围 1-32768。
        /// </remarks>
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
            if (value < 0) value = 0;                  // 防御性收敛：JoinState 已限制 0-65535，
            if (value > 0xFFFF) value = 0xFFFF;        // 此处仅防止外部直接调用时写出越界值
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
        /// <remarks>
        /// 文本按 UTF-8 编码，单帧上限 <see cref="MaxSerialTextBytes"/> 字节；
        /// 超出部分按字符边界截断（否则外层长度字段会溢出回绕，产生非法帧）。
        /// </remarks>
        public static byte[] SerialJoinEvent(int join, string text)
        {
            int j = join - 1;
            byte[] tb = TruncateUtf8(Encoding.UTF8.GetBytes(text ?? string.Empty), MaxSerialTextBytes);
            byte[] data = new byte[3 + tb.Length];
            data[0] = (byte)((j >> 8) & 0xFF);
            data[1] = (byte)(j & 0xFF);
            data[2] = 0x03;
            Array.Copy(tb, 0, data, 3, tb.Length);
            return JoinEvent(CipJoinTypes.SerialUtf8, data);
        }

        /// <summary>按 UTF-8 字符边界截断，避免接收端出现“半个多字节字符”</summary>
        static byte[] TruncateUtf8(byte[] bytes, int max)
        {
            if (bytes.Length <= max) return bytes;
            int len = max;
            // 0x80-0xBF 为续字节，说明该位置正处于多字节字符中间，需继续回退
            while (len > 0 && (bytes[len] & 0xC0) == 0x80) len--;
            byte[] cut = new byte[len];
            Array.Copy(bytes, cut, len);
            return cut;
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
