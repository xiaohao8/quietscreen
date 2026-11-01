using System;
using System.Linq;
using System.Threading;
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
            // 启动即进入本机模式: 开机必亮内屏 (夜间只在你明确按键时进入)
            GuardianService.Instance.Start();
            GuardianService.Instance.SetLocal("程序启动", switchDisplay: true);

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
