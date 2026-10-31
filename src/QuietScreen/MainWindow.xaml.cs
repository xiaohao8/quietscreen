using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using QuietScreen.Core;
using QuietScreen.Views;

namespace QuietScreen
{
    public partial class MainWindow : Window
    {
        [DllImport("dwmapi.dll")]
        private static extern void DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        [DllImport("dwmapi.dll")]
        private static extern void DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref uint value, int size);
        private readonly HomeView _home = new HomeView();
        private readonly GuardianView _guardian = new GuardianView();
        private readonly ShutdownView _shutdown = new ShutdownView();
        private readonly DisplayModeView _display = new DisplayModeView();
        private readonly SettingsView _settings = new SettingsView();
        private readonly LogView _log = new LogView();
        private readonly DispatcherTimer _statusTimer;

        public MainWindow(bool startMinimized = false)
        {
            InitializeComponent();

            // Win11 圆角窗口 (Win10 上静默忽略)
            try
            {
                var helper = new System.Windows.Interop.WindowInteropHelper(this);
                helper.EnsureHandle();
                int pref = 2; // DWMWCP_ROUND
                DwmSetWindowAttribute(helper.Handle, 33, ref pref, 4);
                uint borderColor = 0xFF3A3A44;   // COLORREF: 与窗口边框同色系
                DwmSetWindowAttribute(helper.Handle, 34, ref borderColor, 4);
            }
            catch { }

            NavHome.Tag = _home; NavGuardian.Tag = _guardian; NavShutdown.Tag = _shutdown;
            NavDisplay.Tag = _display; NavSettings.Tag = _settings; NavLog.Tag = _log;
            Navigate(_home, "主页");

            GuardianService.Instance.SnapshotChanged += () =>
                Dispatcher.BeginInvoke(new Action(RefreshGuardStatus));

            // 兜底刷新降频到 30 秒 (事件驱动为主, 窗口可见时的定时刷新有渲染开销)
            _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            _statusTimer.Tick += (s, e) => RefreshGuardStatus();
            _statusTimer.Start();

            Activated += (s, e) => RefreshGuardStatus();

            RefreshGuardStatus();

            // 窗口显隐由 App.OnStartup 决定 (--minimized 时保持隐藏, 托盘可唤回)
            if (startMinimized)
                WindowState = WindowState.Minimized;
        }

        private void Nav_Click(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag is UserControl page)
            {
                string title = rb.Name switch
                {
                    "NavHome" => "主页",
                    "NavGuardian" => "守护",
                    "NavShutdown" => "定时关机",
                    "NavDisplay" => "显示模式",
                    "NavSettings" => "设置",
                    "NavLog" => "日志",
                    _ => "QuietScreen"
                };
                Navigate(page, title);

                if (page == _home) _home.Refresh();
                else if (page == _display) _display.Refresh();
                else if (page == _log) _log.Refresh();
            }
        }

        private void Navigate(UserControl page, string title)
        {
            PageHost.Content = page;
            PageTitle.Text = title;
        }

        private string _lastGuardText = "";

        public void RefreshGuardStatus()
        {
            // 缩在托盘时跳过刷新, 激活窗口时补一次
            if (!IsVisible) return;

            var snap = GuardianService.Instance.GetSnapshot();
            string text = $"{snap.Running}|{snap.Busy}|{snap.PrimaryText}";
            if (text == _lastGuardText) return;   // 内容没变不重绘, 减少渲染开销
            _lastGuardText = text;

            StatusDot.Fill = snap.Running ? FindGreen() : FindRed();
            GuardStateText.Text = snap.Running ? (snap.Busy ? "守护 · 工作中" : "守护运行中") : "守护已停止";
            GuardDetailText.Text = string.IsNullOrEmpty(snap.PrimaryText) ? snap.LastCheckText : snap.PrimaryText;
            _home?.OnSnapshot(snap);
        }

        private Brush FindGreen() => (Brush)FindResource("BrushGreen");
        private Brush FindRed() => (Brush)FindResource("BrushRed");

        private void BtnMin_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            // 守护运行时关闭 = 最小化到托盘, 提示一次
            if (GuardianService.Instance.IsRunning)
            {
                Hide();
                MemoryTrimmer.Trim();
                ToastService.Show("QuietScreen", "已最小化到托盘, 守护继续运行");
            }
            else
            {
                Close();
            }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            _statusTimer?.Stop();
            base.OnClosing(e);
        }
    }
}
