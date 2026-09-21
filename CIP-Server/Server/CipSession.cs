using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using CipServerSim.Logging;
using CipServerSim.Protocol;
using CipServerSim.State;

namespace CipServerSim.Server
{
    public enum SessionState { Connecting, Registered, Refused, Closed }
    public enum Framing { Raw, Packetized }

    /// <summary>
    /// 一个客户端连接的会话状态机：
    /// 连接 → Who-Is → Sign-On(校验 IPID) → 注册应答 → Update 回放 → Ping/Pong 保活
    /// 支持 41794 raw 与 41795 packetized 两种报文框架。
    /// </summary>
    public sealed class CipSession
    {
        public const int PingIntervalMs = 10000;
        public const int MaxMissedPings = 5;

        readonly CipServer owner;
        readonly TcpClient tcp;
        readonly NetworkStream stream;
        readonly Thread rxThread;
        readonly Timer pingTimer;
        readonly object sendLock = new object();
        readonly List<byte> acc = new List<byte>();

        int missedPings;
        volatile bool closed;

        public string RemoteAddr { get; private set; }
        public Framing FramingMode { get; private set; }
        public SessionState State { get; private set; }
        public int Ipid { get; private set; }
        public bool HasIpid { get; private set; }
        public DateTime LastActivity { get; private set; }

        public event Action<CipSession> Disposed;

        public CipSession(CipServer owner, TcpClient tcp, Framing framing)
        {
            this.owner = owner;
            this.tcp = tcp;
            FramingMode = framing;
            State = SessionState.Connecting;
            LastActivity = DateTime.Now;
            RemoteAddr = tcp.Client.RemoteEndPoint != null ? tcp.Client.RemoteEndPoint.ToString() : "未知";
            stream = tcp.GetStream();
            rxThread = new Thread(RxLoop) { IsBackground = true, Name = "CIP-RX " + RemoteAddr };
            rxThread.Start();
            pingTimer = new Timer(PingTick, null, PingIntervalMs, PingIntervalMs);
            if (framing == Framing.Raw)
            {
                // 41794：服务器接受连接后立即发送 Who-Is
                SendRaw(CipCodec.WhoIs(), "Who-Is(请求客户端 Sign-On)");
            }
        }

        public string PeerText()
        {
            return HasIpid
                ? string.Format("IPID 0x{0:X2} · {1}", Ipid, RemoteAddr)
                : RemoteAddr;
        }

        // ===================== 发送 =====================

        public void SendRaw(byte[] msg)
        {
            SendRaw(msg, null);
        }

        /// <summary>发送一条 CIP 消息（packetized 模式自动加数据帧头）；semantic 为空则自动描述</summary>
        public void SendRaw(byte[] msg, string semantic)
        {
            if (closed || msg == null) return;
            byte[] wire = FramingMode == Framing.Packetized ? CipCodec.PacketizedData(msg) : msg;
            try
            {
                lock (sendLock) stream.Write(wire, 0, wire.Length);
            }
            catch
            {
                return;
            }
            string desc = semantic ?? CipCodec.Describe(new CipMessage(msg[0], Slice(msg, 3)));
            Log.Hex(true, string.Format("TX→ {0} · {1}", PeerText(), desc), wire);
        }

        /// <summary>直接发送 packetized 控制帧（心跳/建立连接应答）</summary>
        void SendPacketized(byte[] wire, string desc)
        {
            if (closed) return;
            try
            {
                lock (sendLock) stream.Write(wire, 0, wire.Length);
            }
            catch
            {
                return;
            }
            Log.Hex(true, string.Format("TX→ {0} · {1}", PeerText(), desc), wire);
        }

        static byte[] Slice(byte[] src, int start)
        {
            int len = src.Length - start;
            if (len <= 0) return new byte[0];
            byte[] dst = new byte[len];
            Array.Copy(src, start, dst, 0, len);
            return dst;
        }

        // ===================== 接收 =====================

