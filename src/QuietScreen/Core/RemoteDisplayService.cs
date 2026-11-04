using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Automation;

namespace QuietScreen.Core
{
    public sealed class RemoteDisplayLayout
    {
        public bool Success;
        public bool Changed;
        public string Message = "";
        public string DeviceName = "";
        public int PhysicalWidth;
        public int PhysicalHeight;
        public int RefreshRate;
        public int ScalePercent;
        public int LogicalWidth;
        public int LogicalHeight;

        public bool MeetsMinimum =>
            LogicalWidth >= RemoteDisplayService.MinimumLogicalWidth &&
            LogicalHeight >= RemoteDisplayService.MinimumLogicalHeight;

        public override string ToString()
        {
            if (PhysicalWidth <= 0 || PhysicalHeight <= 0) return Message;
            return $"{DeviceName.Replace(@"\\.\", "")} {PhysicalWidth}x{PhysicalHeight}@{RefreshRate}Hz, " +
                   $"缩放 {ScalePercent}%, 桌面 {LogicalWidth}x{LogicalHeight}";
        }
    }

    /// <summary>
    /// Keeps the remote virtual display at a usable desktop size.
    /// Windows stores a per-monitor scale factor, so changing only the physical
    /// resolution can still leave the logical desktop at 1280x800 or smaller.
    /// </summary>
    public static class RemoteDisplayService
    {
        public const int MinimumLogicalWidth = 1600;
        public const int MinimumLogicalHeight = 900;

        private const int PreferredWidth = 2560;
        private const int PreferredHeight = 1600;
        private const int PreferredRefreshRate = 60;
        private static readonly int[] SupportedScales = { 100, 125, 150, 175, 200, 225, 250, 300 };

        private static readonly object SyncRoot = new object();

        public static RemoteDisplayLayout GetLayout()
        {
            var result = new RemoteDisplayLayout();
            var primary = DisplayService.GetPrimary();
            if (primary == null || !primary.IsVirtual)
            {
                result.Message = "当前主屏不是虚拟屏";
                return result;
            }

            if (!TryGetCurrentMode(primary.DeviceName, out var mode))
            {
                result.Message = "无法读取虚拟屏当前模式";
                result.DeviceName = primary.DeviceName;
                return result;
            }

            result.Success = true;
            result.DeviceName = primary.DeviceName;
            result.PhysicalWidth = mode.Width;
            result.PhysicalHeight = mode.Height;
            result.RefreshRate = mode.RefreshRate;
            result.ScalePercent = GetPrimaryScalePercent();
            FillLogicalSize(result);
            result.Message = result.ToString();
            return result;
        }

