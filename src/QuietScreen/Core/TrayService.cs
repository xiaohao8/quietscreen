using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
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

            _ui = SynchronizationContext.Current ?? new SynchronizationContext();   // 启动流程在 UI 线程, 此处捕获

            _icon = new WinForms.NotifyIcon
            {
                Text = "QuietScreen 屏息",
                Visible = true,
                Icon = LoadIcon(),
            };
            _icon.DoubleClick += (s, e) => ShowMainRequested?.Invoke();
            // 托盘悬浮提示随模式实时更新 (NotifyIcon.Text 上限 63 字符)
            GuardianService.Instance.SnapshotChanged += UpdateTrayTip;

            _menu = new WinForms.ContextMenuStrip();

            var openItem = new WinForms.ToolStripMenuItem("打开主窗口");
            openItem.Font = new Drawing.Font(openItem.Font, Drawing.FontStyle.Bold);
            openItem.Click += (s, e) => ShowMainRequested?.Invoke();
            _menu.Items.Add(openItem);
            _menu.Items.Add(new WinForms.ToolStripSeparator());

            var oneClick = new WinForms.ToolStripMenuItem("一键息屏远控");
            oneClick.Click += (s, e) => RunOnUi(() =>
                GuardianService.Instance.SetNight("托盘一键息屏"));
            _menu.Items.Add(oneClick);

            var restore = new WinForms.ToolStripMenuItem("恢复内屏");
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
                // 结束全部向日葵进程属于破坏性操作, 先确认一次
                if (MessageBox.Show("将结束全部 AweSun (向日葵) 进程, 守护会在下一轮巡检自动拉起。\n\n确认执行?",
                                    "重启远控代理", MessageBoxButton.YesNo, MessageBoxImage.Question)
                    != MessageBoxResult.Yes) return;
                int killed = 0;
                foreach (var pr in System.Diagnostics.Process.GetProcessesByName("AweSun"))
                {
                    try { pr.Kill(); killed++; } catch { }
                }
                Logger.Warn($"手动重启远控代理 (结束 {killed} 个进程), 守护将在下一轮自动拉起");
                ToastService.Show("QuietScreen", $"已结束 {killed} 个向日葵进程\n守护将在 1 分钟内自动拉起新代理\n请重新连接远程");
            });
            _menu.Items.Add(restartAgent);

            // ---- 性能模式快捷切换 (后台直达, 不开主窗口) ----
            var perfRoot = new WinForms.ToolStripMenuItem("性能模式");
            var legionItems = new WinForms.ToolStripMenuItem[3];
            string[] legionNames = { "拯救者: 野兽/狂暴", "拯救者: 均衡", "拯救者: 安静" };
            for (int i = 0; i < 3; i++)
            {
                int mode = i;
                var item = new WinForms.ToolStripMenuItem(legionNames[i]);
                item.Click += (s, e) => RunOnUi(() => _ = SwitchLegionAsync(mode));
                perfRoot.DropDownItems.Add(item);
                legionItems[i] = item;
            }
            perfRoot.DropDownItems.Add(new WinForms.ToolStripSeparator());
            var overlayItems = new WinForms.ToolStripMenuItem[3];
            string[] overlayRoles = { "efficiency", "balanced", "performance" };
            string[] overlayNames = { "电源档: 最佳能效", "电源档: 平衡", "电源档: 最佳性能" };
            for (int i = 0; i < 3; i++)
            {
                string role = overlayRoles[i];
                var item = new WinForms.ToolStripMenuItem(overlayNames[i]);
                item.Click += (s, e) => RunOnUi(() => _ = SwitchOverlayAsync(role));
                perfRoot.DropDownItems.Add(item);
                overlayItems[i] = item;
            }
            perfRoot.DropDownOpening += (s, e) =>
            {
                var svc = PowerModeService.Instance;
                // 懒探测: 第一次打开菜单时才探测拯救者接口 (只读), 完成后下次打开显示对勾
                if (svc.LegionUnavailableReason == "未探测") _ = Task.Run(() => svc.ProbeLegionAsync());
                for (int i = 0; i < 3; i++)
                {
                    legionItems[i].Enabled = svc.LegionAvailable;
                    legionItems[i].Checked = svc.LegionAvailable && svc.LastKnownLegionMode == i;
                }
                for (int i = 0; i < 3; i++)
                    overlayItems[i].Checked = _lastOverlay != null && _lastOverlay == PowerModeService.ResolveOverlay(overlayRoles[i]);
                // 后台刷新当前电源档, 下次打开菜单显示对勾
                Task.Run(async () =>
                {
                    string cur = await svc.GetActiveOverlayAsync();
                    if (cur != null) { _lastOverlay = cur; _ui.Post(_ => { try { UpdateOverlayChecks(overlayItems, overlayRoles); } catch { } }, null); }
                });
            };
            _menu.Items.Add(perfRoot);

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
            // 关键通知兜底: 未打包运行时 WinRT Toast 不可用, 转托盘气泡
            // (NotifyIcon 非线程安全: 一律 Post 回 Init 时的 UI 线程执行)
            ToastService.Fallback = (title, body) =>
            {
                _ui.Post(_ =>
                {
                    try { if (_icon != null) _icon.ShowBalloonTip(5000, title, body, WinForms.ToolTipIcon.Info); } catch { }
                }, null);
            };
        }

        private SynchronizationContext _ui;   // Init 时捕获 (启动流程在 UI 线程)
        private string _lastTip = "";
        private string _lastOverlay;          // 最近一次刷新到的电源档 GUID (托盘对勾用)

        private void UpdateTrayTip()
        {
            if (_icon == null) return;
            _ui.Post(_ =>
            {
                try
                {
                    var snap = GuardianService.Instance.GetSnapshot();
                    string mode = snap.Mode == OperatingMode.Night ? "夜间远控中" : "本机使用中";
                    string text = $"QuietScreen 屏息 · {mode} · {snap.PrimaryText}";
                    if (text.Length > 63) text = text.Substring(0, 63);
                    if (text == _lastTip) return;   // 内容不变时不调用原生接口
                    _lastTip = text;
                    _icon.Text = text;
                }
                catch { }
            }, null);
        }

        // ---------------- 托盘性能模式快捷切换 ----------------

        private async Task SwitchLegionAsync(int mode)
        {
            var svc = PowerModeService.Instance;
            if (!svc.LegionAvailable) await svc.ProbeLegionAsync();
            var r = await svc.SetLegionModeAsync((LegionMode)mode);
            ToastService.Show("QuietScreen", "拯救者档位: " + r.msg);
        }

        private async Task SwitchOverlayAsync(string role)
        {
            string guid = PowerModeService.ResolveOverlay(role);
            var r = await PowerModeService.Instance.SetOverlayAsync(guid);
            if (r.ok) _lastOverlay = guid;
            ToastService.Show("QuietScreen", "电源档: " + r.msg);
        }

        private void UpdateOverlayChecks(WinForms.ToolStripMenuItem[] items, string[] roles)
        {
            for (int i = 0; i < items.Length; i++)
                items[i].Checked = _lastOverlay != null && _lastOverlay == PowerModeService.ResolveOverlay(roles[i]);
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
            try { GuardianService.Instance.SnapshotChanged -= UpdateTrayTip; } catch { }
            try
            {
                if (_icon != null) { _icon.Visible = false; _icon.Dispose(); _icon = null; }
            }
            catch { }
        }
    }
}