        void RxLoop()
        {
            var tmp = new byte[8192];
            try
            {
                while (!closed)
                {
                    int n = stream.Read(tmp, 0, tmp.Length);
                    if (n <= 0) break;
                    LastActivity = DateTime.Now;
                    missedPings = 0;
                    for (int i = 0; i < n; i++) acc.Add(tmp[i]);

                    // raw 会话中出现 packetized 帧头时自动切换（0D 00 00 xx）
                    if (FramingMode == Framing.Raw && acc.Count >= 3 &&
                        acc[0] == 0x0D && acc[1] == 0x00 && acc[2] == 0x00)
                    {
                        FramingMode = Framing.Packetized;
                        Log.Info(string.Format("客户端 {0} 切换为 packetized 报文框架", RemoteAddr));
                    }

                    if (FramingMode == Framing.Packetized) ParsePacketized();
                    else ParseRaw();
                }
            }
            catch
            {
                // socket 异常按断开处理
            }
            finally
            {
                Close("连接关闭");
            }
        }

        /// <summary>解析 41794 raw 消息流：[type][0x00][len][payload]</summary>
        void ParseRaw()
        {
            while (acc.Count >= 3)
            {
                if (acc[1] != 0x00) { acc.RemoveAt(0); continue; } // 重新同步
                int len = acc[2];
                int total = len + 3;
                if (acc.Count < total) break;
                byte[] msg = new byte[total];
                acc.CopyTo(0, msg, 0, total);
                acc.RemoveRange(0, total);
                byte[] payload = new byte[len];
                Array.Copy(msg, 3, payload, 0, len);
                HandleMessage(new CipMessage(msg[0], payload));
            }
        }

        /// <summary>解析 41795 packetized 帧流：[0D 00][len BE16][type][flag][len2 BE16][payload]</summary>
        void ParsePacketized()
        {
            while (acc.Count >= 4)
            {
                if (acc[0] != 0x0D || acc[1] != 0x00) { acc.RemoveAt(0); continue; }
                int len = (acc[2] << 8) | acc[3];
                int total = 4 + len;
                if (total < 8) total = 8;
                if (acc.Count < total) break;
                byte[] pkt = new byte[total];
                acc.CopyTo(0, pkt, 0, total);
                acc.RemoveRange(0, total);
                byte type = pkt[4];
                switch (type)
                {
                    case 0x04: // 建立连接
                        SendPacketized(CipCodec.PacketizedEstablish(), "建立连接应答(establish)");
                        SendPacketized(CipCodec.PacketizedData(CipCodec.WhoIs()), "Who-Is(请求客户端 Sign-On)");
                        break;
                    case 0x02: // 心跳
                        SendPacketized(CipCodec.PacketizedHeartbeat(), "心跳应答(heartbeat)");
                        break;
                    case 0x01: // 数据
                        if (pkt.Length >= 8)
                        {
                            int len2 = (pkt[6] << 8) | pkt[7];
                            if (len2 > 0 && pkt.Length >= 8 + len2)
                            {
                                byte[] cip = new byte[len2];
                                Array.Copy(pkt, 8, cip, 0, len2);
                                HandleRawStream(cip);
                            }
                        }
                        break;
                    case 0x06: // 断开
                        Close("客户端请求断开(packetized disconnect)");
                        return;
                    default:
                        Log.Info(string.Format("{0} 收到未知 packetized 包类型 0x{1:X2}", RemoteAddr, type));
                        break;
                }
            }
        }

        /// <summary>数据帧负载中的 CIP 消息流（可能包含多条）</summary>
        void HandleRawStream(byte[] data)
        {
            int pos = 0;
            while (pos + 3 <= data.Length)
            {
                if (data[pos + 1] != 0x00) { pos++; continue; }
                int len = data[pos + 2];
                if (pos + 3 + len > data.Length) break;
                byte[] payload = new byte[len];
                Array.Copy(data, pos + 3, payload, 0, len);
                HandleMessage(new CipMessage(data[pos], payload));
                pos += 3 + len;
            }
        }

        // ===================== 消息处理 =====================

