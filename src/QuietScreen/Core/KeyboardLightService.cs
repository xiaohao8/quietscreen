using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;

namespace QuietScreen.Core
{
    /// <summary>
    /// 键盘背光控制能力说明 (如实):
    /// LT84 的 Fn 组合与灯效在键盘固件内部处理, 不经过 USB 上报, 软件永远无法模拟或读取状态;
    /// VID 1A2C 不在 OpenRGB 支持列表, 也不支持 Windows LampArray 标准灯效。
    /// 结论: 软件控灯对 LT84 不可行, 熄灯用键盘自带组合键 (专用灯效键循环到熄灭)。
    /// 本服务保留 OpenRGB 钩子与键盘身份检测, 以备将来更换支持协议的键盘。
    /// </summary>
    public static class KeyboardLightService
    {
        private static readonly string[] ProbePaths =
        {
            @"C:\Program Files\OpenRGB\openrgb.exe",
            @"C:\Program Files (x86)\OpenRGB\openrgb.exe",
            @"D:\OpenRGB\openrgb.exe",
        };

        /// <summary>枚举外接键盘的 USB 身份 (VID/PID), 用于向 OpenRGB 社区提交支持请求</summary>
        public static string GetKeyboardIdentity()
        {
            try
            {
                var ids = new System.Collections.Generic.List<string>();
                using (var searcher = new ManagementObjectSearcher(
                    "SELECT DeviceID FROM Win32_PnPEntity WHERE PNPClass='Keyboard'"))
                foreach (var o in searcher.Get())
                {
                    string id = (o["DeviceID"]?.ToString() ?? "");
                    if (id.StartsWith("HID\\VID_", StringComparison.OrdinalIgnoreCase) && !id.Contains("COL0"))
                        ids.Add(id.Split('\\')[1]);
                }
                var distinct = ids.Distinct().ToList();
                return distinct.Count > 0 ? string.Join("; ", distinct) : "未检测到 USB 键盘";
            }
            catch (Exception ex)
            {
                return "检测失败: " + ex.Message;
            }
        }

        /// <summary>软件控灯可行性: LT84 = 不可行; 支持 LampArray/OpenRGB 的键盘 = 可行</summary>
        public static (bool Possible, string Message) TrySoftwareLightsOff()
        {
            // 1. LampArray (Windows 标准灯效) — 本机探测无设备, 但保留判断以兼容未来键盘
            try
            {
                string selector = Windows.Devices.Lights.LampArray.GetDeviceSelector();
                var devOp = Windows.Devices.Enumeration.DeviceInformation.FindAllAsync(selector);
                while (devOp.Status == Windows.Foundation.AsyncStatus.Started) System.Threading.Thread.Sleep(30);
                if (devOp.GetResults().Count > 0)
                {
                    var devices = devOp.GetResults();
                    foreach (Windows.Devices.Enumeration.DeviceInformation di in devices)
                    {
                        var laOp = Windows.Devices.Lights.LampArray.FromIdAsync(di.Id);
                        while (laOp.Status == Windows.Foundation.AsyncStatus.Started) System.Threading.Thread.Sleep(30);
                        var la = laOp.GetResults();
                        la.SetColor(Windows.UI.Color.FromArgb(255, 0, 0, 0));
                    }
                    return (true, $"已通过 LampArray 熄灯 ({devices.Count} 个设备)");
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("LampArray 尝试失败: " + ex.Message);
            }

            // 2. OpenRGB 钩子
            string exe = FindOpenRgb();
            if (exe != null)
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = exe,
                        Arguments = "--mode static --color 000000",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    using (var p = Process.Start(psi))
                    {
                        p.WaitForExit(15000);
                        if (p.ExitCode == 0) return (true, "已通过 OpenRGB 熄灯");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn("OpenRGB 尝试失败: " + ex.Message);
                }
            }

            // 3. 诚实结论: LT84 固件控灯, 软件无通道
            return (false, "LT84 不支持软件控灯 (固件处理, 协议不开放) —— 请用键盘的专用灯效键循环到熄灭");
        }

        public static string FindOpenRgb()
        {
            foreach (var p in ProbePaths)
                if (File.Exists(p)) return p;
            var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var dir in pathEnv.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    var candidate = Path.Combine(dir.Trim(), "openrgb.exe");
                    if (File.Exists(candidate)) return candidate;
                }
                catch { }
            }
            return null;
        }
    }
}
