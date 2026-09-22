// ============================================================================
//  CIP-Server — Crestron CIP 协议模拟服务器（MIT License · 代码由 AI 辅助生成）
//  文件: Server/CipSession.cs
//  说明: 单个客户端连接的完整会话状态机。
//
//  会话生命周期（raw 41794 为例）:
//    TCP Accept → 发送 Who-Is(0F 00 01 02)
//    → 客户端 Sign-On(0x0A 或 0x01，携带 IPID)
//    → 校验 IP 表: 命中→注册成功(02 00 04 00 00 00 1F)；
//                  未命中→拒绝(02 00 03 FF FF 02) 并在 1s 后断开
//    → 客户端 Update Request → 回放全部 join 状态 + 0x16 + 0x1C
//    → Ping/Pong 保活（10s 间隔，连续 5 次无应答断开）
//
//  线程模型: RxLoop 在独立后台线程阻塞读 socket；PingTick 由线程池定时器每 10s 触发；
//  二者都由 CipServer 在订阅 Disposed 事件后调用 Start() 启动（避免事件丢失）。
//  所有 socket 写入经 sendLock 串行化；closed 为终止标志（volatile）。
//  写失败一律按连接失效处理并关闭会话，避免僵尸会话占用 IPID。
// ============================================================================

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
        /// <summary>心跳间隔（毫秒）</summary>
        public const int PingIntervalMs = 10000;

        /// <summary>连续多少次心跳没有任何数据往来即判定连接失效</summary>
        public const int MaxMissedPings = 5;

        /// <summary>注册被拒后延迟断开的时间，先让客户端读到拒绝应答（FF FF 02）</summary>
        const int RefuseCloseDelayMs = 1000;

        /// <summary>socket 写超时：避免无响应客户端把 UI 线程/心跳线程长期阻塞</summary>
        const int SendTimeoutMs = 5000;

        readonly CipServer owner;
        readonly TcpClient tcp;
        readonly NetworkStream stream;
        readonly Thread rxThread;
        readonly Timer pingTimer;
        readonly object sendLock = new object();
        readonly List<byte> acc = new List<byte>();

        Timer closeTimer;

        /// <summary>连续未应答的心跳数（rx 线程清零、定时器线程累加，故用 volatile）</summary>
        volatile int missedPings;

        /// <summary>
        /// 最后一次收到数据的时刻（以 Ticks 存储：32 位进程下 DateTime 结构存在撕裂读风险，
        /// long 本身不能声明 volatile，故读写都走 Volatile 保证 64 位原子性）
        /// </summary>
        long lastActivityTicks;

        volatile bool closed;

        public string RemoteAddr { get; private set; }
        public Framing FramingMode { get; private set; }
        public SessionState State { get; private set; }
        public int Ipid { get; private set; }
        public bool HasIpid { get; private set; }

        /// <summary>最后活动时间（界面显示用）</summary>
        public DateTime LastActivity { get { return new DateTime(Volatile.Read(ref lastActivityTicks)); } }

        public event Action<CipSession> Disposed;

        public CipSession(CipServer owner, TcpClient tcp, Framing framing)
        {
            this.owner = owner;
            this.tcp = tcp;
            FramingMode = framing;
            State = SessionState.Connecting;
            Volatile.Write(ref lastActivityTicks, DateTime.Now.Ticks);
            RemoteAddr = tcp.Client.RemoteEndPoint != null ? tcp.Client.RemoteEndPoint.ToString() : "未知";
            tcp.NoDelay = true;                 // join 报文都很短，关闭 Nagle 合并以降低交互延迟
            tcp.SendTimeout = SendTimeoutMs;
            stream = tcp.GetStream();
            stream.WriteTimeout = SendTimeoutMs;

            // 线程与定时器在此仅创建、不启动，由 Start() 统一启动：
            // CipServer 需要先完成 Disposed 事件订阅再开始收发，否则“连接后立刻断开”
            // 的客户端会让事件早于订阅发生而丢失，会话对象将永远留在会话列表中。
            rxThread = new Thread(RxLoop) { IsBackground = true, Name = "CIP-RX " + RemoteAddr };
            pingTimer = new Timer(PingTick, null, Timeout.Infinite, Timeout.Infinite);
        }

        /// <summary>启动收发线程与保活定时器；raw 模式下同时主动发送 Who-Is</summary>
        public void Start()
        {
            if (closed) return;
            rxThread.Start();
            pingTimer.Change(PingIntervalMs, PingIntervalMs);
            if (FramingMode == Framing.Raw)
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

        /// <summary>发送一条 CIP 消息，语义描述由报文内容自动生成（用于 join 广播）</summary>
        public void SendRaw(byte[] msg)
        {
            SendRaw(msg, null);
        }

        /// <summary>
        /// 发送一条 CIP 消息（packetized 模式自动加数据帧头）。
        /// semantic 为空时按报文内容自动生成语义描述。
        /// </summary>
        public void SendRaw(byte[] msg, string semantic)
        {
            if (closed || msg == null) return;
            byte[] wire = FramingMode == Framing.Packetized ? CipCodec.PacketizedData(msg) : msg;
            string desc = semantic ?? CipCodec.Describe(new CipMessage(msg[0], Slice(msg, 3)));
            if (!WriteWire(wire)) return;
            Log.Hex(true, string.Format("TX→ {0} · {1}", PeerText(), desc), wire);
        }

        /// <summary>直接发送 packetized 控制帧（心跳 / 建立连接应答），其内容不是 CIP 消息</summary>
        void SendPacketized(byte[] wire, string desc)
        {
            if (closed || wire == null) return;
            if (!WriteWire(wire)) return;
            Log.Hex(true, string.Format("TX→ {0} · {1}", PeerText(), desc), wire);
        }

        /// <summary>
        /// 串行化写入 socket。写失败（对端已断开 / 超时 / socket 被关闭）说明连接已不可用，
        /// 直接关闭会话，避免僵尸会话长期占用 IPID 与客户端列表行。
        /// </summary>
        bool WriteWire(byte[] wire)
        {
            try
            {
                lock (sendLock) stream.Write(wire, 0, wire.Length);
                return true;
            }
            catch (Exception ex)
            {
                Close("发送失败(" + ex.GetType().Name + ")");
                return false;
            }
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
                    if (n <= 0) break;                 // 对端正常关闭
                    TouchActivity();
                    for (int i = 0; i < n; i++) acc.Add(tmp[i]);

                    // raw 会话中出现 packetized 帧头（0D 00 00 xx）时自动切换解析模式。
                    // 与 raw Ping（0D 00 02 00 00）不冲突：第 3 字节分别为 0x00 / 0x02。
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

        /// <summary>记录一次数据往来：刷新最后活动时间并清零未应答心跳计数</summary>
        void TouchActivity()
        {
            Volatile.Write(ref lastActivityTicks, DateTime.Now.Ticks);
            missedPings = 0;
        }

        /// <summary>
        /// 解析 41794 raw 消息流：[type][0x00][len][payload]。
        /// 第 2 字节恒为 0x00，据此在脏数据时逐字节丢弃实现重新同步。
        /// </summary>
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
                // 延迟断开：先让客户端读到拒绝应答（FF FF 02）再关连接
                closeTimer = new Timer(RefuseCloseTick, null, RefuseCloseDelayMs, Timeout.Infinite);
            }
        }

        /// <summary>注册被拒后的延迟断开回调</summary>
        void RefuseCloseTick(object state)
        {
            Close("IPID 注册被拒");
        }

        /// <summary>
        /// 处理客户端发来的 Join 事件：数字/模拟/串口写入 JoinState（Remote 来源，不回环广播）；
        /// Update Request（join 类型 0x03 + 数据 0x00）触发全量回放。
        /// </summary>
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

        /// <summary>
        /// Unicode 串口消息（0x12）：两种已知布局都尝试解析，join 号位于 p[7..8]（文本 p[10..]）
        /// 或 p[5..6]（文本 p[8..]）。
        /// </summary>
        void HandleUnicode(byte[] p)
        {
            if (p.Length >= 7 && p[6] == 0x39)
            {
                // 0x39 = SmartObject 串口包装，本模拟器仅记录
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

        /// <summary>
        /// 每 10s 触发一次：未注册超时判定 → 心跳超时判定 → 发送一次心跳。
        /// 连续 MaxMissedPings 次心跳期间没有任何数据往来（rx 线程收到任何数据都会
        /// 清零 missedPings）即判定连接失效，约 60s 后断开。
        /// </summary>
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

        /// <summary>关闭会话（幂等）：释放定时器与 socket，并以 Disposed 事件通知服务器摘除会话</summary>
        public void Close(string reason)
        {
            if (closed) return;
            closed = true;
            State = SessionState.Closed;
            try { pingTimer.Dispose(); } catch { }
            try { if (closeTimer != null) closeTimer.Dispose(); } catch { }
            try { tcp.Close(); } catch { }
            Log.Info(string.Format("客户端 {0} 断开: {1}", PeerText(), reason));
            var h = Disposed;
            if (h != null) h(this);
        }
    }
}
