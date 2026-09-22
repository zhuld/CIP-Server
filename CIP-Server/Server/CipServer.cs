// ============================================================================
//  CIP-Server — Crestron CIP 协议模拟服务器（MIT License · 代码由 AI 辅助生成）
//  文件: Server/CipServer.cs
//  说明: TCP 监听器与会话容器。
//        - 41794 端口 → raw 框架会话；41795 端口 → packetized 框架会话
//        - 订阅 JoinState 的本地变更事件（UI 手动操作），广播给全部已注册会话
//        - 客户端发来的 join（Remote 来源）只更新状态、不回环广播，
//          避免把消息弹回给发送方
// ============================================================================

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using CipServerSim.Logging;
using CipServerSim.Protocol;
using CipServerSim.State;

namespace CipServerSim.Server
{
    /// <summary>
    /// CIP 模拟服务器：监听 41794（raw）与 41795（packetized），
    /// 管理会话并把本地 join 变化广播给已注册客户端。
    /// </summary>
    public sealed class CipServer
    {
        readonly DeviceConfig config;
        readonly JoinState joins;
        readonly List<CipSession> sessions = new List<CipSession>();
        readonly object sync = new object();
        TcpListener l94, l95;

        public bool Running { get; private set; }
        public DeviceConfig Config { get { return config; } }
        public JoinState Joins { get { return joins; } }

        public event Action<CipSession> SessionChanged;
        public event Action<CipSession> SessionRemoved;

        public CipServer(DeviceConfig config, JoinState joins)
        {
            this.config = config;
            this.joins = joins;
            joins.DigitalChanged += Joins_DigitalChanged;
            joins.AnalogChanged += Joins_AnalogChanged;
            joins.SerialChanged += Joins_SerialChanged;
        }

        public void Start()
        {
            if (Running) return;
            try
            {
                l94 = new TcpListener(IPAddress.Any, config.Port41794);
                l94.Start();
                Log.Info(string.Format("√ CIP 模拟服务器已启动 · 型号 {0} · 固件 {1} · 主机名 {2}",
                    config.Model, config.Firmware, config.HostName));
                Log.Info(string.Format("  监听 0.0.0.0:{0} (CIP raw)", config.Port41794));

                if (config.Enable41795)
                {
                    l95 = new TcpListener(IPAddress.Any, config.Port41795);
                    l95.Start();
                    Log.Info(string.Format("  监听 0.0.0.0:{0} (packetized)", config.Port41795));
                }
                Log.Info(string.Format("  IP 表: {0}", config.IpTableSummary()));

                // 必须在开始 Accept 之前置位 Running：OnAccept 用 Running 判断服务器是否仍在运行，
                // 否则首个连接可能被误判为“已停止”而立即关闭。
                Running = true;

                var listen94 = l94;
                listen94.BeginAcceptTcpClient(ar => OnAccept(listen94, ar, Framing.Raw), null);
                if (l95 != null)
                {
                    var listen95 = l95;
                    listen95.BeginAcceptTcpClient(ar => OnAccept(listen95, ar, Framing.Packetized), null);
                }
            }
            catch (Exception ex)
            {
                // 已绑定的端口必须释放，否则下次启动会报“端口被占用”
                Running = false;
                StopListeners();
                Log.Info(string.Format("× 服务器启动失败: {0}", ex.Message));
                throw;
            }
        }

        public void Stop()
        {
            if (!Running) return;
            Running = false;
            CipSession[] snapshot;
            lock (sync) { snapshot = sessions.ToArray(); sessions.Clear(); }
            foreach (var s in snapshot) s.Close("服务器停止");
            StopListeners();
            Log.Info("服务器已停止");
        }

        /// <summary>停止并释放两个监听器（可重复调用）</summary>
        void StopListeners()
        {
            try { if (l94 != null) l94.Stop(); } catch { }
            try { if (l95 != null) l95.Stop(); } catch { }
            l94 = null;
            l95 = null;
        }

        void OnAccept(TcpListener l, IAsyncResult ar, Framing framing)
        {
            TcpClient client;
            try { client = l.EndAcceptTcpClient(ar); }
            catch { return; }   // 监听器已停止，或该次 Accept 被放弃
            try { l.BeginAcceptTcpClient(a => OnAccept(l, a, framing), null); }
            catch { }           // 重新挂起失败（如 Stop 已调用）不影响已建立的会话

            // 与 Stop() 竞争时不能再入列：否则该会话永远不会被关闭/移除
            var s = new CipSession(this, client, framing);
            bool accepted;
            lock (sync)
            {
                accepted = Running;
                if (accepted) sessions.Add(s);
            }
            if (!accepted)
            {
                s.Close("服务器已停止");
                return;
            }

            // 事件订阅必须早于 Start()：客户端若在连接后立刻发送断开通知，
            // 会话会在订阅之前就关闭，导致 Disposed 丢失、列表残留已断开的行。
            s.Disposed += OnSessionDisposed;
            Log.Info(string.Format("客户端接入: {0} · 模式 {1}",
                s.RemoteAddr, framing == Framing.Packetized ? "packetized(41795)" : "raw(41794)"));
            var h = SessionChanged;
            if (h != null) h(s);
            s.Start();
        }

        void OnSessionDisposed(CipSession s)
        {
            lock (sync) sessions.Remove(s);
            var h = SessionRemoved;
            if (h != null) h(s);
        }

        public void OnSessionUpdated(CipSession s)
        {
            var h = SessionChanged;
            if (h != null) h(s);
        }

        /// <summary>同一 IPID 只允许一个会话：新连接抢占时断开旧连接</summary>
        public void KickExisting(int ipid, CipSession except)
        {
            CipSession[] snapshot;
            lock (sync) snapshot = sessions.ToArray();
            foreach (var s in snapshot)
            {
                if (s != except && s.HasIpid && s.Ipid == ipid && s.State == SessionState.Registered)
                {
                    s.SendRaw(CipCodec.DisconnectNotice(),
                        string.Format("中控断开通知(IPID 0x{0:X2} 被新连接抢占)", ipid));
                    s.Close("IPID 被新连接抢占");
                }
            }
        }

        /// <summary>当前全部会话快照（供 UI 刷新客户端列表）</summary>
        public CipSession[] GetSessionsSnapshot()
        {
            lock (sync) return sessions.ToArray();
        }

        void SendToRegistered(byte[] msg)
        {
            CipSession[] snapshot;
            lock (sync) snapshot = sessions.ToArray();
            foreach (var s in snapshot)
            {
                if (s.State == SessionState.Registered) s.SendRaw(msg);
            }
        }

        // 本地（UI）join 变化 → 广播给所有已注册客户端
        void Joins_DigitalChanged(int join, bool value, JoinSource src, string info)
        {
            if (src == JoinSource.Remote) return;
            SendToRegistered(CipCodec.DigitalJoinEvent(join, value));
        }

        void Joins_AnalogChanged(int join, int value, JoinSource src, string info)
        {
            if (src == JoinSource.Remote) return;
            SendToRegistered(CipCodec.AnalogJoinEvent(join, value));
        }

        void Joins_SerialChanged(int join, string value, JoinSource src, string info)
        {
            if (src == JoinSource.Remote) return;
            SendToRegistered(CipCodec.SerialJoinEvent(join, value));
        }
    }
}
