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
            // 版本号随程序集自动更新 (不再手写死在 XAML 里)
            var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            if (v != null) AboutText.Text = $"QuietScreen 屏息 v{v.Major}.{v.Minor}.{v.Build} — 虚拟屏守护与息屏远控工具。";
            Loaded += (s, e) => { RefreshAll(); _ = RefreshAutoStartAsync(); };
        }

        public void Refresh() => RefreshAll();

        private void RefreshAll()
        {
            CfgPathText.Text = "配置文件: " + AppConfig.ConfigPath;
            PatternBox.Text = AppConfig.Current.VirtualDisplayPattern;
            PreventSleepSwitch.IsChecked = AppConfig.Current.PreventSleepEnabled;
            NightQuietSwitch.IsChecked = AppConfig.Current.NightAutoQuietEnabled;
            RefreshHotkeyText();
            _ = RefreshDriverAsync();
            _ = RefreshKeyboardInfoAsync();
            _ = RefreshPowerModeAsync();
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

        private void BtnOpenRgbSite_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo("https://openrgb.org") { UseShellExecute = true });
            }
            catch { }
        }

        // ---------------- 性能模式 (风扇降噪) ----------------

        private static string LegionNameOf(int m) => m switch
        {
            0 => "野兽/狂暴",
            1 => "均衡",
            2 => "安静",
            _ => "未知"
        };

        private static string OverlayNameOf(string g) => g switch
        {
            PowerModeService.OverlayBalanced => "平衡",
            _ => g == PowerModeService.ResolveOverlay("efficiency") ? "最佳能效"
               : g == PowerModeService.ResolveOverlay("performance") ? "最佳性能"
               : g
        };

        private async Task RefreshPowerModeAsync()
        {
            var svc = PowerModeService.Instance;
            await svc.ProbeLegionAsync();   // 只读探测 (Get, 不 Set)
            if (svc.LegionAvailable)
            {
                int cur = await svc.GetLegionModeAsync();
                LegionStatusText.Text = cur >= 0 ? "接口可用, 当前档位: " + LegionNameOf(cur) : "接口可用, 当前档位未知";
                SetLegionButtons(true);
            }
            else
            {
                LegionStatusText.Text = "不可用 — " + svc.LegionUnavailableReason;
                SetLegionButtons(false);
            }
            string overlay = await svc.GetActiveOverlayAsync();
            if (overlay == null)
            {
                OverlayStatusText.Text = "本机不支持电源模式滑条 (旧版 Windows)";
                SetOverlayButtons(false);
            }
            else
            {
                OverlayStatusText.Text = "当前: " + OverlayNameOf(overlay);
                SetOverlayButtons(true);
            }
        }

        private void SetLegionButtons(bool on)
        {
            BtnLegionBeast.IsEnabled = on;
            BtnLegionBalanced.IsEnabled = on;
            BtnLegionQuiet.IsEnabled = on;
        }

        private void SetOverlayButtons(bool on)
        {
            BtnOverlayEfficiency.IsEnabled = on;
            BtnOverlayBalanced.IsEnabled = on;
            BtnOverlayPerformance.IsEnabled = on;
        }

        private async void BtnLegionMode_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button b) || !(int.TryParse((string)b.Tag, out int mode))) return;
            SetLegionButtons(false);
            LegionStatusText.Text = "切换中...";
            var r = await PowerModeService.Instance.SetLegionModeAsync((LegionMode)mode);
            ToastService.Show("QuietScreen", "拯救者档位: " + r.msg);
            await RefreshPowerModeAsync();
        }

        private async void BtnOverlay_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button b) || string.IsNullOrEmpty((string)b.Tag)) return;
            SetOverlayButtons(false);
            OverlayStatusText.Text = "切换中...";
            string guid = PowerModeService.ResolveOverlay((string)b.Tag);   // 角色名 → 本机实际 GUID
            var r = await PowerModeService.Instance.SetOverlayAsync(guid);
            ToastService.Show("QuietScreen", "电源档: " + r.msg);
            await RefreshPowerModeAsync();
        }

        private void NightQuiet_Changed(object sender, RoutedEventArgs e)
        {
            if (NightQuietSwitch.IsChecked == null) return;
            AppConfig.Current.NightAutoQuietEnabled = NightQuietSwitch.IsChecked == true;
            AppConfig.Save();
            ToastService.Show("QuietScreen", "夜间自动降噪: " + (AppConfig.Current.NightAutoQuietEnabled ? "已开启" : "已关闭"));
        }
    }
}
