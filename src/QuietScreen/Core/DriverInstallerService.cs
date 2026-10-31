using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using QuietScreen.Core.Vdd;

namespace QuietScreen.Core
{
    /// <summary>
    /// 驱动一键安装: 内嵌 Parsec 官方驱动安装包 (parsec-vdd-0.45.0.0.exe, Parsec Cloud Inc.),
    /// 安装方式与开源项目 nomi-san/parsec-vdd 的 setup.iss 完全一致 —— 提权后 /S 静默安装,
    /// 轮询驱动状态直到出现。用户从此不需要自己下载和配置任何外部软件。
    /// </summary>
    public static class DriverInstallerService
    {
        private const string ResourceUri = "pack://application:,,,/Assets/parsec-vdd-0.45.0.0.exe";

        public static bool IsInstalled()
        {
            return VddDriver.QueryStatus(out _) == VddDriver.DriverStatus.OK;
        }

        public static string StatusLine()
        {
            var s = VddDriver.QueryStatus(out var v);
            if (s == VddDriver.DriverStatus.OK) return $"驱动正常 (v{v})";
            if (s == VddDriver.DriverStatus.NOT_INSTALLED) return "未安装驱动";
            return "驱动状态: " + s;
        }

        /// <summary>提取内嵌安装包 → UAC 提权静默安装 → 轮询检测。在 UI 线程外调用。</summary>
        public static Task<(bool Success, string Message)> InstallAsync()
        {
            return Task.Run(() =>
            {
                // 1. 提取内嵌驱动安装包 (Parsec 官方原版)
                string tmp = Path.Combine(Path.GetTempPath(), "parsec-vdd-0.45.0.0.exe");
                try
                {
                    var sri = Application.GetResourceStream(new Uri(ResourceUri));
                    if (sri == null) return (false, "内嵌安装包缺失");
                    using (var fs = File.Create(tmp))
                        sri.Stream.CopyTo(fs);
                }
                catch (Exception ex)
                {
                    return (false, "提取安装包失败: " + ex.Message);
                }

                Logger.Info("开始安装虚拟屏驱动 (需要管理员确认)...");
                // 2. UAC 提权 + NSIS 静默安装
                Process elevated;
                try
                {
                    elevated = Process.Start(new ProcessStartInfo
                    {
                        FileName = tmp,
                        Arguments = "/S",
                        UseShellExecute = true,
                        Verb = "runas"
                    });
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    return (false, "已取消 (UAC 未确认)");
                }
                catch (Exception ex)
                {
                    return (false, "启动安装器失败: " + ex.Message);
                }

                // 3. 轮询驱动状态 (安装通常 5-20 秒)
                for (int i = 0; i < 60; i++)
                {
                    Thread.Sleep(2000);
                    var st = VddDriver.QueryStatus(out var v);
                    if (st != VddDriver.DriverStatus.NOT_INSTALLED)
                    {
                        try { elevated?.Refresh(); } catch { }
                        Logger.Ok($"虚拟屏驱动安装完成 (状态 {st}, v{v})");
                        return (true, $"驱动安装成功 (v{v})");
                    }
                }
                return (false, "等待超时: 驱动未出现, 请手动查看安装器窗口");
            });
        }
    }
}
