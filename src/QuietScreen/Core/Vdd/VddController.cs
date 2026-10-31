// ============================================================================
//  VddController - 驱动句柄管理 / 心跳保活 / 睡眠唤醒保护 / 创建移除
//  移植自开源项目 nomi-san/parsec-vdd (MIT License) app/Vdd/Controller.cs
//  https://github.com/nomi-san/parsec-vdd  Copyright (c) 2023 Nguyen Duy
//
//  关键机制 (与原实现一致):
//  - 心跳线程每 100ms 向驱动发一次 IOCTL_UPDATE, 停止约 1 秒
//    驱动看门狗会拔掉全部虚拟屏 —— 所以本控制器必须随应用常驻。
//  - 状态线程每 2 秒查驱动状态, OK 时自动打开设备句柄。
//  - 系统睡眠前先按倒序拔掉虚拟屏并关句柄, 唤醒后自动恢复。
// ============================================================================
using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Win32;

namespace QuietScreen.Core.Vdd
{
    public static class VddController
    {
        private static Thread _updateThread;
        private static Thread _statusThread;
        private static CancellationTokenSource _cts;
        private static IntPtr _handle = IntPtr.Zero;
        private static int _lastStatus = int.MinValue;
        private static int _activeDisplays;                    // 当前需要心跳保活的虚拟屏数 (0 = 不用 ping)
        private static ManualResetEventSlim _statusKick;
        private static ManualResetEventSlim _handleReady;
        private static volatile bool _suspended;
        private static readonly object HandleGate = new object();

        public static bool IsRunning => _cts != null;
        public static bool IsHandleReady => VddDriver.IsValidHandle(Volatile.Read(ref _handle));
        /// <summary>当前设备句柄 (供驱动层清扫等维护操作读取)</summary>
        public static IntPtr HandleRef => Volatile.Read(ref _handle);
        public static bool IsSuspended => _suspended;

        // ---------------- 生命周期 ----------------

        public static void Start()
        {
            if (_cts != null) return;
            _cts = new CancellationTokenSource();
            _statusKick = new ManualResetEventSlim(false);
            _handleReady = new ManualResetEventSlim(false);
            _suspended = false;
            _lastStatus = int.MinValue;

            _updateThread = new Thread(() => UpdateLoop(_cts.Token))
            {
                IsBackground = true,
                Priority = ThreadPriority.Highest,
                Name = "VddUpdateLoop"
            };
            _statusThread = new Thread(() => StatusLoop(_cts.Token))
            {
                IsBackground = true,
                Priority = ThreadPriority.BelowNormal,
                Name = "VddStatusLoop"
            };
            _updateThread.Start();
            _statusThread.Start();

            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            Logger.Info("VDD 控制器已启动");
        }

        public static void Stop()
        {
            if (_cts == null) return;
            Logger.Info("VDD 控制器停止中...");
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            _cts.Cancel();
            _statusKick?.Set();
            try { _updateThread?.Join(3000); } catch { }
            try { _statusThread?.Join(3000); } catch { }

            var handle = Interlocked.Exchange(ref _handle, IntPtr.Zero);
            VddDriver.CloseHandle(handle);
            _statusKick?.Dispose(); _statusKick = null;
            _handleReady?.Dispose(); _handleReady = null;
            _cts.Dispose(); _cts = null;
            Logger.Info("VDD 控制器已停止");
        }

        public static bool WaitForReady(int timeoutMs)
        {
            return _handleReady != null && _handleReady.Wait(timeoutMs);
        }

        public static void KickStatusCheck() => _statusKick?.Set();

        // ---------------- 电源事件 ----------------

        private static void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            try
            {
                if (e.Mode == PowerModes.Suspend) Suspend();
                else if (e.Mode == PowerModes.Resume) Resume();
            }
            catch (Exception ex)
            {
                Logger.Error("电源事件处理失败: " + ex.Message);
            }
        }

