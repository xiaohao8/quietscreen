using System;
using System.Runtime.InteropServices;

namespace QuietScreen.Core
{
    /// <summary>空闲工作集修剪: 托盘常驻应用把暂时不用的内存页还给系统</summary>
    public static class MemoryTrimmer
    {
        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetProcessWorkingSetSize(IntPtr hProcess, IntPtr dwMinimumWorkingSetSize, IntPtr dwMaximumWorkingSetSize);

        /// <summary>(-1, -1) = 把当前工作集整体换出, 任务管理器内存立即下降; 访问时按需换回</summary>
        public static void Trim()
        {
            try
            {
                SetProcessWorkingSetSize(GetCurrentProcess(), new IntPtr(-1), new IntPtr(-1));
            }
            catch { }
        }
    }
}
