using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using QuietScreen.Core;
using QuietScreen.Core.Vdd;

namespace QuietScreen
{
    public partial class App : Application
    {
        public static Mutex SingleMutex;
        public static EventWaitHandle ShowMainSignal;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var args = e.Args;

            // ---- 只读自检模式: --selftest (不改变任何显示设置) ----
            if (args.Length >= 1 && args[0] == "--selftest")
            {
                AppConfig.Load();
                int code = SelfTest.Run();
                VddController.Stop();
                Shutdown(code);
                return;
            }

            // ---- 修复当前远程虚拟屏的桌面尺寸, 不启动托盘/守护 ----
            if (args.Length >= 1 && args[0] == "--fix-display")
            {
                Logger.Init();
                AppConfig.Load();
                var layout = RemoteDisplayService.EnsureLayout();
                Logger.Info("远程桌面修复结果: " + layout.Message);
                VddController.Stop();
                Shutdown(layout.MeetsMinimum ? 0 : 1);
                return;
            }

            // ---- 单实例 ----
            bool createdNew;
            SingleMutex = new Mutex(true, "Local\\QuietScreen.SingleInstance", out createdNew);
            ShowMainSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\QuietScreen.ShowMain");
            if (!createdNew)
            {
                ShowMainSignal.Set();   // 唤醒已有实例显示主窗口
                Shutdown(0);
                return;
            }

            Logger.Init();
            Logger.Info("=== QuietScreen 启动 ===");

            // ---- 全局异常钩子 (审查 P1): 任何未处理异常都写入日志, 绝不静默死亡 ----
            DispatcherUnhandledException += (s, e) =>
            {
                Logger.Error("UI 线程未处理异常: " + e.Exception);
                e.Handled = true;   // 吞掉保持存活, 状态由守护下一轮巡检自愈
            };
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                Logger.Error("未处理异常 (是否终止=" + e.IsTerminating + "): " + e.ExceptionObject);
            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                Logger.Error("未观察任务异常: " + e.Exception);
                e.SetObserved();
            };

            AppConfig.Load();

            // 资源占用: 降低线程池常驻线程数 (按需扩容)
            System.Threading.ThreadPool.SetMinThreads(4, 4);

            // 夜间常驻: 阻止系统自动睡眠 (随程序运行, 退出即恢复系统默认)
            PowerHelper.SetKeepAwake(AppConfig.Current.PreventSleepEnabled);

            // 定时关机 (按配置武装每日定时)
            ShutdownService.Initialize();


            // 启动稳定后修剪一次工作集 (托盘常驻应用保持低内存)
            var _ = System.Threading.Tasks.Task.Run(async () =>
            {
                await System.Threading.Tasks.Task.Delay(15000);
                MemoryTrimmer.Trim();
            });

            // 驱动控制器: 心跳保活 + 睡眠唤醒保护, 随应用常驻
            VddService.EnsureStarted();

            // 托盘
            TrayService.Instance.Init();
            TrayService.Instance.ShowMainRequested += ShowMainWindow;

            // 监听第二个实例的唤醒信号
            var listener = new Thread(() =>
            {
                while (true)
                {
                    try { ShowMainSignal.WaitOne(); Dispatcher.Invoke(ShowMainWindow); }
                    catch { break; }
                }
            }) { IsBackground = true };
            listener.Start();

            // 守护服务
            GuardianService.Instance.Initialize();
            // 启动模式按"内屏是否活跃"恢复 (虚拟主屏标记会跨会话残留, 不能作为远控依据):
            //   全部活跃显示器都是虚拟屏 = 真息屏远控/无头状态 (内屏已断电) → 恢复夜间模式,
            //   守护接管, 不删屏、不切拓扑, 远端会话无缝续命;
            //   内屏活跃 (人在本机, 上次夜间的虚拟主屏标记残留) → 本机启动,
            //   绝不切 ExternalOnly (否则内屏熄灭 = 人在本机却黑屏)。
            var activeDisplays = DisplayService.GetDisplays();
            bool headlessRemote = activeDisplays.Count > 0 && activeDisplays.All(d => d.IsVirtual);
            if (headlessRemote)
                GuardianService.Instance.SetNight("启动时全部显示器均为虚拟屏, 恢复息屏远控");
            else
            {
                GuardianService.Instance.Start();
                GuardianService.Instance.SetLocal("程序启动", switchDisplay: false);
            }
            if (args.Any(a => a == "--night"))
                GuardianService.Instance.SetNight("命令行 --night");

            bool startMinimized = args.Any(a => a == "--minimized");
            var win = new MainWindow(startMinimized);

            // 全局热键: Ctrl+Esc 点亮内屏 / Ctrl+Alt+Esc 切回息屏
            HotkeyService.Register(win);

            if (startMinimized)
                win.Hide();     // 注意: 不能先 Show 再 Hide, 否则状态定时器照常工作
            else
                win.Show();
        }

        private void ShowMainWindow()
        {
            if (MainWindow != null)
            {
                MainWindow.Show();
                MainWindow.WindowState = WindowState.Normal;
                MainWindow.Activate();
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Logger.Info($"程序退出 (ExitCode={e.ApplicationExitCode})");
            try { GuardianService.Instance.Stop(); } catch { }
            try { HotkeyService.Unregister(); } catch { }
            try { PowerHelper.SetKeepAwake(false); } catch { }
            try { VddController.Stop(); } catch { }
            try { TrayService.Instance.Dispose(); } catch { }
            try { SingleMutex?.ReleaseMutex(); } catch { }
            base.OnExit(e);
        }
    }
}