        /// <summary>睡眠前: 快照并倒序拔掉虚拟屏, 关闭句柄 (保留 Windows10 连接性注册表配置)</summary>
        public static void Suspend()
        {
            if (_suspended) return;
            var displays = VddDisplay.GetParsecDisplays();
            Logger.Info($"睡眠: 拔掉 {displays.Count} 个虚拟屏");
            _suspended = true;

            lock (HandleGate)
            {
                var handle = Volatile.Read(ref _handle);
                if (VddDriver.IsValidHandle(handle))
                {
                    for (int i = displays.Count - 1; i >= 0; i--)
                    {
                        try { VddDriver.RemoveDisplay(handle, displays[i].DriverIndex); }
                        catch (Exception ex) { Logger.Warn($"睡眠拔屏失败 idx={displays[i].DriverIndex}: {ex.Message}"); }
                    }
                }
                var closed = Interlocked.Exchange(ref _handle, IntPtr.Zero);
                _handleReady?.Reset();
                VddDriver.CloseHandle(closed);
            }
        }

        public static void Resume()
        {
            Logger.Info("唤醒: 恢复驱动连接");
            _suspended = false;
            _statusKick?.Set();
        }

        // ---------------- 后台循环 ----------------

        private static void UpdateLoop(CancellationToken cancellation)
        {
            // 心跳必须 <=200ms 一次 (看门狗 1 秒无心跳会拔掉全部虚拟屏)。
            // 用停表补偿 Thread.Sleep 的漂移, 系统高负载时也不会掉拍。
            // 优化: 没有虚拟屏时 (白天正常使用) 完全不 ping, 线程睡长觉 —— CPU 趋零。
            var sw = System.Diagnostics.Stopwatch.StartNew();
            long next = 0;
            while (!cancellation.IsCancellationRequested)
            {
                if (!_suspended)
                {
                    var handle = Volatile.Read(ref _handle);
                    if (VddDriver.IsValidHandle(handle) &&
                        (VddDriver.DriverStatus)Volatile.Read(ref _lastStatus) == VddDriver.DriverStatus.OK)
                    {
                        try { VddDriver.Update(handle); }
                        catch { }
                    }
                }

                next += 100;
                long sleepMs = next - sw.ElapsedMilliseconds;
                if (sleepMs > 0) Thread.Sleep((int)Math.Min(sleepMs, 100));
                else if (sw.ElapsedMilliseconds - next > 500)
                    next = sw.ElapsedMilliseconds;   // 落后太多则重置节拍, 避免连发追赶
            }
        }

        /// <summary>同步"需要保活的虚拟屏数量" (守护巡检用实际枚举结果校准, 防计数漂移)</summary>
        public static void SyncActiveDisplays(int count)
        {
            Volatile.Write(ref _activeDisplays, Math.Max(0, count));
        }

        private static void OnDisplayAdded() => Interlocked.Increment(ref _activeDisplays);

        private static void OnDisplayRemoved() =>
            Interlocked.Exchange(ref _activeDisplays, Math.Max(0, Volatile.Read(ref _activeDisplays) - 1));

        private static void StatusLoop(CancellationToken cancellation)
        {
            while (!cancellation.IsCancellationRequested)
            {
                if (_suspended)
                {
                    try { _statusKick.Wait(Timeout.Infinite, cancellation); }
                    catch (OperationCanceledException) { break; }
                    _statusKick.Reset();
                    continue;
                }

                var prev = (VddDriver.DriverStatus)_lastStatus;
                // 必须实时查询: 若读缓存值, 状态循环将永远停留在首帧结果,
                // 驱动失效时无法感知 (原版实现每轮都是 fresh 查询)
                var status = VddDriver.QueryStatus(out var _);
                Volatile.Write(ref _lastStatus, (int)status);
                if ((int)prev != int.MinValue && status != prev)
                    Logger.Info($"驱动状态: {prev} -> {status}");

                lock (HandleGate)
                {
                    if (!_suspended && status == VddDriver.DriverStatus.OK)
                    {
                        var handle = Volatile.Read(ref _handle);
                        if (!VddDriver.IsValidHandle(handle))
                        {
                            VddDriver.OpenHandle(out var h);
                            Volatile.Write(ref _handle, h);
                            if (VddDriver.IsValidHandle(h))
                            {
                                _handleReady?.Set();
                                Logger.Info("驱动句柄已打开");
                            }
                        }
                    }
                    else
                    {
                        var handle = Interlocked.Exchange(ref _handle, IntPtr.Zero);
                        if (VddDriver.IsValidHandle(handle))
                        {
                            _handleReady?.Reset();
                            VddDriver.CloseHandle(handle);
                            Logger.Info($"驱动句柄已关闭 (状态={status})");
                        }
                        // 驱动失效/离线: 心跳计数清零 (看门狗会拔掉所有虚拟屏, 无可保活)
                        Volatile.Write(ref _activeDisplays, 0);
                    }
                }

                // 自适应间隔: 一切正常 10 秒查一次 (省 CPU), 异常时 2 秒 (快速发现恢复)
                int wait = (status == VddDriver.DriverStatus.OK &&
                            VddDriver.IsValidHandle(Volatile.Read(ref _handle))) ? 10000 : 2000;
                try { _statusKick.Wait(wait, cancellation); }
                catch (OperationCanceledException) { break; }
                _statusKick.Reset();
            }
        }

