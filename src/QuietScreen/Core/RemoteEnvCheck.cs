using System;
using System.Collections.Generic;
using Microsoft.Win32;
using QuietScreen.Core.Vdd;

namespace QuietScreen.Core
{
    /// <summary>
    /// 远控环境体检 (只读): 检查"重启之后能否顺利连回"的隐藏前提。
    /// 不修改任何系统状态 — 只报告并给出修复指引。
    /// </summary>
    public static class RemoteEnvCheck
    {
        public class Item
        {
            public string Name;
            public bool Pass;
            public string Detail;
            public override string ToString()
            {
                return (Pass ? "✓ " : "⚠ ") + Name + " — " + Detail;
            }
        }

        public static List<Item> Run()
        {
            var list = new List<Item>();
            list.Add(CheckAutoLogon());
            list.Add(CheckScreenSaverLock());
            list.Add(CheckPendingReboot());
            list.Add(CheckDriver());
            list.Add(CheckAntiSleep());
            return list;
        }

        /// <summary>Windows 自动登录: 重启后不停在登录界面的前提 (息屏远控的关键前提)</summary>
        private static Item CheckAutoLogon()
        {
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon"))
                {
                    var auto = k?.GetValue("AutoAdminLogon") as string;
                    var user = k?.GetValue("DefaultUserName") as string;
                    bool on = auto == "1" && !string.IsNullOrWhiteSpace(user);
                    return new Item
                    {
                        Name = "Windows 自动登录",
                        Pass = on,
                        Detail = on
                            ? "已开启 (用户 " + user + ") — 重启后自动进桌面"
                            : "未开启 — 重启后会停在登录界面，息屏远控将无法连回桌面（可用 netplwiz 勾选自动登录，或 Sysinternals Autologon 工具设置）"
                    };
                }
            }
            catch (Exception ex)
            {
                return new Item { Name = "Windows 自动登录", Pass = false, Detail = "读取失败: " + ex.Message };
            }
        }

        /// <summary>屏保密码保护: 自动登录进桌面后可能又被屏保锁回登录界面</summary>
        private static Item CheckScreenSaverLock()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop"))
                {
                    var active = k?.GetValue("ScreenSaveActive") as string;
                    var secure = k?.GetValue("ScreenSaverIsSecure") as string;
                    bool locked = active == "1" && secure == "1";
                    return new Item
                    {
                        Name = "屏保密码锁定",
                        Pass = !locked,
                        Detail = locked
                            ? "屏保已启用密码保护 — 自动登录后可能被锁回登录界面，远控将无法输入"
                            : "未启用屏保密码 — 无锁定风险"
                    };
                }
            }
            catch (Exception ex)
            {
                return new Item { Name = "屏保密码锁定", Pass = false, Detail = "读取失败: " + ex.Message };
            }
        }

        /// <summary>待重启的 Windows 更新: 系统可能随时自动重启，远端会话会被切断</summary>
        private static Item CheckPendingReboot()
        {
            bool pending = false;
            string where = "";
            try
            {
                using (var k1 = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending"))
                {
                    if (k1 != null) { pending = true; where = "组件服务 (CBS)"; }
                }
                if (!pending)
                {
                    using (var k2 = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired"))
                    {
                        if (k2 != null) { pending = true; where = "Windows 更新"; }
                    }
                }
            }
            catch (Exception ex)
            {
                return new Item { Name = "待重启的 Windows 更新", Pass = true, Detail = "读取失败 (不影响判断): " + ex.Message };
            }
            return new Item
            {
                Name = "待重启的 Windows 更新",
                Pass = !pending,
                Detail = pending
                    ? where + " 有待重启的更新 — 系统可能随时自动重启，远端会话会被切断；建议主动择时重启"
                    : "无待重启更新"
            };
        }

        /// <summary>虚拟屏驱动状态</summary>
        private static Item CheckDriver()
        {
            try
            {
                var st = VddDriver.QueryStatus(out var ver);
                bool ok = st == VddDriver.DriverStatus.OK;
                return new Item
                {
                    Name = "虚拟屏驱动",
                    Pass = ok,
                    Detail = ok ? "正常 (v" + ver + ")" : "状态异常: " + st
                };
            }
            catch (Exception ex)
            {
                return new Item { Name = "虚拟屏驱动", Pass = false, Detail = "读取失败: " + ex.Message };
            }
        }

        /// <summary>防睡眠: 运行期系统不自动睡眠/休眠/熄屏</summary>
        private static Item CheckAntiSleep()
        {
            bool on = AppConfig.Current.PreventSleepEnabled;
            return new Item
            {
                Name = "防睡眠 (运行期)",
                Pass = on,
                Detail = on
                    ? "已启用 — 运行期间系统不会自动睡眠/休眠/熄屏"
                    : "未启用 — 代理或系统空闲时可能睡眠，远端会掉线"
            };
        }
    }
}
