using System;
using System.Windows;
using System.Windows.Controls;
using QuietScreen.Core;

namespace QuietScreen.Views
{
    public partial class GuardianView : UserControl
    {
        private bool _loading = true;

        public GuardianView()
        {
            InitializeComponent();
            Loaded += (s, e) =>
            {
                LoadFromConfig();
                RefreshStatus();
                GuardianService.Instance.SnapshotChanged += OnSnapshotChanged;
            };
            Unloaded += (s, e) => GuardianService.Instance.SnapshotChanged -= OnSnapshotChanged;
        }

        private void OnSnapshotChanged()
        {
            Dispatcher.BeginInvoke(new Action(RefreshStatus));
        }

        public void RefreshStatus()
        {
            var snap = GuardianService.Instance.GetSnapshot();
            StatPrimary.Text = snap.PrimaryText;
            StatAgent.Text = snap.AgentText;
            StatChecks.Text = $"{snap.CheckCount} 次   (最后: {(snap.LastCheckTime == default ? "—" : snap.LastCheckTime.ToString("HH:mm:ss"))})";
            StatRepairs.Text = snap.RepairCount + " 次";
        }


        private void LoadFromConfig()
        {
            _loading = true;
            var c = AppConfig.Current;
            GuardSwitch.IsChecked = GuardianService.Instance.IsRunning;
            OptAutoCreate.IsChecked = c.AutoCreateIfMissing;
            OptSwitchExternal.IsChecked = c.SwitchExternal;
            OptClean.IsChecked = c.CleanExtraDisplays;
            OptToast.IsChecked = c.ToastOnChange;
            IntervalBox.Text = c.CheckIntervalSec.ToString();
            AgentSwitch.IsChecked = c.AgentKeepaliveEnabled;
            AgentExeBox.Text = c.AgentExe;
            AgentArgsBox.Text = c.AgentArgs;
            AgentPortBox.Text = c.AgentPort.ToString();
            _loading = false;
        }

        private void Save(Action<AppConfig> apply)
        {
            if (_loading) return;
            apply(AppConfig.Current);
            AppConfig.Save();
        }

        private void GuardSwitch_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            bool on = GuardSwitch.IsChecked == true;
            AppConfig.Current.GuardianEnabled = on;
            AppConfig.Save();
            if (on) GuardianService.Instance.Start();
            else GuardianService.Instance.Stop();
            RefreshStatus();
        }

        private void Opt_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            var c = AppConfig.Current;
            c.AutoCreateIfMissing = OptAutoCreate.IsChecked == true;
            c.SwitchExternal = OptSwitchExternal.IsChecked == true;
            c.CleanExtraDisplays = OptClean.IsChecked == true;
            c.ToastOnChange = OptToast.IsChecked == true;
            AppConfig.Save();
        }

        private void Interval_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            if (int.TryParse(IntervalBox.Text.Trim(), out int v) && v >= 5 && v <= 3600)
                Save(c => c.CheckIntervalSec = v);
        }

        private void AgentSwitch_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            Save(c => c.AgentKeepaliveEnabled = AgentSwitch.IsChecked == true);
        }

        private void Agent_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            if (!int.TryParse(AgentPortBox.Text.Trim(), out int port)) port = 0;
            Save(c =>
            {
                c.AgentExe = AgentExeBox.Text.Trim();
                c.AgentArgs = AgentArgsBox.Text;
                c.AgentPort = port;
            });
        }

        private void BtnCheckNow_Click(object sender, RoutedEventArgs e)
        {
            BtnCheckNow.IsEnabled = false;
            GuardianService.Instance.CheckNow();
            RefreshStatus();
            BtnCheckNow.IsEnabled = true;
        }
    }
}
