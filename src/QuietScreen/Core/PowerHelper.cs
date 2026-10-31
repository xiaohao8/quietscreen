using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace QuietScreen.Core
{
    /// <summary>
    /// 防睡眠: 程序运行期间用 SetThreadExecutionState 声明"系统需要保持工作状态",
    /// 阻止空闲睡眠/休眠/屏幕超时熄灭。程序退出后状态自动消失,
    /// 不像 powercfg 那样永久改动系统设置。
    ///
    /// 注意: SetThreadExecutionState 的状态是"按线程"生效的,
    /// 必须由同一个常驻线程持有和清除, 所以用单线程 + 事件驱动。
    /// </summary>
    public static class PowerHelper
    {
        [Flags]
        private enum EXECUTION_STATE : uint
        {
            ES_CONTINUOUS = 0x80000000,
            ES_SYSTEM_REQUIRED = 0x00000001,   // 阻止系统睡眠/休眠
            ES_DISPLAY_REQUIRED = 0x00000002,  // 阻止显示器超时熄灭 (虚拟屏远控不掉线)
        }

        private static readonly object Gate = new object();
        private static Thread _holder;
        private static AutoResetEvent _kick;
        private static volatile bool _wantAwake;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern EXECUTION_STATE SetThreadExecutionState(EXECUTION_STATE esFlags);

        public static void SetKeepAwake(bool on)
        {
            lock (Gate)
            {
                if (_wantAwake == on && _holder != null) return;
                _wantAwake = on;
                if (_holder == null)
                {
                    _kick = new AutoResetEvent(false);
                    _holder = new Thread(HolderLoop)
                    {
                        IsBackground = true,
                        Name = "VddKeepAwake"
                    };
                    _holder.Start();
                }
                _kick.Set();
            }
        }

        private static void HolderLoop()
        {
            bool applied = false;
            while (true)
            {
                if (_wantAwake)
                {
                    SetThreadExecutionState(
                        EXECUTION_STATE.ES_CONTINUOUS |
                        EXECUTION_STATE.ES_SYSTEM_REQUIRED |
                        EXECUTION_STATE.ES_DISPLAY_REQUIRED);
                    if (!applied) { Logger.Ok("防睡眠已启用: 运行期间系统不会自动睡眠/休眠/熄屏"); applied = true; }
                }
                else
                {
                    SetThreadExecutionState(EXECUTION_STATE.ES_CONTINUOUS);
                    if (applied) { Logger.Info("防睡眠已关闭"); applied = false; }
                }
                _kick.WaitOne();
            }
        }
    }
}
