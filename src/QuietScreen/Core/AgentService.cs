using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace QuietScreen.Core
{
    /// <summary>
    /// 远控代理守护 (向日葵等): 两种健康模式 —
    ///   端口模式 (AgentPort &gt; 0): 端口监听检测 + 拉起进程, 适合本地监听型代理;
    ///   进程模式 (AgentPort = 0): 代理进程存在即健康, 适合云端中继型代理
    ///   (如向日葵出站连接, 本地无监听端口 — 端口检测对此类代理永远误报)。
    /// </summary>
    public static class AgentService
    {
        private static readonly object StartGate = new object();

        public static bool IsConfigured()
        {
            var c = AppConfig.Current;
            return c.AgentKeepaliveEnabled
                && !string.IsNullOrWhiteSpace(c.AgentExe)
                && File.Exists(c.AgentExe);
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

        private static bool ProcessRunning(string procName)
        {
            return System.Diagnostics.Process.GetProcessesByName(procName).Length > 0;
        }

        /// <summary>确保代理在运行 (端口模式: 未监听则拉起; 进程模式: 进程不在则拉起)。</summary>
        public static bool EnsureAgentRunning(out string action)
        {
            action = null;
            var c = AppConfig.Current;
            if (!IsConfigured()) { action = "未配置"; return false; }

            string procName = Path.GetFileNameWithoutExtension(c.AgentExe);
            bool portMode = c.AgentPort > 0;

            if (portMode && IsPortListening(c.AgentPort)) { action = "正常"; return true; }
            if (!portMode && ProcessRunning(procName)) { action = "正常 (进程模式)"; return true; }

            lock (StartGate)
            {
                // 双重检查, 避免并发重复拉起
                if (portMode && IsPortListening(c.AgentPort)) { action = "正常"; return true; }
                if (!portMode && ProcessRunning(procName)) { action = "正常 (进程模式)"; return true; }

                // 端口模式特有: 进程在但端口不开 = 单实例交接/配置失配, 重复拉起只堆僵尸
                if (portMode && ProcessRunning(procName))
                {
                    action = $"代理进程已在运行 ({ProcessRunningCount(procName)} 个) 但端口 {c.AgentPort} 未监听";
                    Logger.Warn(action + " — 不重复拉起");
                    return false;
                }

                Logger.Warn(portMode
                    ? $"代理端口 {c.AgentPort} 未监听, 正在拉起..."
                    : $"代理进程未运行, 正在拉起...");
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

                // 最多等 8 秒 (端口模式等端口, 进程模式等进程出现)
                for (int i = 0; i < 8; i++)
                {
                    Thread.Sleep(1000);
                    bool ok = portMode ? IsPortListening(c.AgentPort) : ProcessRunning(procName);
                    if (ok)
                    {
                        action = portMode ? "已重新启动" : "已重新启动 (进程模式)";
                        Logger.Ok(portMode
                            ? $"代理已拉起 (端口 {c.AgentPort} 监听中)"
                            : $"代理已拉起 (进程运行中)");
                        return true;
                    }
                }
                action = portMode ? "启动后端口仍未监听" : "启动后进程仍未出现";
                Logger.Warn(action);
                return false;
            }
        }

        private static int ProcessRunningCount(string procName)
        {
            return System.Diagnostics.Process.GetProcessesByName(procName).Length;
        }
    }
}