        void HandleMessage(CipMessage m)
        {
            Log.Hex(false, string.Format("RX← {0} · {1}", PeerText(), CipCodec.Describe(m)), CipCodec.Wrapper(m.Type, m.Payload));
            switch (m.Type)
            {
                case CipMessageTypes.ServerSignOn:
                    if (m.Payload.Length >= 6) HandleSignOn(m.Payload[5], "长格式 0x01");
                    break;
                case CipMessageTypes.ClientSignOn:
                    if (m.Payload.Length >= 2) HandleSignOn(m.Payload[1], "短格式 0x0A");
                    break;
                case CipMessageTypes.JoinEvent:
                    HandleJoinEvent(m.Payload);
                    break;
                case CipMessageTypes.Ping:
                    SendRaw(CipCodec.Pong(), "Pong(心跳应答)");
                    break;
                case CipMessageTypes.Pong:
                    if (missedPings > 0) missedPings--;
                    break;
                case CipMessageTypes.Unicode:
                    HandleUnicode(m.Payload);
                    break;
                case CipMessageTypes.ClientDisconnect:
                    Close("客户端发送断开通知");
                    break;
                case CipMessageTypes.Ack:
                case CipMessageTypes.TimeSync:
                case CipMessageTypes.ConnAccepted:
                case CipMessageTypes.ConnRefused:
                case CipMessageTypes.WhoIs:
                    break;
                default:
                    Log.Info(string.Format("{0} 收到未处理的 CIP 消息类型 0x{1:X2}", PeerText(), m.Type));
                    break;
            }
        }

        /// <summary>Sign-On：校验 IPID 是否在 IP 表中，注册成功或拒绝</summary>
        void HandleSignOn(int ipid, string format)
        {
            Ipid = ipid;
            HasIpid = true;
            var entry = owner.Config.FindIpEntry(ipid);
            if (entry != null)
            {
                State = SessionState.Registered;
                SendRaw(CipCodec.AcceptRegistration(),
                    string.Format("注册成功(IPID 0x{0:X2} · {1})", ipid, entry.Name ?? ""));
                Log.Info(string.Format("√ 客户端 {0} 以 IPID 0x{1:X2} ({2}) 注册成功 [{3}]",
                    RemoteAddr, ipid, entry.Name ?? "", format));
                owner.KickExisting(ipid, this);
                owner.OnSessionUpdated(this);
            }
            else
            {
                State = SessionState.Refused;
                SendRaw(CipCodec.RefuseRegistration(),
                    string.Format("注册失败(IPID 0x{0:X2} 不在 IP 表中)", ipid));
                Log.Info(string.Format("× 客户端 {0} 使用 IPID 0x{1:X2} 注册被拒(不在 IP 表中)", RemoteAddr, ipid));
                owner.OnSessionUpdated(this);
                var t = new Timer(delegate { Close("IPID 注册被拒"); }, null, 1000, Timeout.Infinite);
            }
        }

        void HandleJoinEvent(byte[] payload)
        {
            byte joinType;
            byte[] data;
            if (!CipCodec.TryParseJoinEvent(payload, out joinType, out data))
            {
                Log.Info("无法解析 Join 事件负载");
                return;
            }
            switch (joinType)
            {
                case CipJoinTypes.Digital:
                    if (data.Length >= 2)
                    {
                        int join = (data[0] | ((data[1] & 0x7F) << 8)) + 1;
                        bool state = (data[1] & 0x80) == 0;
                        owner.Joins.SetDigital(join, state, JoinSource.Remote, PeerText());
                    }
                    break;
                case CipJoinTypes.Analog:
                case CipJoinTypes.AnalogAlt:
                    for (int i = 0; i + 3 < data.Length; i += 4)
                    {
                        int join = ((data[i] << 8) | data[i + 1]) + 1;
                        int value = (data[i + 2] << 8) | data[i + 3];
                        owner.Joins.SetAnalog(join, value, JoinSource.Remote, PeerText());
                    }
                    break;
                case CipJoinTypes.Serial:
                case CipJoinTypes.SerialAlt:
                case CipJoinTypes.SerialUtf8:
                case CipJoinTypes.SerialUnicode:
                    if (data.Length >= 3)
                    {
                        int join = ((data[0] << 8) | data[1]) + 1;
                        string text = Encoding.UTF8.GetString(data, 3, data.Length - 3);
                        owner.Joins.SetSerial(join, text, JoinSource.Remote, PeerText());
                    }
                    break;
                case CipJoinTypes.Update:
                    if (data.Length >= 1 && data[0] == 0x00)
                    {
                        Log.Info(string.Format("客户端 {0} 发送 Update Request，回放全部 join 状态", PeerText()));
                        SendAllJoins();
                    }
                    break;
                case CipJoinTypes.TimeSync:
                    break;
                case CipJoinTypes.SmartObject:
                    Log.Info(string.Format("{0} SmartObject 事件(暂不模拟): {1}", PeerText(), Logging.Log.ToHex(data)));
                    break;
                default:
                    Log.Info(string.Format("{0} 未知 join 类型 0x{1:X2}: {2}", PeerText(), joinType, Logging.Log.ToHex(data)));
                    break;
            }
        }

