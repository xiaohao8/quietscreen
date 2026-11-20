using System;
using Windows.UI.Notifications;
using Windows.Data.Xml.Dom;

namespace QuietScreen.Core
{
    /// <summary>
    /// Windows Toast 通知。MSIX 打包后 AUMID 自动来自包标识, 开箱即用;
    /// 未打包运行时 WinRT Toast 不可用, 自动降级为托盘气泡 (由 TrayService 注册兜底),
    /// 保证"定时关机触发"等关键通知在任何运行形态下都能到达用户。
    /// </summary>
    public static class ToastService
    {
        /// <summary>托盘气泡兜底 (TrayService.Init 注册); 参数: title, body</summary>
        public static Action<string, string> Fallback;

        public static void Show(string title, string body)
        {
            try
            {
                var xml = ToastNotificationManager.GetTemplateContent(ToastTemplateType.ToastText02);
                var texts = xml.GetElementsByTagName("text");
                texts[0].AppendChild(xml.CreateTextNode(title ?? "QuietScreen"));
                texts[1].AppendChild(xml.CreateTextNode(body ?? ""));

                var toast = new ToastNotification(xml);
                GetNotifier().Show(toast);
            }
            catch (Exception ex)
            {
                Logger.Warn("Toast 不可用, 转托盘气泡: " + ex.Message);
                try { Fallback?.Invoke(title ?? "QuietScreen", body ?? ""); } catch { }
            }
        }

        private static ToastNotifier GetNotifier()
        {
            // 打包环境: 无参重载自动使用包标识
            return ToastNotificationManager.CreateToastNotifier();
        }
    }
}
