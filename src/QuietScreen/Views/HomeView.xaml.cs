using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using QuietScreen.Core;
using QuietScreen.Core.Vdd;

namespace QuietScreen.Views
{
    public partial class HomeView : UserControl
    {
        private bool _busy;

        public HomeView()
        {
            InitializeComponent();
            Loaded += (s, e) => Refresh();
        }

        public void Refresh()
        {
            RefreshDisplays();
            _ = RefreshDriverAsync();
        }

        public void OnSnapshot(GuardianSnapshot snap)
        {
            ModeText.Text = snap.Mode == Core.OperatingMode.Night
                ? "当前模式: 夜间远控 (虚拟屏主屏 · 内屏熄灭)"
                : "当前模式: 本机使用 (内屏点亮)";
            if (_busy || snap.CheckCount == 0) return;
            string agent = string.IsNullOrEmpty(snap.AgentText) ? "" : $" · 代理{snap.AgentText}";
            ActionHint.Text = $"第 {snap.CheckCount} 次检查 ({snap.LastCheckTime:HH:mm:ss}): {snap.LastCheckText}{agent}";
        }

        private void RefreshDisplays()
        {
            DisplayList.ItemsSource = DisplayService.GetDisplays();
        }

        private async Task RefreshDriverAsync()
        {
            try
            {
                Version version = new Version();
                var status = await Task.Run(() => VddController.QueryStatusFresh(out version));
                bool missing = status == VddDriver.DriverStatus.NOT_INSTALLED;
                DriverText.Text = status == VddDriver.DriverStatus.OK ? "驱动正常" : (missing ? "未安装驱动" : "驱动异常");
                DriverSubText.Text = status == VddDriver.DriverStatus.OK
                    ? $"v{version}  ·  虚拟屏 {VddDisplay.GetParsecDisplays().Count} 个"
                    : VddService.StatusToText(status);
                DriverDot.Fill = status == VddDriver.DriverStatus.OK
                    ? (Brush)FindResource("BrushGreen")
                    : (Brush)FindResource("BrushRed");
                InstallPanel.Visibility = missing ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                DriverText.Text = "驱动检测失败";
                DriverSubText.Text = ex.Message;
            }
        }

        private async void BtnInstallDrv_Click(object sender, RoutedEventArgs e)
        {
            if (_busy) return;
            if (MessageBox.Show("将安装内置的 Parsec 官方虚拟屏驱动包 v0.45 (驱动厂商原版, 与开源项目 parsec-vdd 安装流程一致)。\n会弹出管理员授权窗口, 静默安装约 10-20 秒。\n\n继续安装?",
                                "安装虚拟屏驱动", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
            _busy = true;
            SetButtons(false);
            DriverText.Text = "正在安装驱动...";
            var r = await DriverInstallerService.InstallAsync();
            ToastService.Show("QuietScreen", r.Success ? r.Message : "安装未完成: " + r.Message);
            _busy = false;
            SetButtons(true);
            await RefreshDriverAsync();
        }

        private async void BtnCheckDriver_Click(object sender, RoutedEventArgs e)
        {
            DriverText.Text = "检测中...";
            await RefreshDriverAsync();
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e) => RefreshDisplays();

        private async void BtnOneClick_Click(object sender, RoutedEventArgs e)
        {
            if (_busy) return;
            _busy = true;
            SetButtons(false);
            ActionHint.Text = "正在创建虚拟屏并切换到仅第二屏 (内屏会熄灭)...";
            try
            {
                var cfg = AppConfig.Current;
                GuardianService.Instance.SetNight("主页一键息屏");   // 模式切换 (创建/复用/切换/键盘灯全含)
                await Task.Delay(300);
                Refresh();
                ActionHint.Text = "已进入夜间远控模式。回到本机点\"恢复内屏\"或按住 Ctrl+Esc 1 秒。";
            }
            catch (Exception ex)
            {
                ActionHint.Text = "执行失败: " + ex.Message;
            }
            finally
            {
                _busy = false;
                SetButtons(true);
            }
        }

        private void BtnRestore_Click(object sender, RoutedEventArgs e)
        {
            GuardianService.Instance.SetLocal("主页恢复内屏");
            ActionHint.Text = "已点亮内屏, 守护暂停 2 小时 (不会被自动切回息屏)。离开时按 Ctrl+Alt+Esc 恢复。";
        }

        private async void BtnRemoveVdd_Click(object sender, RoutedEventArgs e)
        {
            if (_busy) return;
            _busy = true;
            SetButtons(false);
            ActionHint.Text = "正在移除虚拟屏...";
            try
            {
                var r = await Task.Run(() => VddService.RemoveAllVirtualDisplays());
                ActionHint.Text = r.Message;
            }
            finally
            {
                _busy = false;
                SetButtons(true);
                Refresh();
            }
        }

        private void SetButtons(bool enabled)
        {
            BtnOneClick.IsEnabled = enabled;
            BtnRestore.IsEnabled = enabled;
            BtnRemoveVdd.IsEnabled = enabled;
            BtnCheckDriver.IsEnabled = enabled;
            BtnRefresh.IsEnabled = enabled;
            BtnInstallDrv.IsEnabled = enabled;
        }
    }
}
