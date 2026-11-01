using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using QuietScreen.Core.Vdd;

namespace QuietScreen.Core
{
    public enum OperatingMode { Local, Night }

    public class GuardianSnapshot
    {
        public bool Running;
        public bool Busy;
        public OperatingMode Mode;
        public int CheckCount;
        public int RepairCount;
        public string LastCheckText = "尚未检查";
        public string PrimaryText = "";
        public string AgentText = "";
        public DateTime LastCheckTime;
    }

    /// <summary>
    /// 守护服务 —— 双模式状态机:
    ///
    ///   本机模式 (Local): 内屏正常使用, 程序不碰显示拓扑; 只保活远控代理; 防误触解除。
    ///   夜间远控 (Night): 虚拟屏为主屏 + 内屏断电 + 防误触布防 + 心跳保活 + 代理守护。
    ///
    /// 进入夜间: 热键 Ctrl+Alt+Esc / 托盘·主页一键息屏 / 本机闲置 30 分钟自动。
    /// 回到本机: 按住 Ctrl+Esc 1 秒 (防误触唤醒) / 热键 Ctrl+Esc / 托盘·主页恢复内屏。
    /// </summary>
    public sealed class GuardianService
    {
        public static GuardianService Instance { get; } = new GuardianService();


        private volatile bool _running;
        private volatile OperatingMode _mode = OperatingMode.Local;
        private CancellationTokenSource _cts;
        private long _checkCount;
        private long _repairCount;
        private string _lastCheckText = "尚未检查";
        private string _agentText = "";
        private string _lastSignature = null;
        private DateTime _lastCheckTime;
        private readonly SemaphoreSlim _checkGate = new SemaphoreSlim(1, 1);

        public bool IsRunning => _running;
        public OperatingMode Mode => _mode;
        public bool IsNightMode => _mode == OperatingMode.Night;
        public event Action SnapshotChanged;

        public GuardianSnapshot GetSnapshot()
        {
            var p = DisplayService.GetPrimary();
            return new GuardianSnapshot
            {
                Running = _running,
                Busy = _checkGate.CurrentCount == 0,
                Mode = _mode,
                CheckCount = (int)Interlocked.Read(ref _checkCount),
                RepairCount = (int)Interlocked.Read(ref _repairCount),
                LastCheckText = _lastCheckText,
                PrimaryText = p == null ? "无显示器" : $"{p.ShortName} {p.Resolution}{(p.IsVirtual ? " (虚拟屏)" : "")}{(p.Primary ? " ·主屏" : "")}",
                AgentText = _agentText,
                LastCheckTime = _lastCheckTime,
            };
        }

        public void Initialize()
        {
            Task.Run(() => { try { DisplayService.GetDisplays(); } catch { } });
        }

        // ---------------- 模式切换 ----------------

        /// <summary>进入夜间远控模式: 创建/复用虚拟屏 → 切息屏 → 键盘灯联动 (立即执行, 不看在场状态)</summary>
        public void SetNight(string reason)
        {
            bool wasNight = _mode == OperatingMode.Night;
            _mode = OperatingMode.Night;
            if (!_running) Start();
            if (!wasNight) Logger.Ok($"进入夜间远控模式 ({reason})");
            Task.Run(async () =>
            {
                try
                {
                    await VddService.EnsureVirtualDisplayAsync();   // 缺屏才创建 (常驻后跳过)
                    DisplayService.SwitchTo(DisplayService.Topology.ExternalOnly);
                    var r = KeyboardLightService.TrySoftwareLightsOff();
                    Logger.Info("键盘灯: " + r.Message);
                }
                catch (Exception ex) { Logger.Error("夜间模式切换异常: " + ex.Message); }
            });
            RaiseChanged();
        }

        /// <summary>回到本机模式: 纯拓扑切换 (虚拟屏转入后台保活, 秒级生效)</summary>
        public void SetLocal(string reason, bool switchDisplay = true)
        {
            _mode = OperatingMode.Local;
            Logger.Info($"进入本机模式 ({reason})");
            if (switchDisplay) DisplayService.SwitchTo(DisplayService.Topology.InternalOnly);
            RaiseChanged();
        }