        // ---------------- 对外操作 ----------------

        public static VddDriver.DriverStatus QueryStatus() =>
            _lastStatus == int.MinValue
                ? VddDriver.QueryStatus(out _)
                : (VddDriver.DriverStatus)Volatile.Read(ref _lastStatus);

        public static VddDriver.DriverStatus QueryStatusFresh(out Version version) =>
            VddDriver.QueryStatus(out version);

        public static string QueryDriverVersion()
        {
            var handle = Volatile.Read(ref _handle);
            if (VddDriver.IsValidHandle(handle) && VddDriver.GetVersion(handle, out var v))
                return v;
            VddDriver.QueryStatus(out var dv);
            return dv.Major > 0 ? dv.ToString(3) : "(未知)";
        }

        /// <summary>创建一个虚拟屏 (要求驱动状态 OK)。返回驱动索引, 失败返回 -1。</summary>
        public static int AddDisplay(out string error)
        {
            error = null;
            var status = QueryStatus();
            if (status != VddDriver.DriverStatus.OK)
            {
                error = "驱动状态异常: " + status;
                return -1;
            }

            if (VddDisplay.GetParsecDisplays().Count >= VddDriver.MAX_DISPLAYS)
            {
                error = $"虚拟屏数量已达上限 ({VddDriver.MAX_DISPLAYS})";
                return -1;
            }

            // 状态循环平时 10 秒才查一次, 这里主动踢醒它尽快就绪句柄
            KickStatusCheck();
            WaitForReady(5000);
            var handle = Volatile.Read(ref _handle);
            if (!VddDriver.IsValidHandle(handle))
            {
                error = "设备句柄未就绪";
                return -1;
            }

            // 与旧脚本一致: 单次 Add (干净驱动状态下一次即成)
            if (!VddDriver.AddDisplay(handle, out int index) || index < 0)
            {
                error = "创建虚拟屏 IOCTL 失败";
                return -1;
            }

            OnDisplayAdded();
            Logger.Info($"虚拟屏已创建, 驱动索引 {index}");
            return index;
        }

        /// <summary>按驱动索引移除虚拟屏</summary>
        public static bool RemoveDisplay(int index)
        {
            if (index < 0) return false;
            var status = QueryStatus();
            if (status != VddDriver.DriverStatus.OK) return false;

            WaitForReady(3000);
            var handle = Volatile.Read(ref _handle);
            if (!VddDriver.IsValidHandle(handle)) return false;
            if (VddDriver.RemoveDisplay(handle, index))
            {
                OnDisplayRemoved();
                return true;
            }
            return false;
        }

        /// <summary>移除当前所有 (GDI 可见的) 虚拟屏。不做驱动层盲扫 —— 实测盲扫会扰动驱动。</summary>
        public static int RemoveAllDisplays()
        {
            var displays = VddDisplay.GetParsecDisplays();
            int removed = 0;
            for (int i = displays.Count - 1; i >= 0; i--)
            {
                if (RemoveDisplay(displays[i].DriverIndex)) removed++;
            }
            SyncActiveDisplays(VddDisplay.GetParsecDisplays().Count);
            if (displays.Count > 0)
                Logger.Info($"移除虚拟屏: {removed}/{displays.Count}");
            return removed;
        }
    }
}
