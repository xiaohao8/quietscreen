using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace QuietScreen.Core
{
    /// <summary>
    /// 全局热键 (随程序运行, 托盘常驻时始终有效):
    ///   Ctrl+Esc      → 点亮笔记本内屏 (仅电脑屏幕)
    ///   Ctrl+Alt+Esc  → 切回虚拟屏息屏 (仅第二屏幕)
    ///
    /// Ctrl+Esc 在 Windows 上被系统占用, RegisterHotKey 经常注册不上;
    /// 注册失败时自动降级为低级键盘钩子 (WH_KEYBOARD_LL) 拦截,
    /// 远控客户端注入的按键同样能被捕获。
    /// 注意: 注册后 Ctrl+Esc 不再打开开始菜单, 开始菜单用 Win 键。
    /// </summary>
    public static class HotkeyService
    {
        private const int WM_HOTKEY = 0x0312;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const uint MOD_ALT = 0x1;
        private const uint MOD_CONTROL = 0x2;
        private const uint MOD_NOREPEAT = 0x4000;
        private const uint VK_ESCAPE = 0x1B;
        private const int VK_CONTROL = 0x11;
        private const int VK_MENU = 0x12;
        private const int VK_LWIN = 0x5B;
        private const int VK_RWIN = 0x5C;

        private const int ID_INTERNAL = 1; // Ctrl+Esc
        private const int ID_EXTERNAL = 2; // Ctrl+Alt+Esc

        private static IntPtr _hwnd;
        private static HwndSource _source;

        // 动作队列: LL 钩子回调必须微秒级返回, 重活由专职线程执行
        internal enum Hotkey { Local, External }
        private static readonly System.Collections.Concurrent.BlockingCollection<Hotkey> ActionQueue =
            new System.Collections.Concurrent.BlockingCollection<Hotkey>(64);
        private static Thread _actionWorker;

        private static IntPtr _hook;
        private static LowLevelKeyboardProc _hookProc;
        private const int WH_KEYBOARD_LL = 13;

        public static bool InternalRegistered { get; private set; }
        public static bool ExternalRegistered { get; private set; }
        public static bool HookInstalled { get; private set; }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        public static void Register(Window window)
        {
            if (!AppConfig.Current.HotkeyEnabled)
            {
                Logger.Info("全局热键已在设置中关闭");
                return;
            }
            try
            {
                var helper = new WindowInteropHelper(window);
                _hwnd = helper.EnsureHandle();
                _source = HwndSource.FromHwnd(_hwnd);
                _source?.AddHook(WndProc);

                InternalRegistered = RegisterHotKey(_hwnd, ID_INTERNAL, MOD_CONTROL | MOD_NOREPEAT, VK_ESCAPE);
                ExternalRegistered = RegisterHotKey(_hwnd, ID_EXTERNAL, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, VK_ESCAPE);
                StartActionWorker();

                // Ctrl+Esc 被系统占用时降级为键盘钩子 (装在主线程, WPF 消息泵即可驱动)
                if (!InternalRegistered) InstallHook();

                Logger.Ok($"全局热键: Ctrl+Esc=点亮内屏 {(InternalRegistered ? "√" : (HookInstalled ? "√(键盘钩子)" : "×(被占用)"))}, " +
                          $"Ctrl+Alt+Esc=切回息屏 {(ExternalRegistered ? "√" : "×")}");
            }
            catch (Exception ex)
            {
                Logger.Error("热键注册失败: " + ex.Message);
            }
        }

        public static void Unregister()
        {
            try
            {
                if (_hwnd != IntPtr.Zero)
                {
                    if (InternalRegistered) UnregisterHotKey(_hwnd, ID_INTERNAL);
                    if (ExternalRegistered) UnregisterHotKey(_hwnd, ID_EXTERNAL);
                }
                if (_hook != IntPtr.Zero) UnhookWindowsHookEx(_hook);
            }
            catch { }
            _hook = IntPtr.Zero;
            InternalRegistered = ExternalRegistered = HookInstalled = false;
        }

        // ---------------- RegisterHotKey 消息路径 ----------------

        private static void StartActionWorker()
        {
            if (_actionWorker != null) return;
            _actionWorker = new Thread(() =>
            {
                foreach (var action in ActionQueue.GetConsumingEnumerable())
                {
                    try { DispatchAction(action == Hotkey.Local ? ID_INTERNAL : ID_EXTERNAL); }
                    catch (Exception ex) { Logger.Error("热键动作执行失败: " + ex.Message); }
                }
            })
            { IsBackground = true, Name = "QuietHotkeyActions" };
            _actionWorker.Start();
        }

        private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg != WM_HOTKEY) return IntPtr.Zero;
            handled = true;
            int id = wParam.ToInt32();
            ActionQueue.Add(id == ID_INTERNAL ? Hotkey.Local : Hotkey.External);
            return IntPtr.Zero;
        }

        // ---------------- 键盘钩子路径 (Ctrl+Esc 兜底) ----------------

        private static void InstallHook()
        {
            if (HookInstalled) return;
            _hookProc = HookCallback;
            _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc, GetModuleHandle(null), 0);
            if (_hook == IntPtr.Zero)
            {
                Logger.Error("键盘钩子安装失败: " + Marshal.GetLastWin32Error());
                return;
            }
            HookInstalled = true;
        }

        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
            {
                var kb = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT));
                bool ctrl = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
                bool alt = (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;
                bool win = ((GetAsyncKeyState(VK_LWIN) | GetAsyncKeyState(VK_RWIN)) & 0x8000) != 0;

                if (kb.vkCode == VK_ESCAPE && ctrl && !alt && !win)
                {
                    // Ctrl+Esc → 点亮内屏, 吞掉按键 (不打开开始菜单)
                    InternalRegistered = true;
                    ActionQueue.Add(Hotkey.Local);   // 钩子回调只入队, 微秒级返回
                    return new IntPtr(1);
                }
            }
            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        // ---------------- 动作 ----------------

        private static void DispatchAction(int id)
        {
            var app = Application.Current;
            if (app == null) return;
            app.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    if (id == ID_INTERNAL)
                        GuardianService.Instance.SetLocal("热键 Ctrl+Esc");
                    else if (id == ID_EXTERNAL)
                        GuardianService.Instance.SetNight("热键 Ctrl+Alt+Esc");
                }
                catch (Exception ex)
                {
                    Logger.Error("热键动作失败: " + ex.Message);
                }
            }));
        }
    }
}
