using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Text.RegularExpressions;

namespace QuietScreen.Core
{
    /// <summary>
    /// 定时关机模块。
    ///
    /// 可靠性设计 (与开源 ShutdownTimerClassic 一致的业界最佳实践):
    /// 倒计时交给系统自带的 shutdown.exe (/s /t 秒数), 而不是程序自己计时——
    ///   1. 程序崩溃/退出后系统级倒计时依然成立
    ///   2. 触发前 Windows 弹标准倒计时通知, 其他程序能正常收到关机消息保存数据
    ///   3. 取消用 shutdown /a, 由内核保证无竞态
    /// 不用 ExitWindowsEx: 需要自行 AdjustTokenPrivileges, 出错率高且无系统级取消能力。
    /// </summary>
    public static class ShutdownService
    {
        private static Timer _dailyTimer;
        private static DateTime _nextDaily;
        private static DateTime? _osPendingUntil;
        private static readonly object Gate = new object();

        public static bool DailyEnabled { get; private set; }
        public static DateTime? NextDailyTime => DailyEnabled ? _nextDaily : (DateTime?)null;
        public static DateTime? PendingUntil => _osPendingUntil;

        /// <summary>程序启动时按配置武装每日定时 (未启用则不动作)</summary>
        public static void Initialize()
        {
            if (AppConfig.Current.ShutdownDailyEnabled)
                EnableDaily();
        }

        public static void EnableDaily()
        {
            lock (Gate)
            {
                var t = ParseTime(AppConfig.Current.ShutdownDailyTime);
                if (t == null)
                {
                    Logger.Error("定时关机时间无效: " + AppConfig.Current.ShutdownDailyTime);
                    return;
                }
                ArmNext(t.Value);
                DailyEnabled = true;
                Logger.Ok($"每日定时关机已启用: 每天 {AppConfig.Current.ShutdownDailyTime} (下次 {_nextDaily:MM-dd HH:mm})");
            }
        }

        public static void DisableDaily()
        {
            lock (Gate)
            {
                _dailyTimer?.Dispose();
                _dailyTimer = null;
                DailyEnabled = false;
                Logger.Info("每日定时关机已关闭");
            }
        }

        /// <summary>时间被改后重新武装 (保持启用状态)</summary>
        public static void RearmIfEnabled()
        {
            if (DailyEnabled) EnableDaily();
        }

        /// <summary>N 秒后关机 (经系统 shutdown 安排, 可随时取消)</summary>
        public static bool ScheduleIn(int seconds, out string message)
        {
            if (seconds < 10) seconds = 10;
            lock (Gate)
            {
                var r = Run("shutdown", $"/s /t {seconds} /c \"QuietScreen 定时关机\"");
                if (r.Success)
                {
                    _osPendingUntil = DateTime.Now.AddSeconds(seconds);
                    message = $"已安排 {seconds} 秒后关机 (系统倒计时, 可随时取消)";
                    Logger.Ok(message);
                }
                else
                {
                    message = "安排关机失败: " + r.Output;
                    Logger.Error(message);
                }
                return r.Success;
            }
        }

        /// <summary>取消已安排的关机 (shutdown /a, 对任何来源的系统级倒计时有效)</summary>
        public static bool Abort(out string message)
        {
            lock (Gate)
            {
                var r = Run("shutdown", "/a");
                _osPendingUntil = null;
                if (r.Success)
                {
                    message = "已取消关机";
                    Logger.Ok(message);
                    return true;
                }
                message = string.IsNullOrEmpty(r.Output) ? "当前没有可取消的关机" : "取消失败: " + r.Output;
                Logger.Info(message);
                return false;
            }
        }

        // ---------------- 内部 ----------------

        private static void ArmNext(TimeSpan time)
        {
            var now = DateTime.Now;
            var next = new DateTime(now.Year, now.Month, now.Day, time.Hours, time.Minutes, 0);
            if (next <= now) next = next.AddDays(1);
            _nextDaily = next;
            int dueMs = Math.Max(1000, (int)(next - now).TotalMilliseconds);
            _dailyTimer?.Dispose();
            _dailyTimer = new Timer(_ => DailyFire(), null, dueMs, Timeout.Infinite);
        }

        private static void DailyFire()
        {
            try
            {
                var grace = Math.Max(10, AppConfig.Current.ShutdownGraceSec);
                Logger.Warn($"每日定时关机触发, {grace} 秒后关机");
                ToastService.Show("QuietScreen", $"每日定时关机触发, {grace} 秒后关机\n可打开程序取消");
                ScheduleIn(grace, out _);
            }
            catch (Exception ex)
            {
                Logger.Error("每日关机触发异常: " + ex.Message);
            }
            finally
            {
                // 重新武装到明天 (一次性定时器, 避免系统时间变化导致漂移)
                lock (Gate)
                {
                    if (DailyEnabled)
                    {
                        var t = ParseTime(AppConfig.Current.ShutdownDailyTime);
                        if (t != null) ArmNext(t.Value);
                    }
                }
            }
        }

        public static TimeSpan? ParseTime(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            var m = Regex.Match(s.Trim(), @"^(\d{1,2}):(\d{2})$");
            if (!m.Success) return null;
            int h = int.Parse(m.Groups[1].Value), mi = int.Parse(m.Groups[2].Value);
            if (h > 23 || mi > 59) return null;
            return new TimeSpan(h, mi, 0);
        }

        private static (bool Success, string Output) Run(string exe, string args)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = Path.Combine(Environment.SystemDirectory, exe),
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (var p = Process.Start(psi))
                {
                    string stdout = p.StandardOutput.ReadToEnd().Trim();
                    string stderr = p.StandardError.ReadToEnd().Trim();
                    p.WaitForExit(10000);
                    bool ok = p.ExitCode == 0;
                    return (ok, (stderr + " " + stdout).Trim());
                }
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }
    }
}