        public static RemoteDisplayLayout EnsureLayout(bool allowScaleChange = true)
        {
            lock (SyncRoot)
            {
                var result = GetLayout();
                if (!result.Success) return result;

                var primary = DisplayService.GetPrimary();
                if (primary == null || !primary.IsVirtual)
                {
                    result.Success = false;
                    result.Message = "当前主屏不是虚拟屏";
                    return result;
                }

                int targetScale = ChooseTargetScale(result.PhysicalWidth, result.PhysicalHeight);
                bool sizeTooSmall = !result.MeetsMinimum;
                bool scaleTooLarge = result.ScalePercent > targetScale;

                if (sizeTooSmall || scaleTooLarge ||
                    result.PhysicalWidth != PreferredWidth ||
                    result.PhysicalHeight != PreferredHeight)
                {
                    int refresh = ChooseRefreshRate(primary.DeviceName, PreferredWidth, PreferredHeight);
                    if (refresh > 0 &&
                        (result.PhysicalWidth != PreferredWidth ||
                         result.PhysicalHeight != PreferredHeight ||
                         result.RefreshRate != refresh))
                    {
                        if (TrySetMode(primary.DeviceName, PreferredWidth, PreferredHeight, refresh, out string modeMessage))
                        {
                            result.Changed = true;
                            Logger.Ok("远程虚拟屏模式已调整: " + modeMessage);
                            result.PhysicalWidth = PreferredWidth;
                            result.PhysicalHeight = PreferredHeight;
                            result.RefreshRate = refresh;
                            Thread.Sleep(800);
                            result.ScalePercent = GetPrimaryScalePercent();
                            FillLogicalSize(result);
                        }
                        else
                        {
                            Logger.Warn("远程虚拟屏模式调整失败: " + modeMessage);
                        }
                    }
                }

                targetScale = ChooseTargetScale(result.PhysicalWidth, result.PhysicalHeight);
                if (allowScaleChange && result.ScalePercent > targetScale)
                {
                    Logger.Warn($"远程虚拟屏缩放偏大 ({result.ScalePercent}%), 正在调整为 {targetScale}%");
                    if (TrySetPrimaryScale(targetScale))
                    {
                        Thread.Sleep(1200);
                        result.ScalePercent = GetPrimaryScalePercent();
                        FillLogicalSize(result);
                        if (result.ScalePercent <= targetScale)
                        {
                            result.Changed = true;
                            Logger.Ok($"远程虚拟屏缩放已调整: {targetScale}%");
                        }
                        else
                        {
                            Logger.Warn($"缩放设置返回成功, 但当前仍为 {result.ScalePercent}%");
                        }
                    }
                    else
                    {
                        Logger.Warn("无法通过 Windows 显示设置调整远程虚拟屏缩放");
                    }
                }

                result.Success = result.PhysicalWidth > 0 && result.PhysicalHeight > 0;
                result.Message = result.ToString();
                if (result.MeetsMinimum)
                    Logger.Info("远程桌面可用尺寸: " + result.Message);
                else
                    Logger.Warn("远程桌面尺寸仍偏小: " + result.Message);
                return result;
            }
        }

        private static void FillLogicalSize(RemoteDisplayLayout layout)
        {
            int scale = Math.Max(100, layout.ScalePercent);
            layout.LogicalWidth = (int)Math.Round(layout.PhysicalWidth * 100.0 / scale);
            layout.LogicalHeight = (int)Math.Round(layout.PhysicalHeight * 100.0 / scale);
        }

        private static int ChooseTargetScale(int width, int height)
        {
            // Pick the largest Windows scale that still leaves enough logical
            // desktop area. For 2560x1600 this is 150%, not 100%.
            for (int i = SupportedScales.Length - 1; i >= 0; i--)
            {
                int scale = SupportedScales[i];
                int logicalWidth = (int)Math.Round(width * 100.0 / scale);
                int logicalHeight = (int)Math.Round(height * 100.0 / scale);
                if (logicalWidth >= MinimumLogicalWidth && logicalHeight >= MinimumLogicalHeight)
                    return scale;
            }
            return SupportedScales[0];
        }

        private static int ChooseRefreshRate(string deviceName, int width, int height)
        {
            var modes = EnumerateModes(deviceName)
                .Where(m => m.Width == width && m.Height == height)
                .ToList();
            if (modes.Count == 0) return 0;

            var exact = modes.FirstOrDefault(m => m.RefreshRate == PreferredRefreshRate);
            if (exact.RefreshRate > 0) return exact.RefreshRate;
            return modes.OrderByDescending(m => m.RefreshRate).First().RefreshRate;
        }

        private static List<DisplayModeInfo> EnumerateModes(string deviceName)
        {
            var result = new List<DisplayModeInfo>();
            for (int i = 0; ; i++)
            {
                var mode = new Native.DEVMODE { dmSize = (short)Marshal.SizeOf<Native.DEVMODE>() };
                if (!Native.EnumDisplaySettings(deviceName, i, ref mode)) break;
                if (mode.dmPelsWidth <= 0 || mode.dmPelsHeight <= 0) continue;
                result.Add(new DisplayModeInfo
                {
                    Width = mode.dmPelsWidth,
                    Height = mode.dmPelsHeight,
                    RefreshRate = mode.dmDisplayFrequency
                });
            }
            return result;
        }