        /// <summary>Update Request 应答：回放所有被触碰过的 join 状态，最后发 Update 结束/就绪标记</summary>
        void SendAllJoins()
        {
            foreach (int j in owner.Joins.GetTouchedDigital())
            {
                if (owner.Joins.GetDigital(j))
                    SendRaw(CipCodec.DigitalJoinEvent(j, true), string.Format("回放 数字 join {0} = 高", j));
            }
            foreach (int j in owner.Joins.GetTouchedAnalog())
            {
                int v = owner.Joins.GetAnalog(j);
                SendRaw(CipCodec.AnalogJoinEvent(j, v), string.Format("回放 模拟 join {0} = {1}", j, v));
            }
            foreach (int j in owner.Joins.GetTouchedSerial())
            {
                string s = owner.Joins.GetSerial(j);
                SendRaw(CipCodec.SerialJoinEvent(j, s), string.Format("回放 串口 join {0} = \"{1}\"", j, s));
            }
            SendRaw(CipCodec.UpdateEnded(), "Update 结束(0x16)");
            SendRaw(CipCodec.UpdateReady(), "Update 就绪(0x1C)");
        }

        /// <summary>Unicode 串口消息（0x12）</summary>
        void HandleUnicode(byte[] p)
        {
            if (p.Length >= 7 && p[6] == 0x39)
            {
                Log.Info(string.Format("{0} SmartObject 串口(Unicode)消息: {1}", PeerText(), Logging.Log.ToHex(p)));
                return;
            }
            if (p.Length >= 11)
            {
                int join = ((p[7] << 8) | p[8]) + 1;
                string text = Encoding.UTF8.GetString(p, 10, p.Length - 10);
                owner.Joins.SetSerial(join, text, JoinSource.Remote, PeerText());
            }
            else if (p.Length >= 9)
            {
                // 兼容另一派实现：join 位于 p[5..6]，文本位于 p[8..]
                int join = ((p[5] << 8) | p[6]) + 1;
                string text = Encoding.UTF8.GetString(p, 8, p.Length - 8);
                owner.Joins.SetSerial(join, text, JoinSource.Remote, PeerText());
            }
        }

        // ===================== 保活与关闭 =====================

        void PingTick(object state)
        {
            if (closed) return;
            if (State == SessionState.Connecting && (DateTime.Now - LastActivity).TotalSeconds > 30)
            {
                Close("注册超时(客户端未完成 Sign-On)");
                return;
            }
            if (missedPings >= MaxMissedPings)
            {
                Close(string.Format("心跳超时(连续 {0} 次无应答)", MaxMissedPings));
                return;
            }
            missedPings++;
            if (FramingMode == Framing.Packetized)
                SendPacketized(CipCodec.PacketizedHeartbeat(), "心跳(heartbeat)");
            else
                SendRaw(CipCodec.Ping(), "Ping(心跳)");
        }

        public void Close(string reason)
        {
            if (closed) return;
            closed = true;
            State = SessionState.Closed;
            try { pingTimer.Dispose(); } catch { }
            try { tcp.Close(); } catch { }
            Log.Info(string.Format("客户端 {0} 断开: {1}", PeerText(), reason));
            var h = Disposed;
            if (h != null) h(this);
        }
    }
}
