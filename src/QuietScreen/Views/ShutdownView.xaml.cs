using System;
using System.Windows;
using System.Windows.Controls;
using QuietScreen.Core;

namespace QuietScreen.Views
{
    public partial class ShutdownView : UserControl
    {
        private bool _loading = true;

        public ShutdownView()
        {
            InitializeComponent();
            Loaded += (s, e) => LoadFromConfig();
        }

        public void Refresh() => RefreshStatus();

        private static readonly string[] TimeOptions =
        {
            "21:00", "21:30", "22:00", "22:30", "23:00", "23:30",
            "00:00", "00:30", "01:00", "01:30", "02:00", "02:30",
            "03:00", "03:30", "04:00", "04:30", "05:00", "05:30",
            "06:00", "07:00", "12:00"
        };
        private static readonly int[] GraceOptions = { 30, 60, 120, 180, 300, 600 };

        private void LoadFromConfig()
        {
            _loading = true;
            var c = AppConfig.Current;

            var times = new System.Collections.Generic.List<string>(TimeOptions);
            if (!times.Contains(c.ShutdownDailyTime)) times.Insert(0, c.ShutdownDailyTime);
            TimeBox.ItemsSource = times;
            TimeBox.SelectedItem = c.ShutdownDailyTime;

            var graces = new System.Collections.Generic.List<int>(GraceOptions);
            if (!graces.Contains(c.ShutdownGraceSec)) graces.Insert(0, c.ShutdownGraceSec);
            GraceBox.ItemsSource = graces;
            GraceBox.SelectedItem = c.ShutdownGraceSec;

            DailySwitch.IsChecked = ShutdownService.DailyEnabled;
            _loading = false;
            RefreshStatus();
        }

        private void RefreshStatus()
        {
            var parts = new System.Collections.Generic.List<string>();
            if (ShutdownService.PendingUntil is DateTime pend)
                parts.Add($"系统倒计时中: {pend:HH:mm:ss} 关机");
            if (ShutdownService.DailyEnabled && ShutdownService.NextDailyTime is DateTime next)
                parts.Add($"每日关机: {next:MM-dd HH:mm}");
            StatusText.Text = parts.Count > 0 ? string.Join("  ·  ", parts) : "当前没有已安排的关机";
        }

        private void Daily_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            AppConfig.Current.ShutdownDailyEnabled = DailySwitch.IsChecked == true;
            AppConfig.Save();
            if (DailySwitch.IsChecked == true) ShutdownService.EnableDaily();
            else ShutdownService.DisableDaily();
            RefreshStatus();
        }

        private void Time_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || TimeBox.SelectedItem is not string t) return;
            AppConfig.Current.ShutdownDailyTime = t;
            AppConfig.Save();
            ShutdownService.RearmIfEnabled();
            RefreshStatus();
        }

        private void Grace_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || GraceBox.SelectedItem is not int g) return;
            AppConfig.Current.ShutdownGraceSec = g;
            AppConfig.Save();
        }

        private void Quick_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && int.TryParse(b.Tag?.ToString(), out int sec))
            {
                if (ShutdownService.ScheduleIn(sec, out var msg))
                    ToastService.Show("QuietScreen", msg + "\n按 Ctrl+Alt+Esc 后可在此页取消");
                RefreshStatus();
            }
        }

        private void BtnAbort_Click(object sender, RoutedEventArgs e)
        {
            ShutdownService.Abort(out var msg);
            ToastService.Show("QuietScreen", msg);
            RefreshStatus();
        }
    }
}