        private static bool TryGetCurrentMode(string deviceName, out DisplayModeInfo mode)
        {
            mode = null;
            var dm = new Native.DEVMODE { dmSize = (short)Marshal.SizeOf<Native.DEVMODE>() };
            if (!Native.EnumDisplaySettings(deviceName, Native.ENUM_CURRENT_SETTINGS, ref dm))
                return false;

            mode = new DisplayModeInfo
            {
                Width = dm.dmPelsWidth,
                Height = dm.dmPelsHeight,
                RefreshRate = dm.dmDisplayFrequency
            };
            return true;
        }

        private static bool TrySetMode(string deviceName, int width, int height, int refreshRate, out string message)
        {
            message = "";
            var dm = new Native.DEVMODE { dmSize = (short)Marshal.SizeOf<Native.DEVMODE>() };
            if (!Native.EnumDisplaySettings(deviceName, Native.ENUM_CURRENT_SETTINGS, ref dm))
            {
                message = "读取当前 DEVMODE 失败";
                return false;
            }

            dm.dmPelsWidth = width;
            dm.dmPelsHeight = height;
            dm.dmDisplayFrequency = refreshRate;
            dm.dmFields |= Native.DM_POSITION | Native.DM_BITSPERPEL |
                           Native.DM_PELSWIDTH | Native.DM_PELSHEIGHT |
                           Native.DM_DISPLAYFREQUENCY;

            int test = Native.ChangeDisplaySettingsEx(deviceName, ref dm, IntPtr.Zero, Native.CDS_TEST, IntPtr.Zero);
            if (test != Native.DISP_CHANGE_SUCCESSFUL)
            {
                message = $"模式不受支持 (code {test})";
                return false;
            }

            int apply = Native.ChangeDisplaySettingsEx(
                deviceName, ref dm, IntPtr.Zero, Native.CDS_UPDATEREGISTRY, IntPtr.Zero);
            if (apply != Native.DISP_CHANGE_SUCCESSFUL)
            {
                message = $"ChangeDisplaySettingsEx 返回 {apply}";
                return false;
            }

            Thread.Sleep(700);
            if (!TryGetCurrentMode(deviceName, out var current) ||
                current.Width != width || current.Height != height)
            {
                message = "系统未接受目标模式";
                return false;
            }

            message = $"{width}x{height}@{current.RefreshRate}Hz";
            return true;
        }

        private static int GetPrimaryScalePercent()
        {
            IntPtr oldContext = IntPtr.Zero;
            try
            {
                oldContext = Native.SetThreadDpiAwarenessContext(new IntPtr(Native.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2));
                var monitor = Native.MonitorFromPoint(new Native.POINT(), Native.MONITOR_DEFAULTTOPRIMARY);

                if (Native.GetDpiForMonitor(monitor, 0, out uint dpiX, out _) == 0 && dpiX > 0)
                    return NormalizeScale((int)Math.Round(dpiX * 100.0 / 96.0));

                if (Native.GetScaleFactorForMonitor(monitor, out int scale) == 0 && scale > 0)
                    return NormalizeScale(scale);
            }
            catch
            {
            }
            finally
            {
                if (oldContext != IntPtr.Zero)
                {
                    try { Native.SetThreadDpiAwarenessContext(oldContext); } catch { }
                }
            }

            return 100;
        }

        private static int NormalizeScale(int scale)
        {
            int closest = SupportedScales[0];
            int distance = Math.Abs(scale - closest);
            foreach (int candidate in SupportedScales)
            {
                int currentDistance = Math.Abs(scale - candidate);
                if (currentDistance < distance)
                {
                    closest = candidate;
                    distance = currentDistance;
                }
            }
            return closest;
        }

