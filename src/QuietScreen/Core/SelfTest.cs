using System;
using System.Collections.Generic;
using System.Linq;
using QuietScreen.Core.Vdd;

namespace QuietScreen.Core
{
    /// <summary>
    /// 只读自检: QuietScreen.exe --selftest
    /// 验证配置 / 日志 / 显示枚举 / 驱动状态 / 设备句柄 / IOCTL 版本 / 代理端口。
    /// 不创建、不移除虚拟屏, 不切换显示模式 —— 可随时安全运行。
    /// </summary>
    public static class SelfTest
    {
        private static readonly List<string> Results = new List<string>();

        private static void Check(string name, bool ok, string detail = "")
        {
            Results.Add($"[{(ok ? "PASS" : "FAIL")}] {name}{(string.IsNullOrEmpty(detail) ? "" : " — " + detail)}");
        }

        public static int Run()
        {
            Console.WriteLine("==== QuietScreen 只读自检 (不改变任何显示设置) ====");

            // 1. 配置
            try
            {
                AppConfig.Load();
                Check("配置加载", true, AppConfig.ConfigPath);
            }
            catch (Exception ex) { Check("配置加载", false, ex.Message); }

            // 2. 日志 (写 MARKER -> 轮询读回验证真正落盘; 写入线程是异步的, 最多等 2 秒)
            try
            {
                Logger.Init();
                Logger.Info("MARKER-QUIET-SELFTEST");
                bool found = false;
                for (int i = 0; i < 40 && !found; i++)
                {
                    System.Threading.Thread.Sleep(50);
                    found = System.IO.File.ReadAllText(Logger.LogFile).Contains("MARKER-QUIET-SELFTEST");
                }
                string detail = Logger.LogFile;
                if (!found && !System.IO.File.Exists(Logger.LogFile + ".alt"))
                    detail += " (若持续失败: 检查杀软/HIPS 是否拦截本程序写入)";
                Check("日志写入(读回验证)", found, detail);

                // 路径逐字符编码 (与外部工具对照用)
                var codes = string.Join(" ", Logger.LogFile.Select(c => ((int)c).ToString("X4")));
                Console.WriteLine("PATH-CODES: " + codes);
                var fi = new System.IO.FileInfo(Logger.LogFile);
                Console.WriteLine($"FILE-VIEW: lastwrite={fi.LastWriteTime:yyyy-MM-dd HH:mm:ss} size={fi.Length}");
                var tail = System.IO.File.ReadAllLines(Logger.LogFile);
                Console.WriteLine("FILE-TAIL: " + string.Join(" | ", tail.Skip(Math.Max(0, tail.Length - 3))));
            }
            catch (Exception ex) { Check("日志写入", false, ex.Message); }

            // 3. 显示器枚举
            try
            {
                var all = VddDisplay.GetAllDisplays();
                foreach (var d in all)
                {
                    string kind = d.IsParsec ? " [Parsec虚拟屏]" : "";
                    Console.WriteLine($"  显示器: {d.DeviceName}  active={d.Active} primary={d.Primary} " +
                                      $"{d.Width}x{d.Height}  uid={d.Address} drvIdx={d.DriverIndex}{kind}");
                }
                Check("显示器枚举", all.Count > 0, $"{all.Count} 个显示设备");

                var screens = DisplayService.GetDisplays();
                var primary = screens.FirstOrDefault(s => s.Primary);
                Check("主屏判定", primary != null,
                    primary == null ? "" : $"{primary.ShortName} {primary.Resolution} virtual={primary.IsVirtual}");

                var layout = RemoteDisplayService.GetLayout();
                Check("远程桌面尺寸", !layout.Success || layout.MeetsMinimum,
                    layout.Success ? layout.Message : "当前没有虚拟主屏, 跳过");
            }
            catch (Exception ex) { Check("显示器枚举", false, ex.Message); }

            // 4. 驱动状态 (SetupAPI + CfgMgr)
            try
            {
                var status = VddDriver.QueryStatus(out var ver);
                Check("驱动状态查询", true, $"{status} v{ver}");
            }
            catch (Exception ex) { Check("驱动状态查询", false, ex.Message); }

            // 5. 设备句柄 + 版本 IOCTL (只读, 不发 ADD/REMOVE/UPDATE)
            try
            {
                bool opened = VddDriver.OpenHandle(out var h);
                Check("打开设备句柄", opened);
                if (opened)
                {
                    bool gotVer = VddDriver.GetVersion(h, out var v);
                    Check("版本 IOCTL", gotVer, "driver " + v);
                    VddDriver.CloseHandle(h);
                    Check("关闭句柄", true);
                }
            }
            catch (Exception ex) { Check("设备句柄/IOCTL", false, ex.Message); }

            // 6. 控制器线程 (心跳线程就绪, 因无句柄不会发 UPDATE)
            try
            {
                VddService.EnsureStarted();
                System.Threading.Thread.Sleep(2500);
                Check("控制器启动", VddController.IsRunning,
                    $"handleReady={VddController.IsHandleReady} suspended={VddController.IsSuspended}");
            }
            catch (Exception ex) { Check("控制器启动", false, ex.Message); }

            // 7. 代理端口探测 (TCP 连接, 只读)
            try
            {
                var cfg = AppConfig.Current;
                if (cfg.AgentPort > 0)
                {
                    bool listening = AgentService.IsPortListening(cfg.AgentPort);
                    Check("代理端口探测", true, $"port {cfg.AgentPort} listening={listening} (仅探测, 不拉起)");
                }
            }
            catch (Exception ex) { Check("代理端口探测", false, ex.Message); }

            Console.WriteLine("---- 结果 ----");
            foreach (var r in Results) Console.WriteLine(r);
            int failed = Results.Count(r => r.Contains("[FAIL]"));
            Console.WriteLine($"==== 完成: {Results.Count - failed} 通过, {failed} 失败 ====");
            Logger.Info($"自检完成: {Results.Count - failed} 通过, {failed} 失败");
            return failed == 0 ? 0 : 1;
        }
    }
}
