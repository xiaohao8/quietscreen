using System;
using System.IO;
using System.Windows;
using WinForms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace QuietScreen.Core
{
    /// <summary>托盘图标与右键菜单</summary>
    public sealed class TrayService
    {
        public static TrayService Instance { get; } = new TrayService();

        private WinForms.NotifyIcon _icon;
        private WinForms.ContextMenuStrip _menu;
        private WinForms.ToolStripMenuItem _toggleGuardItem;

        public event Action ShowMainRequested;

        public void Init()
        {
            if (_icon != null) return;

            _icon = new WinForms.NotifyIcon
            {
                Text = "QuietScreen 虚拟屏管家",
                Visible = true,
                Icon = LoadIcon(),
            };
            _icon.DoubleClick += (s, e) => ShowMainRequested?.Invoke();

            _menu = new WinForms.ContextMenuStrip();

            var openItem = new WinForms.ToolStripMenuItem("打开主窗口");
            openItem.Font = new Drawing.Font(openItem.Font, Drawing.FontStyle.Bold);
            openItem.Click += (s, e) => ShowMainRequested?.Invoke();
            _menu.Items.Add(openItem);
            _menu.Items.Add(new WinForms.ToolStripSeparator());

            var oneClick = new WinForms.ToolStripMenuItem("一键息屏远控");
            oneClick.Click += (s, e) => RunOnUi(() =>
            {
                GuardianService.Instance.Start();
                GuardianService.Instance.CheckNow();
                _ = System.Threading.Tasks.Task.Run(() => Logger.Info("键盘灯: " + KeyboardLightService.TrySoftwareLightsOff().Message));
            });
            _menu.Items.Add(oneClick);

            var restore = new WinForms.ToolStripMenuItem("恢复内屏 (守护暂停 2 小时)");
            restore.Click += (s, e) => RunOnUi(() =>
            {
                GuardianService.Instance.SetLocal("托盘恢复内屏");
            });
            _menu.Items.Add(restore);

            var checkNow = new WinForms.ToolStripMenuItem("立即检查一次");
            checkNow.Click += (s, e) => RunOnUi(() => GuardianService.Instance.CheckNow());
            _menu.Items.Add(checkNow);

            var toggleGuard = new WinForms.ToolStripMenuItem("守护运行中");
            toggleGuard.CheckOnClick = true;
            toggleGuard.Checked = GuardianService.Instance.IsRunning;
            toggleGuard.Click += (s, e) => RunOnUi(() =>
            {
                AppConfig.Current.GuardianEnabled = toggleGuard.Checked;
                AppConfig.Save();
                if (toggleGuard.Checked) GuardianService.Instance.Start();
                else GuardianService.Instance.Stop();
            });
            _menu.Items.Add(toggleGuard);
            _toggleGuardItem = toggleGuard;

            // 每次打开菜单时同步守护状态, 避免 UI 与托盘不同步
            _menu.Opening += (s, e) =>
            {
                if (_toggleGuardItem != null)
                    _toggleGuardItem.Checked = GuardianService.Instance.IsRunning;
            };

            var restartAgent = new WinForms.ToolStripMenuItem("重启远控代理 (解除隐私屏/卡死)");
            restartAgent.Click += (s, e) => RunOnUi(() =>
            {
                int killed = 0;
                foreach (var pr in System.Diagnostics.Process.GetProcessesByName("AweSun"))
                {
                    try { pr.Kill(); killed++; } catch { }
                }
                Logger.Warn($"手动重启远控代理 (结束 {killed} 个进程), 守护将在下一轮自动拉起");
                ToastService.Show("QuietScreen", $"已结束 {killed} 个向日葵进程\n守护将在 1 分钟内自动拉起新代理\n请重新连接远程");
            });
            _menu.Items.Add(restartAgent);

            _menu.Items.Add(new WinForms.ToolStripSeparator());
            var exit = new WinForms.ToolStripMenuItem("退出");
            exit.Click += (s, e) =>
            {
                Logger.Info("用户从托盘退出");
                _icon.Visible = false;
                Application.Current?.Dispatcher.Invoke(() => Application.Current.Shutdown());
            };
            _menu.Items.Add(exit);

            _icon.ContextMenuStrip = _menu;
        }

        private static void RunOnUi(Action action)
        {
            var app = Application.Current;
            if (app == null) { action(); return; }
            app.Dispatcher.BeginInvoke(new Action(() =>
            {
                try { action(); } catch (Exception ex) { Logger.Error("托盘操作失败: " + ex.Message); }
            }));
        }

        private static Drawing.Icon LoadIcon()
        {
            try
            {
                var sri = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"));
                if (sri != null) return new Drawing.Icon(sri.Stream);
            }
            catch { }
            return Drawing.SystemIcons.Application;
        }

        public void Dispose()
        {
            try
            {
                if (_icon != null) { _icon.Visible = false; _icon.Dispose(); _icon = null; }
            }
            catch { }
        }
    }
}
