using System;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace QuietScreen.Core
{
    /// <summary>拯救者档位 (Lenovo GameZone IntelligentSubMode)</summary>
    public enum LegionMode
    {
        Beast = 0,     // 野兽/狂暴 (性能拉满, 风扇最响)
        Balanced = 1,  // 均衡
        Quiet = 2      // 安静 (降噪, 适合夜间)
    }

    /// <summary>
    /// 性能模式服务 —— 拯救者三档 (Fn+Q 对应的官方 WMI 接口) + Windows 电源叠加层。
    /// 全部操作有兜底: 任一路径不可用只报原因, 绝不抛异常打断守护流程。
    /// </summary>
    public sealed class PowerModeService
    {
        public static PowerModeService Instance { get; } = new PowerModeService();

        // ---- 拯救者 GameZone WMI 探测结果 (缓存) ----
        /// <summary>拯救者接口是否可用 (探测成功过 Get/Set 即 true)</summary>
        public bool LegionAvailable { get; private set; }
        /// <summary>不可用原因 (权限不足/非拯救者机型/接口异常)</summary>
        public string LegionUnavailableReason { get; private set; } = "未探测";
        /// <summary>最近一次读到的档位, -1 = 未知</summary>
        public int LastKnownLegionMode { get; private set; } = -1;

        // ---- Windows 电源叠加层 (Win11 滑条三档, 免管理员) ----
        // ⚠️ 联想等 OEM 会改 GUID 尾号 (实测 83F4: 能效档尾号 7d86181b8a7a, 文档值 6ddeb7cd7e03),
        //    硬编码文档值会报"参数无效"。正确做法: 运行时用 ResolveOverlay 从注册表按前缀发现。
        public const string OverlayBestEfficiencyPrefix = "961cc777-2547-4f9d-8174-";
        public const string OverlayBestPerformancePrefix = "ded574b5-45a0-4f42-8737-";
        public const string OverlayBalanced = "00000000-0000-0000-0000-000000000000";
        /// <summary>微软文档标准值 (仅作注册表发现失败时的兜底)</summary>
        public const string OverlayBestEfficiency = OverlayBestEfficiencyPrefix + "6ddeb7cd7e03";
        public const string OverlayBestPerformance = OverlayBestPerformancePrefix + "46345c09c238";

        // ---- 夜间自动降噪: 切换前快照, 回本机还原 ----
        private int _snapshotLegionMode = -1;
        private string _snapshotOverlay;
        private bool _hasSnapshot;

        private PowerModeService() { }

        // ================= 拯救者档位 =================

        /// <summary>探测拯救者 GameZone 接口 (只读, 只调 Get 不调 Set)。</summary>
        public async Task ProbeLegionAsync()
        {
            await Task.Run(() =>
            {
                try
                {
                    int mode = LegionWmiCall("GetIntelligentSubMode", null);
                    LastKnownLegionMode = mode;
                    LegionAvailable = true;
                    LegionUnavailableReason = "";
                    Logger.Info($"拯救者 GameZone 接口可用, 当前档位: {LegionModeName(mode)}");
                }
                catch (Exception ex)
                {
                    LegionAvailable = false;
                    LegionUnavailableReason = FriendlyLegionError(ex);
                    Logger.Info("拯救者 GameZone 接口不可用: " + LegionUnavailableReason);
                }
            });
        }

        /// <summary>切换拯救者档位。返回 (成功?, 结果描述)。</summary>
        public Task<(bool ok, string msg)> SetLegionModeAsync(LegionMode mode)
        {
            return Task.Run(() =>
            {
                try
                {
                    LegionWmiCall("SetIntelligentSubMode", (uint)mode);
                    LastKnownLegionMode = (int)mode;
                    string name = LegionModeName((int)mode);
                    Logger.Ok("拯救者档位 → " + name);
                    return (true, "已切换到 " + name);
                }
                catch (Exception ex)
                {
                    string reason = FriendlyLegionError(ex);
                    Logger.Error("拯救者档位切换失败: " + reason);
                    return (false, "切换失败: " + reason);
                }
            });
        }

        /// <summary>读取当前拯救者档位 (失败返回 -1, 不更新缓存)。</summary>
        public Task<int> GetLegionModeAsync()
        {
            return Task.Run(() =>
            {
                try
                {
                    int m = LegionWmiCall("GetIntelligentSubMode", null);
                    LastKnownLegionMode = m;
                    return m;
                }
                catch { return -1; }
            });
        }

        /// <summary>调用 GameZone WMI 方法 (单实例对象)。hasArg=false 时读, true 时写。</summary>
        private static int LegionWmiCall(string method, uint? arg)
        {
            using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM LENOVO_GAMEZONE_DATA");
            var instances = searcher.Get().Cast<ManagementObject>().ToList();
            if (instances.Count == 0) throw new InvalidOperationException("LENOVO_GAMEZONE_DATA 无实例 (非拯救者机型或接口未启用)");
            object result = null;
            foreach (var mo in instances)
            {
                if (arg.HasValue)
                {
                    ManagementBaseObject inParams = null;
                    try
                    {
                        inParams = mo.GetMethodParameters(method);
                        inParams["IntelligentSubMode"] = arg.Value;
                    }
                    catch
                    {
                        inParams = null;
                    }
                    result = inParams != null
                        ? mo.InvokeMethod(method, inParams, null)
                        : mo.InvokeMethod(method, new object[] { arg.Value });
                }
                else
                {
                    result = mo.InvokeMethod(method, new object[0]);
                }
            }
            if (result is uint u) return (int)u;
            return result != null && int.TryParse(result.ToString(), out int i) ? i : -1;
        }

        private static string LegionModeName(int mode) => mode switch
        {
            (int)LegionMode.Beast => "野兽/狂暴",
            (int)LegionMode.Balanced => "均衡",
            (int)LegionMode.Quiet => "安静",
            _ => "未知(" + mode + ")"
        };

        private static string FriendlyLegionError(Exception ex)
        {
            string m = ex.Message ?? "";
            if (m.Contains("拒绝访问") || m.Contains("Access denied") || (ex is UnauthorizedAccessException))
                return "权限不足 (WMI 拒绝访问)。可尝试以管理员身份运行, 或用 Fn+Q 手动切换";
            if (m.Contains("无实例") || m.Contains("not found") || m.Contains("Invalid class"))
                return "本机不支持拯救者档位接口 (非拯救者机型或 LegionZone 未启用)";
            return m.Length > 80 ? m.Substring(0, 80) : m;
        }

        // ================= Windows 电源叠加层 =================

        /// <summary>切换 Windows 电源档 (Win11 滑条: 最佳能效/平衡/最佳性能)。overlayGuid 传常量。</summary>
        public Task<(bool ok, string msg)> SetOverlayAsync(string overlayGuid)
        {
            return Task.Run(() =>
            {
                try
                {
                    var psi = new ProcessStartInfo("powercfg", "/overlaysetactive " + overlayGuid)
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    using var p = Process.Start(psi);
                    p.WaitForExit(8000);
                    if (p.ExitCode != 0)
                    {
                        string err = p.StandardError.ReadToEnd().Trim();
                        Logger.Error("电源档切换失败: " + (string.IsNullOrEmpty(err) ? "exit " + p.ExitCode : err));
                        return (false, "电源档切换失败 (本机可能不支持叠加层)");
                    }
                    string name = OverlayName(overlayGuid);
                    Logger.Ok("Windows 电源档 → " + name);
                    return (true, "已切换到 " + name);
                }
                catch (Exception ex)
                {
                    Logger.Error("电源档切换异常: " + ex.Message);
                    return (false, "电源档切换失败: " + ex.Message);
                }
            });
        }

        /// <summary>读取当前叠加层 GUID。优先读注册表 (部分联想机型不支持 powercfg /getactiveoverlay
        /// 但支持 /overlaysetactive —— 用查询命令判支持会把可用的切换按钮误禁用)。</summary>
        public Task<string> GetActiveOverlayAsync()
        {
            return Task.Run(() =>
            {
                // 路径 1: 注册表 (免管理员, 83F4 实测有效)
                try
                {
                    using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                        @"SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes");
                    var v = key?.GetValue("ActiveOverlayAcPowerScheme") as string;
                    if (!string.IsNullOrEmpty(v)) return v.ToLowerInvariant();
                }
                catch { }
                // 路径 2: powercfg (老系统回退)
                try
                {
                    var psi = new ProcessStartInfo("powercfg", "/getactiveoverlay")
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    using var p = Process.Start(psi);
                    string output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(8000);
                    if (p.ExitCode != 0) return null;
                    var m = Regex.Match(output, @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");
                    return m.Success ? m.Value.ToLowerInvariant() : null;
                }
                catch { return null; }
            });
        }

        /// <summary>
        /// 按角色解析本机实际的叠加层 GUID。role: "efficiency" / "balanced" / "performance"。
        /// 注册表 HKLM\...\PowerSchemes 枚举子键按微软固定前缀匹配 (OEM 只改尾号)。
        /// </summary>
        private static string _resolvedEfficiency, _resolvedPerformance;
        public static string ResolveOverlay(string role)
        {
            if (role == "balanced") return OverlayBalanced;
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes");
                if (key != null)
                {
                    foreach (var name in key.GetSubKeyNames())
                    {
                        string n = name.ToLowerInvariant();
                        if (n.StartsWith(OverlayBestEfficiencyPrefix)) _resolvedEfficiency = n;
                        else if (n.StartsWith(OverlayBestPerformancePrefix)) _resolvedPerformance = n;
                    }
                }
            }
            catch { }
            return role == "efficiency"
                ? (_resolvedEfficiency ?? OverlayBestEfficiency)
                : (_resolvedPerformance ?? OverlayBestPerformance);
        }

        private static string OverlayName(string guid) => guid switch
        {
            OverlayBalanced => "平衡",
            _ => guid == ResolveOverlay("efficiency") ? "最佳能效"
               : guid == ResolveOverlay("performance") ? "最佳性能"
               : "电源档 " + guid
        };

        // ================= 夜间自动降噪联动 =================

        /// <summary>
        /// 进入夜间模式时调用 (配置开启才生效):
        /// 快照当前档位 → 切到配置的夜间档位。返回给日志的描述, 未启用返回 null。
        /// </summary>
        public async Task<string> ApplyNightPolicyAsync(bool wasAlreadyNight)
        {
            var cfg = AppConfig.Current;
            if (!cfg.NightAutoQuietEnabled) return null;
            if (wasAlreadyNight) return null;   // 已在夜间 (如启动恢复), 不重复切也不覆盖快照

            _hasSnapshot = false;
            var parts = new System.Collections.Generic.List<string>();

            // 拯救者档位
            if (cfg.NightLegionMode >= 0)
            {
                if (!LegionAvailable) await ProbeLegionAsync();
                if (LegionAvailable)
                {
                    int prev = await GetLegionModeAsync();
                    if (prev >= 0)
                    {
                        _snapshotLegionMode = prev;
                        _hasSnapshot = true;
                    }
                    var r = await SetLegionModeAsync((LegionMode)cfg.NightLegionMode);
                    parts.Add("拯救者→" + LegionModeName(cfg.NightLegionMode) + (r.ok ? "" : " (失败)"));
                }
                else
                {
                    parts.Add("拯救者接口不可用, 跳过");
                }
            }

            // Windows 电源档 (配置存角色字符串: efficiency/balanced/performance/none, 亦兼容直接写 GUID)
            if (!string.IsNullOrEmpty(cfg.NightOverlay) && cfg.NightOverlay != "none")
            {
                string ov = cfg.NightOverlay;
                string guid = (ov == "efficiency" || ov == "performance" || ov == "balanced") ? ResolveOverlay(ov) : ov;
                string prevOverlay = await GetActiveOverlayAsync();
                if (prevOverlay != null && prevOverlay != guid)
                {
                    _snapshotOverlay = prevOverlay;
                    _hasSnapshot = true;
                }
                var r = await SetOverlayAsync(guid);
                parts.Add("电源档→" + OverlayName(guid) + (r.ok ? "" : " (失败)"));
            }

            return parts.Count == 0 ? null : "夜间降噪: " + string.Join(", ", parts);
        }

        /// <summary>回到本机模式时调用: 还原进夜间前的档位。返回给日志的描述, 无快照返回 null。</summary>
        public Task<string> RestoreAfterNightAsync()
        {
            if (!_hasSnapshot) return Task.FromResult<string>(null);
            _hasSnapshot = false;
            return Task.Run(async () =>
            {
                var parts = new System.Collections.Generic.List<string>();
                if (_snapshotLegionMode >= 0)
                {
                    var r = await SetLegionModeAsync((LegionMode)_snapshotLegionMode);
                    parts.Add("拯救者→" + LegionModeName(_snapshotLegionMode) + (r.ok ? "" : " (失败)"));
                    _snapshotLegionMode = -1;
                }
                if (!string.IsNullOrEmpty(_snapshotOverlay))
                {
                    var r = await SetOverlayAsync(_snapshotOverlay);
                    parts.Add("电源档→" + OverlayName(_snapshotOverlay) + (r.ok ? "" : " (失败)"));
                    _snapshotOverlay = null;
                }
                return parts.Count == 0 ? null : "还原性能模式: " + string.Join(", ", parts);
            });
        }
    }
}