        private static bool TrySetPrimaryScale(int scalePercent)
        {
            var beforeProcesses = GetSystemSettingsProcessIds();
            bool launched = false;
            AutomationElement settingsWindow = null;
            AutomationElement combo = null;

            try
            {
                combo = FindScaleCombo(out settingsWindow);
                if (combo == null)
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "ms-settings:display",
                        UseShellExecute = true
                    });
                    launched = true;

                    for (int i = 0; i < 60 && combo == null; i++)
                    {
                        Thread.Sleep(250);
                        combo = FindScaleCombo(out settingsWindow);
                    }
                }

                if (combo == null || settingsWindow == null)
                {
                    Logger.Warn("未找到 Windows 显示设置的缩放控件");
                    return false;
                }

                if (!combo.Current.IsEnabled)
                {
                    SelectPrimaryDisplayTile(settingsWindow);
                    Thread.Sleep(700);
                    combo = FindScaleCombo(out settingsWindow);
                }

                if (combo == null || !combo.Current.IsEnabled)
                {
                    Logger.Warn("Windows 显示设置的缩放控件不可用");
                    return false;
                }

                var expand = (ExpandCollapsePattern)combo.GetCurrentPattern(ExpandCollapsePattern.Pattern);
                expand.Expand();
                Thread.Sleep(500);

                var options = combo.FindAll(TreeScope.Descendants, Condition.TrueCondition);
                AutomationElement selected = null;
                string wantedPrefix = scalePercent + "%";
                foreach (AutomationElement option in options)
                {
                    if (option.Current.ControlType != ControlType.ListItem) continue;
                    if (!option.Current.Name.StartsWith(wantedPrefix, StringComparison.OrdinalIgnoreCase))
                        continue;
                    selected = option;
                    break;
                }

                if (selected == null)
                {
                    Logger.Warn($"Windows 显示设置中没有 {scalePercent}% 缩放选项");
                    try { expand.Collapse(); } catch { }
                    return false;
                }