        // ---------------- 生命周期 ----------------

        public void Start()
        {
            if (_running) return;
            _running = true;
            _cts = new CancellationTokenSource();
            VddService.EnsureStarted();
            var token = _cts.Token;
            // 常驻单屏: 启动时确保恰好一个虚拟屏存在 (缺失才创建), 心跳终身保活
            Task.Run(async () =>
            {
                try
                {
                    await VddService.EnsureVirtualDisplayAsync();
                    Logger.Ok("常驻虚拟屏就绪");
                }
                catch (Exception ex) { Logger.Error("常驻虚拟屏创建失败: " + ex.Message); }
            });
            Task.Run(() => Loop(token), token);
            Logger.Ok("守护已开启");
            RaiseChanged();
        }

        public void Stop()
        {
            if (!_running) return;
            _running = false;
            try { _cts?.Cancel(); } catch { }
            Logger.Info("守护已停止");
            RaiseChanged();
        }

        /// <summary>立即执行一轮当前模式的维护</summary>
        public void CheckNow()
        {
            Task.Run(() => CheckAsync(true).Wait());
        }

        private async Task Loop(CancellationToken token)
        {
            bool firstRun = true;
            while (!token.IsCancellationRequested)
            {
                int interval = Math.Max(10, AppConfig.Current.CheckIntervalSec) * 1000;
                try
                {
                    if (firstRun)
                        await Task.Delay(TimeSpan.FromSeconds(20), token);   // 等显示拓扑就绪

                    await CheckAsync(firstRun);
                    firstRun = false;

                    await Task.Delay(interval, token);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    Logger.Error("巡检异常: " + ex.Message);
                    try { await Task.Delay(5000, token); } catch (OperationCanceledException) { break; }
                }
            }
        }

        // ---------------- 单轮维护 ----------------

        private async Task CheckAsync(bool forceReport)
        {
            await _checkGate.WaitAsync();
            try
            {
                var cfg = AppConfig.Current;
                var actions = new List<string>();

                Interlocked.Increment(ref _checkCount);

                var primary = DisplayService.GetDisplays().FirstOrDefault(d => d.Primary);
                bool primaryVirtual = primary != null && primary.IsVirtual;
                string checkText;

                if (_mode == OperatingMode.Night)
                {
                    // ---- 夜间维护: 保证虚拟屏存在且为主屏 ----
                    if (!primaryVirtual)
                    {
                        Logger.Warn($"夜间模式主屏异常 (当前: {primary?.ShortName ?? "?"}), 开始恢复虚拟屏...");
                        Interlocked.Increment(ref _repairCount);
                        string repairMsg = await RepairAsync();
                        actions.Add(repairMsg);
                        checkText = repairMsg;
                    }
                    else
                    {
                        checkText = $"夜间远控中 {primary.ShortName} {primary.Resolution}, 内屏已熄灭";
                    }

                }
                else
                {
                    // ---- 本机模式: 不碰拓扑, 只解除防误触 ----
                    checkText = "本机模式: 内屏使用中";
                }

                // ---- 关闭旧版工具 (其自动重加屏与本程序抢屏) ----
                if (cfg.CloseLegacyToolEnabled)
                {
                    try
                    {
                        var legacy = System.Diagnostics.Process.GetProcessesByName("ParsecVDisplay");
                        foreach (var pr in legacy)
                        {
                            try { pr.CloseMainWindow(); } catch { }
                            try { pr.Kill(); } catch { }
                            Logger.Info("已关闭旧版 ParsecVDisplay (避免抢屏)");
                        }
                    }
                    catch { }
                }

                // ---- 远控代理守护 (两种模式都保活) ----
                string agentAction = null;
                if (AgentService.IsConfigured())
                {
                    bool ok = AgentService.EnsureAgentRunning(out var action);
                    _agentText = ok ? $"正常 ({action})" : $"异常 ({action})";
                    if (action != "正常") { agentAction = action; actions.Add($"向日葵代理: {action}"); }
                }
                else
                {
                    _agentText = "未配置";
                }

                _lastCheckTime = DateTime.Now;
                _lastCheckText = checkText;

                // ---- 通知: 状态翻转才提醒 ----
                string sigOk = (_mode == OperatingMode.Local || primaryVirtual || checkText.StartsWith("虚拟屏已恢复")) ? "ok" : "bad";
                string signature = $"{(_mode == OperatingMode.Night ? "N:" : "L:")}{sigOk}|{agentAction ?? "-"}";
                bool flipped = signature != _lastSignature;
                if (flipped || (forceReport && actions.Count > 0))
                {
                    _lastSignature = signature;
                    if (cfg.ToastOnChange)
                        ToastService.Show("QuietScreen", actions.Count > 0 ? string.Join("\n", actions) : _lastCheckText);
                    Logger.Info("状态变化: " + signature);
                }

                RaiseChanged();
            }
            finally
            {
                _checkGate.Release();
            }
        }

