using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using QuietScreen.Core;

namespace QuietScreen.Views
{
    public partial class GuardianView : UserControl
    {
        private bool _loading = true;

        private bool _envChecked;

        public GuardianView()
        {
            InitializeComponent();
            Loaded += (s, e) =>
            {
                LoadFromConfig();
                RefreshStatus();
                GuardianService.Instance.SnapshotChanged += OnSnapshotChanged;
                // 首次进入守护页自动体检一次 (只读, 结果留在卡片里供随时查看)
                if (!_envChecked)
                {
                    _envChecked = true;
                    BtnEnvCheck_Click(this, new RoutedEventArgs());
                }
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
            // 界面标注"最小 10", 巡检循环也有 Math.Max(10, ...) 兜底 — 校验保持一致
            if (int.TryParse(IntervalBox.Text.Trim(), out int v) && v >= 10 && v <= 3600)
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

        private async void BtnEnvCheck_Click(object sender, RoutedEventArgs e)
        {
            BtnEnvCheck.IsEnabled = false;
            EnvResultText.Text = "体检中...";
            try
            {
                var items = await Task.Run(() => RemoteEnvCheck.Run());
                var sb = new System.Text.StringBuilder();
                int warnCount = 0;
                foreach (var it in items)
                {
                    sb.AppendLine(it.ToString());
                    if (!it.Pass) warnCount++;
                }
                EnvResultText.Text = sb.ToString().TrimEnd();
                Logger.Info("远控环境体检完成: " + warnCount + " 项待处理");
            }
            finally
            {
                BtnEnvCheck.IsEnabled = true;
            }
        }

        private async void BtnCheckNow_Click(object sender, RoutedEventArgs e)
        {
            BtnCheckNow.IsEnabled = false;
            try
            {
                // 检查完成后刷新状态 (原实现在检查完成前就刷新, 状态总是旧的)
                await GuardianService.Instance.CheckNowAsync();
                RefreshStatus();
            }
            finally
            {
                BtnCheckNow.IsEnabled = true;
            }
        }
    }
}