                var selection = (SelectionItemPattern)selected.GetCurrentPattern(SelectionItemPattern.Pattern);
                selection.Select();
                Thread.Sleep(1800);
                try { expand.Collapse(); } catch { }
                return true;
            }
            catch (Exception ex)
            {
                Logger.Warn("自动调整 Windows 缩放失败: " + ex.Message);
                return false;
            }
            finally
            {
                if (launched)
                {
                    try
                    {
                        if (settingsWindow != null && settingsWindow.Current.NativeWindowHandle != 0)
                            Native.PostMessage(
                                new IntPtr(settingsWindow.Current.NativeWindowHandle),
                                Native.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                    }
                    catch { }

                    Thread.Sleep(1000);
                    var afterProcesses = GetSystemSettingsProcessIds();
                    foreach (int processId in afterProcesses.Except(beforeProcesses))
                    {
                        try
                        {
                            using var process = Process.GetProcessById(processId);
                            if (!process.HasExited) process.Kill();
                        }
                        catch { }
                    }
                }
            }
        }

        private static AutomationElement FindScaleCombo(out AutomationElement settingsWindow)
        {
            settingsWindow = null;
            var root = AutomationElement.RootElement;
            var windows = root.FindAll(TreeScope.Children, Condition.TrueCondition);
            var byId = new PropertyCondition(
                AutomationElement.AutomationIdProperty,
                "SystemSettings_Display_Scaling_ItemSizeOverride_ComboBox");

            foreach (AutomationElement window in windows)
            {
                try
                {
                    if (window.Current.ClassName != "ApplicationFrameWindow") continue;
                    var combo = window.FindFirst(TreeScope.Descendants, byId);
                    if (combo == null) continue;
                    settingsWindow = window;
                    return combo;
                }
                catch
                {
                }
            }
            return null;
        }

        private static void SelectPrimaryDisplayTile(AutomationElement settingsWindow)
        {
            var listItems = settingsWindow.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem));

            foreach (AutomationElement item in listItems)
            {
                string id = item.Current.AutomationId ?? "";
                if (!id.StartsWith("Display", StringComparison.OrdinalIgnoreCase)) continue;

                try
                {
                    var selection = (SelectionItemPattern)item.GetCurrentPattern(SelectionItemPattern.Pattern);
                    selection.Select();
                    Thread.Sleep(500);

                    var mainMonitor = settingsWindow.FindFirst(
                        TreeScope.Descendants,
                        new PropertyCondition(
                            AutomationElement.AutomationIdProperty,
                            "SystemSettings_Display_MainMonitor_CheckBox"));

                    // The virtual screen is primary in Night mode, so Windows
                    // disables "make this my main display" for the correct tile.
                    if (mainMonitor != null && !mainMonitor.Current.IsEnabled)
                        return;
                }
                catch
                {
                }
            }
        }

        private static HashSet<int> GetSystemSettingsProcessIds()
        {
            try
            {
                return Process.GetProcessesByName("SystemSettings")
                    .Select(p => p.Id)
                    .ToHashSet();
            }
            catch
            {
                return new HashSet<int>();
            }
        }

        private sealed class DisplayModeInfo
        {
            public int Width;
            public int Height;
            public int RefreshRate;
        }

        private static class Native
        {
            public const int ENUM_CURRENT_SETTINGS = -1;
            public const uint MONITOR_DEFAULTTOPRIMARY = 1;
            public const long DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4;

            public const int DM_POSITION = 0x00000020;
            public const int DM_BITSPERPEL = 0x00040000;
            public const int DM_PELSWIDTH = 0x00080000;
            public const int DM_PELSHEIGHT = 0x00100000;
            public const int DM_DISPLAYFREQUENCY = 0x00400000;

            public const uint CDS_UPDATEREGISTRY = 0x00000001;
            public const uint CDS_TEST = 0x00000002;
            public const int DISP_CHANGE_SUCCESSFUL = 0;

            public const uint WM_CLOSE = 0x0010;

            [StructLayout(LayoutKind.Sequential)]
            public struct POINT
            {
                public int X;
                public int Y;
            }

            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
            public struct DEVMODE
            {
                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
                public short dmSpecVersion;
                public short dmDriverVersion;
                public short dmSize;
                public short dmDriverExtra;
                public int dmFields;
                public int dmPositionX;
                public int dmPositionY;
                public int dmDisplayOrientation;
                public int dmDisplayFixedOutput;
                public short dmColor;
                public short dmDuplex;
                public short dmYResolution;
                public short dmTTOption;
                public short dmCollate;
                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
                public short dmLogPixels;
                public int dmBitsPerPel;
                public int dmPelsWidth;
                public int dmPelsHeight;
                public int dmDisplayFlags;
                public int dmDisplayFrequency;
                public int dmICMMethod;
                public int dmICMIntent;
                public int dmMediaType;
                public int dmDitherType;
                public int dmReserved1;
                public int dmReserved2;
                public int dmPanningWidth;
                public int dmPanningHeight;
            }

            [DllImport("user32.dll", CharSet = CharSet.Ansi)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DEVMODE devMode);

            [DllImport("user32.dll", CharSet = CharSet.Ansi)]
            public static extern int ChangeDisplaySettingsEx(
                string deviceName, ref DEVMODE devMode, IntPtr hwnd, uint flags, IntPtr param);

            [DllImport("user32.dll")]
            public static extern IntPtr MonitorFromPoint(POINT point, uint flags);

            [DllImport("user32.dll")]
            public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr dpiContext);

            [DllImport("shcore.dll")]
            public static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

            [DllImport("shcore.dll")]
            public static extern int GetScaleFactorForMonitor(IntPtr monitor, out int scale);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
        }
    }
}