        /// <summary>恢复虚拟屏 (复用优先, 创建兜底)</summary>
        private async Task<string> RepairAsync()
        {
            var cfg = AppConfig.Current;

            // 阶段一: 复用现有虚拟屏 (只切拓扑, 不新建)
            for (int i = 0; i < 2; i++)
            {
                if (VddDisplay.GetParsecDisplays().Count == 0) break;
                if (cfg.SwitchExternal)
                {
                    DisplayService.SwitchTo(DisplayService.Topology.ExternalOnly);
                    await Task.Delay(6000);
                }
                if (DisplayService.IsPrimaryVirtual())
                {
                    var cur = DisplayService.GetPrimary();
                    Logger.Ok($"复用现有虚拟屏: {cur?.ShortName} {cur?.Resolution}");
                    return $"虚拟屏已恢复 {cur?.ShortName}, 内屏已熄灭";
                }
            }

            // 阶段二: 创建新虚拟屏 (旧脚本两段式)
            for (int attempt = 1; attempt <= 2; attempt++)
            {
                if (attempt == 2)
                {
                    Logger.Warn("常规恢复无效, 清理后重试");
                    try { VddService.RemoveAllVirtualDisplays(); } catch { }
                    await Task.Delay(2000);
                }

                var r = await Task.Run(() => VddService.CreateVirtualDisplay(clean: cfg.CleanExtraDisplays));
                if (!r.Success)
                {
                    Logger.Warn($"本次恢复失败: {r.Message}");
                    var st = VddController.QueryStatusFresh(out _);
                    if (st != VddDriver.DriverStatus.OK)
                        return $"虚拟屏恢复失败: {VddService.StatusToText(st)}";
                    continue;
                }

                for (int try2 = 0; try2 < 5; try2++)
                {
                    await Task.Delay(3000);
                    if (DisplayService.IsPrimaryVirtual()) break;
                }

                if (DisplayService.IsPrimaryVirtual())
                {
                    Logger.Ok($"虚拟屏恢复成功: {r.ScreenName} {r.Resolution}");
                    return $"虚拟屏已恢复 {r.ScreenName}, 主屏正常";
                }

                if (cfg.SwitchExternal)
                {
                    DisplayService.SwitchTo(DisplayService.Topology.ExternalOnly);
                    await Task.Delay(6000);
                }

                if (DisplayService.IsPrimaryVirtual())
                {
                    Logger.Ok($"虚拟屏恢复成功并已切换主屏: {r.ScreenName} {r.Resolution}");
                    return $"虚拟屏已恢复 {r.ScreenName}, 内屏已熄灭";
                }

                Logger.Error($"虚拟屏恢复失败 (当前主屏: {DisplayService.GetPrimary()?.ShortName ?? "?"})");
            }

            return "虚拟屏恢复失败, 请重启电脑后重试";
        }


        private void RaiseChanged()
        {
            try { SnapshotChanged?.Invoke(); } catch { }
        }
    }
}
