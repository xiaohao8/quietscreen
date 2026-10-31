using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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

        /// <summary>调用系统 DisplaySwitch 切换显示模式 (与旧脚本同一路径, 稳定可靠)</summary>
        public static bool SwitchTo(Topology t)
        {
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
                Logger.Info($"已切换显示模式: {t} ({arg})");
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
