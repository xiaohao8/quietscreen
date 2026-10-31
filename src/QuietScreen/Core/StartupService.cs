using System;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Microsoft.Win32;

namespace QuietScreen.Core
{
    /// <summary>
    /// 开机自启动。
    /// MSIX 打包: 使用系统的 StartupTask 扩展 (任务管理器 > 启动应用 可管理);
    /// 未打包运行: 退回 HKCU Run 注册表。
    /// </summary>
    public static class StartupService
    {
        public const string TaskId = "QuietScreenStartup";
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValue = "QuietScreen";

        public static bool IsPackaged
        {
            get
            {
                try { return Package.Current != null; }
                catch { return false; }
            }
        }

        /// <summary>返回描述当前自启状态的文本</summary>
        public static async Task<string> GetStatusTextAsync()
        {
            if (IsPackaged)
            {
                try
                {
                    var task = await StartupTask.GetAsync(TaskId);
                    switch (task.State)
                    {
                        case StartupTaskState.Enabled: return "已开启 (系统启动项)";
                        case StartupTaskState.Disabled: return "未开启";
                        case StartupTaskState.DisabledByUser: return "已被用户在任务管理器禁用";
                        case StartupTaskState.DisabledByPolicy: return "已被组策略禁用";
                        case StartupTaskState.EnabledByPolicy: return "已由组策略开启";
                        default: return task.State.ToString();
                    }
                }
                catch (Exception ex)
                {
                    return "未知 (" + ex.Message + ")";
                }
            }

            using (var key = Registry.CurrentUser.OpenSubKey(RunKey, false))
            {
                return key?.GetValue(RunValue) != null ? "已开启 (注册表)" : "未开启";
            }
        }

        /// <summary>请求开启自启, 返回是否成功</summary>
        public static async Task<Tuple<bool, string>> EnableAsync()
        {
            if (IsPackaged)
            {
                try
                {
                    var task = await StartupTask.GetAsync(TaskId);
                    if (task.State == StartupTaskState.Enabled)
                        return Tuple.Create(true, "已经是开启状态");
                    if (task.State == StartupTaskState.DisabledByUser)
                        return Tuple.Create(false, "需先在 任务管理器>启动应用 或 系统设置 中重新启用");

                    var newState = await task.RequestEnableAsync();
                    return Tuple.Create(newState == StartupTaskState.Enabled,
                        newState == StartupTaskState.Enabled ? "已开启" : "系统拒绝了请求");
                }
                catch (Exception ex)
                {
                    return Tuple.Create(false, "失败: " + ex.Message);
                }
            }

            try
            {
                string exe = Environment.ProcessPath;
                using (var key = Registry.CurrentUser.CreateSubKey(RunKey, true))
                {
                    key.SetValue(RunValue, $"\"{exe}\" --minimized");
                }
                return Tuple.Create(true, "已开启");
            }
            catch (Exception ex)
            {
                return Tuple.Create(false, "失败: " + ex.Message);
            }
        }

        public static Task<Tuple<bool, string>> DisableAsync()
        {
            if (IsPackaged)
            {
                // WinRT StartupTask 没有提供应用侧关闭接口, 由系统"启动应用"页管理
                return Task.FromResult(Tuple.Create(false,
                    "请在 系统设置>应用>启动 或 任务管理器>启动应用 中关闭"));
            }

            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    key?.DeleteValue(RunValue, false);
                }
                return Task.FromResult(Tuple.Create(true, "已关闭"));
            }
            catch (Exception ex)
            {
                return Task.FromResult(Tuple.Create(false, "失败: " + ex.Message));
            }
        }
    }
}
