using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QuietScreen.Core
{
    /// <summary>
    /// 应用配置, 保存于 %LOCALAPPDATA%\QuietScreen\config.json
    /// (MSIX 打包后系统会自动重定向到包的 LocalCache, 读写方式不变)
    /// </summary>
    public class AppConfig
    {
        // ---- 守护 ----
        public bool GuardianEnabled { get; set; } = true;
        public int CheckIntervalSec { get; set; } = 45;
        public bool AutoCreateIfMissing { get; set; } = true;
        public bool SwitchExternal { get; set; } = true;
        public bool CleanExtraDisplays { get; set; } = true;

        // ---- 通知 ----
        public bool ToastOnChange { get; set; } = true;
        public int ToastOnOkEvery { get; set; } = 0;

        // ---- 虚拟屏驱动 ----
        /// <summary>旧版 ParsecVDisplay 路径 (仅兼容旧配置; 新版本不使用)</summary>
        public string VddExePath { get; set; } = "";
        /// <summary>虚拟屏设备名正则, 例如 \\.\DISPLAY29</summary>
        public string VirtualDisplayPattern { get; set; } = @"^DISPLAY([2-9]\d)$";

        // ---- 远控代理守护 (可选, 默认关闭, 由用户自行配置) ----
        public bool AgentKeepaliveEnabled { get; set; } = false;
        public string AgentExe { get; set; } = "";
        public string AgentArgs { get; set; } = "";
        public int AgentPort { get; set; } = 0;

        // ---- 自启动 ----
        public bool AutoStartEnabled { get; set; } = false;

        // ---- 夜间常驻 ----
        /// <summary>运行期间阻止系统自动睡眠/休眠/熄屏 (SetThreadExecutionState)</summary>
        public bool PreventSleepEnabled { get; set; } = true;
        /// <summary>全局热键: Ctrl+Esc 点亮内屏, Ctrl+Alt+Esc 切回息屏</summary>
        public bool HotkeyEnabled { get; set; } = true;

        // ---- 定时关机 ----
        /// <summary>每天固定时间自动关机</summary>
        public bool ShutdownDailyEnabled { get; set; } = false;
        /// <summary>每日关机时间 HH:mm</summary>
        public string ShutdownDailyTime { get; set; } = "02:00";
        /// <summary>触发前宽限秒数 (系统倒计时, 可取消)</summary>
        public int ShutdownGraceSec { get; set; } = 60;


        // ---- 旧版工具 ----
        /// <summary>自动关闭旧版 ParsecVDisplay (默认关闭, 避免误杀用户正在使用的程序)</summary>
        public bool CloseLegacyToolEnabled { get; set; } = false;

        [JsonIgnore]
        public static AppConfig Current { get; private set; } = new AppConfig();

        public static string ConfigDir
        {
            get
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "QuietScreen");
                try { Directory.CreateDirectory(dir); } catch { }
                return dir;
            }
        }

        public static string ConfigPath => Path.Combine(ConfigDir, "config.json");

        private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public static void Load()
        {
            var cfg = new AppConfig();
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var loaded = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigPath), JsonOpts);
                    if (loaded != null) cfg = loaded;
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("config.json 解析失败, 使用默认配置: " + ex.Message);
            }
            Current = cfg;
        }

        public static void Save()
        {
            try
            {
                File.WriteAllText(ConfigPath, JsonSerializer.Serialize(Current, JsonOpts));
                Logger.Info("配置已保存");
            }
            catch (Exception ex)
            {
                Logger.Error("配置保存失败: " + ex.Message);
            }
        }
    }
}
