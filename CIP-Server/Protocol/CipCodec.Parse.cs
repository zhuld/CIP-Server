// ============================================================================
//  CIP-Server — Crestron CIP 协议模拟服务器（MIT License · 代码由 AI 辅助生成）
//  文件: Protocol/CipCodec.Parse.cs
//  说明: CIP 报文解析与 41795 packetized 帧的构建/解析。
//
//  41795 packetized 帧布局（8 字节头）:
//      [0] 0x0D  [1] 0x00          帧标识
//      [2..3] len (BE16)           = 4 + len2（type+flag+len2+负载）
//      [4] type                    0x01=数据 / 0x02=心跳 / 0x04=建立连接 / 0x06=断开
//      [5] flag                    心跳标志（恒为 0x00）
//      [6..7] len2 (BE16)          CIP 数据长度
//      [8..] CIP 消息流            即 41794 raw 框架的连续消息
//
//  与 raw ping 的区分: raw ping = 0D 00 02 00 00（[2]=0x02），
//  packetized 心跳 = 0D 00 00 04 02 00 00 00（[2..3]=0x00 0x04），二者无歧义。
// ============================================================================

using System;

namespace CipServerSim.Protocol
{
    /// <summary>CIP 解析与 packetized 41795 帧</summary>
    public static partial class CipCodec
    {
        /// <summary>
        /// 解析 Join 事件负载。
        /// 常规格式: [00 00 (dataLen+1) joinType data...]（第 3 字节 = dataLen+1，恒 ≥ 1；
        /// 最后 1 位计入 joinType 字节，故 dataLen = payload[2] - 1）
        /// Unicode 串口变体: 第 3 字节为 0x00（常规格式不可能出现），
        /// join 类型 0x34 与长度位于第 4/5 字节，两种字节序都做兼容（见下）。
        /// </summary>
        public static bool TryParseJoinEvent(byte[] payload, out byte joinType, out byte[] data)
        {
            joinType = 0;
            data = Empty;
            if (payload == null || payload.Length < 4) return false;

            // Unicode 串口(0x34) 变体：payload[2] == 0x00 是不可能出现在常规格式中的标志位
            // 变体 A: [00 00 00 34 len data...]（长度在 payload[4]）
            // 变体 B: [00 00 00 len 34 data...]（长度在 payload[3]）
            if (payload.Length > 5 && payload[2] == 0x00)
            {
                if (payload[3] == CipJoinTypes.SerialUnicode)
                {
                    joinType = CipJoinTypes.SerialUnicode;
                    data = TakeBytes(payload, 5, payload[4]);
                    return true;
                }
                if (payload[4] == CipJoinTypes.SerialUnicode)
                {
                    joinType = CipJoinTypes.SerialUnicode;
                    data = TakeBytes(payload, 5, payload[3]);
                    return true;
                }
            }

            joinType = payload[3];
            data = TakeBytes(payload, 4, payload[2] - 1);
            return true;
        }

        /// <summary>从 src[start] 起取最多 len 字节（自动收敛到剩余可用长度，负值按 0 处理）</summary>
        static byte[] TakeBytes(byte[] src, int start, int len)
        {
            if (start < 0) start = 0;
            if (len < 0) len = 0;
            int avail = src.Length - start;
            if (avail <= 0 || len == 0) return Empty;
            int take = len < avail ? len : avail;
            byte[] dst = new byte[take];
            Array.Copy(src, start, dst, 0, take);
            return dst;
        }

        /// <summary>心跳包（客户端/服务器互发，8 字节）</summary>
        public static byte[] PacketizedHeartbeat()
        {
            return new byte[] { 0x0D, 0x00, 0x00, 0x04, 0x02, 0x00, 0x00, 0x00 };
        }

        /// <summary>建立连接应答（echo 建立 payload 00 01 02 03）</summary>
        public static byte[] PacketizedEstablish()
        {
            return new byte[] { 0x0D, 0x00, 0x00, 0x08, 0x04, 0x00, 0x00, 0x04, 0x00, 0x01, 0x02, 0x03 };
        }

        /// <summary>数据包：[0D 00][len BE16=4+n][01 00][len2 BE16=n][CIP 消息]</summary>
        /// <remarks>长度字段为 16 位（本层不限长），但内嵌的 CIP 消息仍是 raw 3 字节头格式（负载 ≤ 255）。</remarks>
        public static byte[] PacketizedData(byte[] cipMessage)
        {
            int len2 = cipMessage.Length;
            int len = 4 + len2;
            byte[] p = new byte[8 + len2];
            p[0] = 0x0D;
            p[1] = 0x00;
            p[2] = (byte)((len >> 8) & 0xFF);
            p[3] = (byte)(len & 0xFF);
            p[4] = 0x01;
            p[5] = 0x00;
            p[6] = (byte)((len2 >> 8) & 0xFF);
            p[7] = (byte)(len2 & 0xFF);
            Array.Copy(cipMessage, 0, p, 8, len2);
            return p;
        }
    }
}
