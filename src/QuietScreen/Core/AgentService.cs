using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace QuietScreen.Core
{
    /// <summary>远控代理守护 (向日葵等): 端口监听检测 + 拉起进程</summary>
    public static class AgentService
    {
        private static readonly object StartGate = new object();

        public static bool IsConfigured()
        {
            var c = AppConfig.Current;
            return c.AgentKeepaliveEnabled
                && !string.IsNullOrWhiteSpace(c.AgentExe)
                && File.Exists(c.AgentExe)
                && c.AgentPort > 0;
        }

        /// <summary>
        /// 代理端口是否在监听 (localhost TCP 连接探测, 无需管理员权限)。
        /// 用同步 Socket + Linger(0) 关闭: 实测 TcpClient+ConnectAsync 每次探测
        /// 会泄漏约 1 个内核句柄, 长期守护下会持续增长; 同步方式零泄漏,
        /// 且 RST 关闭不留 TIME_WAIT。
        /// </summary>
        public static bool IsPortListening(int port)
        {
            if (port <= 0) return false;
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                socket.LingerState = new LingerOption(true, 0);
                socket.Connect(IPAddress.Loopback, port);
                return socket.Connected;
            }
            catch
            {
                return false;
            }
            finally
            {
                try { socket.Close(); } catch { }
            }
        }

        /// <summary>确保代理在运行; 未监听则拉起。返回最终是否在监听。</summary>
        public static bool EnsureAgentRunning(out string action)
        {
            action = null;
            var c = AppConfig.Current;
            if (!IsConfigured()) { action = "未配置"; return false; }

            if (IsPortListening(c.AgentPort)) { action = "正常"; return true; }

            lock (StartGate)
            {
                // 双重检查, 避免并发重复拉起
                if (IsPortListening(c.AgentPort)) { action = "正常"; return true; }

                Logger.Warn($"代理端口 {c.AgentPort} 未监听, 正在拉起...");
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = c.AgentExe,
                        Arguments = c.AgentArgs ?? "",
                        WorkingDirectory = Path.GetDirectoryName(c.AgentExe) ?? "",
                        WindowStyle = ProcessWindowStyle.Hidden,
                        UseShellExecute = true
                    };
                    Process.Start(psi);
                }
                catch (Exception ex)
                {
                    action = "启动失败: " + ex.Message;
                    Logger.Error("拉起代理失败: " + ex.Message);
                    return false;
                }

                // 最多等 8 秒
                for (int i = 0; i < 8; i++)
                {
                    Thread.Sleep(1000);
                    if (IsPortListening(c.AgentPort))
                    {
                        action = "已重新启动";
                        Logger.Ok($"代理已拉起 (端口 {c.AgentPort} 监听中)");
                        return true;
                    }
                }
                action = "启动后仍未监听";
                Logger.Warn("代理启动后端口仍未监听");
                return false;
            }
        }
    }
}
