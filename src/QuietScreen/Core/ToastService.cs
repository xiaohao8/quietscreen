using System;
using Windows.UI.Notifications;
using Windows.Data.Xml.Dom;

namespace QuietScreen.Core
{
    /// <summary>
    /// Windows Toast 通知。MSIX 打包后 AUMID 自动来自包标识, 开箱即用;
    /// 未打包运行时可能不可用 (降级为仅写日志)。
    /// </summary>
    public static class ToastService
    {
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
                Logger.Warn("通知发送失败 (未打包运行时属正常): " + ex.Message);
            }
        }

        private static ToastNotifier GetNotifier()
        {
            // 打包环境: 无参重载自动使用包标识
            return ToastNotificationManager.CreateToastNotifier();
        }
    }
}
