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
                l94.BeginAcceptTcpClient(ar => OnAccept(l94, ar, Framing.Raw), null);

                if (config.Enable41795)
                {
                    l95 = new TcpListener(IPAddress.Any, config.Port41795);
                    l95.Start();
                    Log.Info(string.Format("  监听 0.0.0.0:{0} (packetized)", config.Port41795));
                    l95.BeginAcceptTcpClient(ar => OnAccept(l95, ar, Framing.Packetized), null);
                }
                Log.Info(string.Format("  IP 表: {0}", config.IpTableSummary()));
                Running = true;
            }
            catch (Exception ex)
            {
                Running = false;
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
            try { if (l94 != null) l94.Stop(); } catch { }
            try { if (l95 != null) l95.Stop(); } catch { }
            l94 = null;
            l95 = null;
            Log.Info("服务器已停止");
        }

        void OnAccept(TcpListener l, IAsyncResult ar, Framing framing)
        {
            TcpClient client;
            try { client = l.EndAcceptTcpClient(ar); }
            catch { return; }
            if (!Running)
            {
                try { client.Close(); } catch { }
                return;
            }
            try { l.BeginAcceptTcpClient(a => OnAccept(l, a, framing), null); }
            catch { }

            var s = new CipSession(this, client, framing);
            lock (sync) sessions.Add(s);
            s.Disposed += OnSessionDisposed;
            Log.Info(string.Format("客户端接入: {0} · 模式 {1}",
                s.RemoteAddr, framing == Framing.Packetized ? "packetized(41795)" : "raw(41794)"));
            var h = SessionChanged;
            if (h != null) h(s);
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
