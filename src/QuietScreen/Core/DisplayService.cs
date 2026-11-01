using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using QuietScreen.Core.Vdd;

namespace QuietScreen.Core
{
    public class DisplayInfo
    {
        public string DeviceName;     // \\.\DISPLAY1
        public string ShortName;      // DISPLAY1
        public string FriendlyName;   // 显示器/适配器名
        public int Width;
        public int Height;
        public bool Primary;
        public bool IsVirtual;

        public string Resolution => $"{Width}x{Height}";
    }

    /// <summary>显示器枚举 / 主屏判断 / 拓扑切换</summary>
    public static class DisplayService
    {
        /// <summary>当前活动的显示器 (来自 VddDisplay 枚举 + 正则兜底识别其他虚拟屏软件)</summary>
        public static List<DisplayInfo> GetDisplays()
        {
            var list = new List<DisplayInfo>();
            try
            {
                foreach (var d in VddDisplay.GetAllDisplays())
                {
                    if (!d.Active) continue;
                    var info = new DisplayInfo
                    {
                        DeviceName = d.DeviceName,
                        ShortName = d.DeviceName.Replace(@"\\.\", ""),
                        FriendlyName = d.IsParsec ? "Parsec 虚拟屏" : d.DisplayName,
                        Width = d.Width,
                        Height = d.Height,
                        Primary = d.Primary,
                        IsVirtual = d.IsParsec || MatchesVirtualPattern(d.DeviceName),
                    };
                    list.Add(info);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("枚举显示器失败: " + ex.Message);
            }
            return list;
        }

        private static bool MatchesVirtualPattern(string deviceName)
        {
            if (string.IsNullOrEmpty(deviceName)) return false;
            string shortName = deviceName.Replace(@"\\.\", "");
            try
            {
                return Regex.IsMatch(shortName, AppConfig.Current.VirtualDisplayPattern ?? "",
                                     RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }
            catch
            {
                return Regex.IsMatch(shortName, @"^DISPLAY([2-9]\d)$");
            }
        }

        public static DisplayInfo GetPrimary()
        {
            return GetDisplays().FirstOrDefault(d => d.Primary);
        }

        public static bool IsPrimaryVirtual()
        {
            var p = GetPrimary();
            return p != null && p.IsVirtual;
        }

        /// <summary>轮询等待主屏满足条件 (每 3 秒查一次)</summary>
        public static bool WaitPrimary(Func<DisplayInfo, bool> predicate, int tries)
        {
            for (int i = 0; i < tries; i++)
            {
                Task.Delay(3000).Wait();
                var p = GetPrimary();
                if (p != null && predicate(p)) return true;
            }
            return false;
        }

        // ---------------- 拓扑切换 ----------------
        public enum Topology { InternalOnly, ExternalOnly, Extend, Duplicate }

        // ---- Windows 显示拓扑 API (DisplaySwitch.exe 的底层实现, 不依赖外部进程) ----
        [DllImport("user32.dll")]
        private static extern int SetDisplayConfig(uint numPathArrayElements, uint[] pathArray, uint numModeArrayElements, uint[] modeArray, uint flags);

        private const uint SDC_TOPOLOGY_INTERNAL = 0x00000001;
        private const uint SDC_TOPOLOGY_EXTERNAL = 0x00000002;
        private const uint SDC_TOPOLOGY_CLONE = 0x00000004;
        private const uint SDC_TOPOLOGY_EXTEND = 0x00000008;
        private const uint SDC_ALLOW_CHANGES = 0x00000040;

        /// <summary>纯拓扑切换 (等价 DisplaySwitch 的四个按钮), 返回是否成功</summary>
        public static bool SetTopology(Topology t)
        {
            uint flag = t switch
            {
                Topology.InternalOnly => SDC_TOPOLOGY_INTERNAL,
                Topology.ExternalOnly => SDC_TOPOLOGY_EXTERNAL,
                Topology.Extend => SDC_TOPOLOGY_EXTEND,
                Topology.Duplicate => SDC_TOPOLOGY_CLONE,
                _ => 0
            };
            try
            {
                // 路径/模式数组传空 = 只改拓扑, 保留各屏分辨率
                int result = SetDisplayConfig(0, null, 0, null, flag | SDC_ALLOW_CHANGES);
                return result == 0;   // ERROR_SUCCESS
            }
            catch { return false; }
        }

        /// <summary>切换显示模式: 优先 Windows 拓扑 API (无外部进程), 失败回退 DisplaySwitch.exe</summary>
        public static bool SwitchTo(Topology t)
        {
            if (SetTopology(t))
            {
                Logger.Info($"已切换显示模式: {t} (SetDisplayConfig)");
                return true;
            }
            // API 失败 (罕见): 回退 DisplaySwitch.exe
            string arg = t switch
            {
                Topology.InternalOnly => "/internal",
                Topology.ExternalOnly => "/external",
                Topology.Extend => "/extend",
                Topology.Duplicate => "/clone",
                _ => ""
            };
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = System.IO.Path.Combine(Environment.SystemDirectory, "DisplaySwitch.exe"),
                    Arguments = arg,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    UseShellExecute = false
                });
                Logger.Info($"已切换显示模式: {t} (DisplaySwitch)");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error("DisplaySwitch 失败: " + ex.Message);
                return false;
            }
        }
    }
}
