using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Threading.Tasks;
using QuietScreen.Core;

namespace QuietScreen.Views
{
    public partial class SettingsView : UserControl
    {
        public SettingsView()
        {
            InitializeComponent();
            Loaded += (s, e) => { RefreshAll(); _ = RefreshAutoStartAsync(); };
        }

        public void Refresh() => RefreshAll();

        private void RefreshAll()
        {
            CfgPathText.Text = "配置文件: " + AppConfig.ConfigPath;
            PatternBox.Text = AppConfig.Current.VirtualDisplayPattern;
            PreventSleepSwitch.IsChecked = AppConfig.Current.PreventSleepEnabled;
            RefreshHotkeyText();
            _ = RefreshDriverAsync();
            _ = RefreshKeyboardInfoAsync();
        }

        private async Task RefreshKeyboardInfoAsync()
        {
            LampCapText.Text = "USB 键盘: " + await Task.Run(() => KeyboardLightService.GetKeyboardIdentity());
        }

        private void BtnLightsOff_Click(object sender, RoutedEventArgs e)
        {
            BtnLightsOff.IsEnabled = false;
            Task.Run(() => KeyboardLightService.TrySoftwareLightsOff())
                .ContinueWith(t =>
                {
                    var r = t.Result;
                    Logger.Info("手动熄灯: " + r.Message);
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        BtnLightsOff.IsEnabled = true;
                        ToastService.Show("QuietScreen", r.Message);
                    }));
                });
        }

        private void RefreshHotkeyText()
        {
            if (!AppConfig.Current.HotkeyEnabled)
            {
                HotkeyText.Text = "已在配置中关闭 (HotkeyEnabled=false)";
                return;
            }
            HotkeyText.Text = "Ctrl+Esc = 点亮笔记本内屏 (仅电脑屏幕)\n" +
                              "Ctrl+Alt+Esc = 切回息屏远控 (仅第二屏幕)\n" +
                              $"注册状态: {(Core.HotkeyService.InternalRegistered || Core.HotkeyService.HookInstalled ? "Ctrl+Esc ✓" + (Core.HotkeyService.HookInstalled ? "(键盘钩子)" : "") : "Ctrl+Esc ✗被占用")}, " +
                              $"{(Core.HotkeyService.ExternalRegistered ? "Ctrl+Alt+Esc ✓" : "Ctrl+Alt+Esc ✗被占用")}" +
                              "\n注意: 注册后 Ctrl+Esc 不再打开开始菜单, 开始菜单用 Win 键";
        }

        private void PreventSleep_Changed(object sender, RoutedEventArgs e)
        {
            if (PreventSleepSwitch.IsChecked == null) return;
            AppConfig.Current.PreventSleepEnabled = PreventSleepSwitch.IsChecked == true;
            AppConfig.Save();
            PowerHelper.SetKeepAwake(AppConfig.Current.PreventSleepEnabled);
        }

        private async Task RefreshDriverAsync()
        {
            try
            {
                var r = await Task.Run(() => VddService.CheckDriver());
                DrvText.Text = r.Success ? "虚拟屏驱动正常" : "虚拟屏驱动异常";
                DrvSub.Text = r.Message + "  ·  控制逻辑内置 (基于开源 parsec-vdd)";
                DrvDot.Fill = r.Success ? (Brush)FindResource("BrushGreen") : (Brush)FindResource("BrushRed");
                DrvHelpPanel.Visibility = r.Success ? Visibility.Collapsed : Visibility.Visible;
            }
            catch (Exception ex)
            {
                DrvText.Text = "驱动检测失败";
                DrvSub.Text = ex.Message;
                DrvHelpPanel.Visibility = Visibility.Visible;
            }
        }

        private async Task RefreshAutoStartAsync()
        {
            AutoStartText.Text = await StartupService.GetStatusTextAsync();
        }

        private void BtnDrvRefresh_Click(object sender, RoutedEventArgs e)
        {
            DrvText.Text = "检测中...";
            _ = RefreshDriverAsync();
        }

        private void BtnDrvFolder_Click(object sender, RoutedEventArgs e)
        {
            // 常见驱动安装包位置
            string[] candidates =
            {
                @"D:\ParsecVDisplay\driver",
                @"D:\ParsecVDisplay",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ParsecVDisplay"),
            };
            foreach (var dir in candidates)
            {
                if (Directory.Exists(dir))
                {
                    Process.Start("explorer.exe", dir);
                    return;
                }
            }
            ToastService.Show("QuietScreen", "没有找到驱动安装包, 请到项目主页重新下载");
        }

        private void BtnDrvHome_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://github.com/nomi-san/parsec-vdd/releases",
                    UseShellExecute = true
                });
            }
            catch { }
        }

        private void PatternBox_LostFocus(object sender, RoutedEventArgs e)
        {
            string p = PatternBox.Text.Trim();
            if (string.IsNullOrEmpty(p)) return;
            try
            {
                _ = new System.Text.RegularExpressions.Regex(p);
                AppConfig.Current.VirtualDisplayPattern = p;
                AppConfig.Save();
            }
            catch
            {
                ToastService.Show("QuietScreen", "正则表达式无效, 已还原");
                PatternBox.Text = AppConfig.Current.VirtualDisplayPattern;
            }
        }

        private async void BtnAutoOn_Click(object sender, RoutedEventArgs e)
        {
            var r = await StartupService.EnableAsync();
            ToastService.Show("QuietScreen", "开机自启: " + r.Item2);
            await RefreshAutoStartAsync();
        }

        private async void BtnAutoOff_Click(object sender, RoutedEventArgs e)
        {
            var r = await StartupService.DisableAsync();
            ToastService.Show("QuietScreen", "开机自启: " + r.Item2);
            await RefreshAutoStartAsync();
        }

        private void BtnCfgFolder_Click(object sender, RoutedEventArgs e)
        {
            Process.Start("explorer.exe", AppConfig.ConfigDir);
        }

        private void BtnLog_Click(object sender, RoutedEventArgs e)
        {
            if (File.Exists(Logger.LogFile))
                Process.Start(new ProcessStartInfo(Logger.LogFile) { UseShellExecute = true });
            else
                ToastService.Show("QuietScreen", "日志文件还没有生成");
        }

        private void BtnOpenRgbOff_Click(object sender, RoutedEventArgs e)
        {
            BtnOpenRgbOff.IsEnabled = false;
            Task.Run(() => KeyboardLightService.TrySoftwareLightsOff())
                .ContinueWith(t =>
                {
                    var r = t.Result;
                    ToastService.Show("QuietScreen", r.Message);
                    Dispatcher.BeginInvoke(new Action(() => BtnOpenRgbOff.IsEnabled = true));
                });
        }

        private void BtnOpenRgbSite_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo("https://openrgb.org") { UseShellExecute = true });
            }
            catch { }
        }
    }
}
