using System.Linq;
using System;
using System.Threading;
using System.Threading.Tasks;
using QuietScreen.Core.Vdd;

namespace QuietScreen.Core
{
    public class VddResult
    {
        public bool Success;
        public string Message = "";
        public string ScreenName = "";
        public string Resolution = "";

        public override string ToString()
        {
            if (Success) return (ScreenName + " " + Resolution).Trim();
            return Message;
        }
    }

    /// <summary>
    /// 虚拟屏操作门面: 直接通过 IOCTL 控制已安装的 Parsec 虚拟屏驱动
    /// (移植自开源项目 nomi-san/parsec-vdd), 不再依赖外部工具。
    /// </summary>
    public static class VddService
    {
        private static readonly SemaphoreSlim CreateGate = new SemaphoreSlim(1, 1);
        private static int _started;

        /// <summary>启动驱动控制器 (心跳/状态/句柄线程), 幂等</summary>
        public static void EnsureStarted()
        {
            if (Interlocked.Exchange(ref _started, 1) == 0)
                VddController.Start();
        }

        public static string DriverStatusText(out VddDriver.DriverStatus status, out Version version)
        {
            EnsureStarted();
            status = VddController.QueryStatusFresh(out version);
            return StatusToText(status) + (status == VddDriver.DriverStatus.OK ? $" (v{version})" : "");
        }

        /// <summary>驱动状态的中文说明</summary>
        public static string StatusToText(VddDriver.DriverStatus status)
        {
            switch (status)
            {
                case VddDriver.DriverStatus.OK: return "驱动正常";
                case VddDriver.DriverStatus.NOT_INSTALLED: return "未安装 Parsec 虚拟屏驱动";
                case VddDriver.DriverStatus.DISABLED: return "虚拟屏设备已被禁用";
                case VddDriver.DriverStatus.DISABLED_SERVICE: return "驱动服务已禁用";
                case VddDriver.DriverStatus.RESTART_REQUIRED: return "驱动需要重启电脑";
                case VddDriver.DriverStatus.DRIVER_ERROR: return "驱动启动失败";
                case VddDriver.DriverStatus.INACCESSIBLE: return "无法访问驱动设备";
                default: return "驱动状态异常: " + status;
            }
        }

        /// <summary>
        /// 确保恰好一个常驻虚拟屏存在 (ParsecVDisplay 秒切秘诀: 屏幕常驻, 切换只是拓扑)。
        /// 已存在 → 立即返回; 缺失 → 创建 (约 5-10 秒, 仅此一次)。
        /// </summary>
        public static async Task EnsureVirtualDisplayAsync()
        {
            await CreateGate.WaitAsync();
            try
            {
                // 关键: 判断"已附加"(含未激活), 而非"已激活" —— 否则本机模式下会把
                // 常驻屏误判为缺失, 重复创建导致多屏。
                if (VddDisplay.GetAllDisplays().Any(d => d.IsParsec))
                {
                    Logger.Info("常驻虚拟屏已就绪 (含未激活状态)");
                    return;
                }
                Logger.Info("常驻虚拟屏缺失, 创建中...");
                CreateVirtualDisplayLocked(clean: true);
            }
            finally { CreateGate.Release(); }
        }

        /// <summary>探测驱动是否可用 (强制实时查询)</summary>
        public static VddResult CheckDriver()
        {
            EnsureStarted();
            var status = VddController.QueryStatusFresh(out var version);
            return new VddResult
            {
                Success = status == VddDriver.DriverStatus.OK,
                Message = status == VddDriver.DriverStatus.OK
                    ? $"驱动正常 (v{version})"
                    : "驱动状态: " + status
            };
        }

        /// <summary>
        /// 创建虚拟屏。clean = 先清掉已有的虚拟屏 (对应旧版 CleanExtraDisplays)。
        /// 成功后轮询等待 GDI 出现新屏幕并返回其名称与分辨率。
        /// </summary>
        public static VddResult CreateVirtualDisplay(bool clean = true)
        {
            CreateGate.Wait();
            try { return CreateVirtualDisplayLocked(clean); }
            finally { CreateGate.Release(); }
        }

        private static VddResult CreateVirtualDisplayLocked(bool clean)
        {
            EnsureStarted();
            var r = new VddResult();

            if (clean)
            {
                int removed = VddController.RemoveAllDisplays();
                if (removed > 0) Thread.Sleep(2000);
            }

            int before = VddDisplay.GetParsecDisplays().Count;   // 基线在清空之后统计
            int index = VddController.AddDisplay(out string error);
            if (index < 0)
            {
                r.Message = error ?? "创建失败";
                Logger.Error("创建虚拟屏失败: " + r.Message);
                return r;
            }

            // 等待 GDI 枚举出新屏幕 (拓扑变化有延迟)
            for (int attempt = 0; attempt < 15; attempt++)
            {
                Thread.Sleep(2000);
                var screens = VddDisplay.GetParsecDisplays();
                if (screens.Count > before)
                {
                    var s = screens[screens.Count - 1];
                    r.Success = true;
                    r.ScreenName = s.DeviceName.Replace(@"\\.\", "");
                    r.Resolution = s.Resolution;
                    Logger.Ok($"虚拟屏创建成功: {r.ScreenName} {r.Resolution} (索引 {index})");
                    return r;
                }
            }

            r.Message = "IOCTL 成功但屏幕未出现 (索引 " + index + ")";
            Logger.Error(r.Message);
            return r;
        }

        /// <summary>移除所有虚拟屏</summary>
        public static VddResult RemoveAllVirtualDisplays()
        {
            EnsureStarted();
            int removed = VddController.RemoveAllDisplays();
            return new VddResult { Success = removed > 0, Message = $"已移除 {removed} 个虚拟屏" };
        }

        /// <summary>
        /// 清理积压的旧虚拟屏: 保留当前主屏。设门槛 (多于 3 个才清, 一次清一个):
        /// 实测移除显示器会触发 Windows 重排拓扑, 频繁清理反而引起主屏翻面。
        /// </summary>
        public static int RemoveStaleVirtualDisplays()
        {
            EnsureStarted();
            var primary = DisplayService.GetPrimary();
            string keep = primary?.DeviceName;
            var displays = VddDisplay.GetParsecDisplays();
            int removed = 0;
            foreach (var d in displays)
            {
                if (keep != null && string.Equals(d.DeviceName, keep, StringComparison.OrdinalIgnoreCase))
                    continue;   // 主屏留着
                try
                {
                    if (VddController.RemoveDisplay(d.DriverIndex)) removed++;
                }
                catch { }
            }
            // 驱动层清扫幽灵屏: 保留主屏对应的驱动索引
            int? keepIdx = VddDisplay.GetParsecDisplays()
                .Where(x => keep != null && string.Equals(x.DeviceName, keep, StringComparison.OrdinalIgnoreCase))
                .Select(x => x.DriverIndex).FirstOrDefault();
            if (keepIdx == null || keepIdx < 0) keepIdx = -1;
            var handleReady = VddController.WaitForReady(2000);
            if (handleReady)
            {
                var h = VddController.HandleRef;
                for (int idx = 0; idx < 16; idx++)
                {
                    if (idx == keepIdx) continue;
                    try { if (VddDriver.RemoveDisplay(h, idx)) removed++; } catch { }
                }
            }
            if (removed > 0) Logger.Ok($"已清理 {removed} 个多余的虚拟屏/幽灵屏 (保留主屏)");
            return removed;
        }
    }
}
