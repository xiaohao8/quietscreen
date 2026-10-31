// ============================================================================
//  VddDriver - Parsec 虚拟屏驱动底层交互 (SetupAPI 枚举 / 设备句柄 / IOCTL)
//
//  移植自开源项目 nomi-san/parsec-vdd (MIT License)
//  https://github.com/nomi-san/parsec-vdd
//  Copyright (c) 2023 Nguyen Duy <wuuyi123@gmail.com>
//  本文件为 MIT 许可下的衍生作品, 许可声明见项目 LICENSE 文件。
// ============================================================================
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace QuietScreen.Core.Vdd
{
    /// <summary>驱动状态与 IOCTL 通信 (对应原项目 Device.cs + Core.cs)</summary>
    public static class VddDriver
    {
        public const string DISPLAY_ID = "PSCCDD0";
        public const string DISPLAY_NAME = "ParsecVDA";
        public const string ADAPTER_NAME = "Parsec Virtual Display Adapter";
        public const string ADAPTER_GUID_STRING = "{00b41627-04c4-429e-a26e-0265cf50c8fa}";
        public const string CLASS_GUID_STRING = "{4d36e968-e325-11ce-bfc1-08002be10318}";
        public const string HARDWARE_ID = @"Root\Parsec\VDA";

        public const int MAX_DISPLAYS = 8; // 驱动实际支持 16, 取一半避免插拔延迟

        public enum DriverStatus
        {
            OK = 0, INACCESSIBLE, UNKNOWN, UNKNOWN_PROBLEM, DISABLED,
            DRIVER_ERROR, RESTART_REQUIRED, DISABLED_SERVICE, NOT_INSTALLED
        }

        // IOCTL 控制码 (驱动 v0.45)
        private const uint IOCTL_ADD = 0x22E004;
        private const uint IOCTL_REMOVE = 0x22A008;
        private const uint IOCTL_UPDATE = 0x22A00C;
        private const uint IOCTL_VERSION = 0x22E010;

        public static bool IsValidHandle(IntPtr h) => h != IntPtr.Zero && h != new IntPtr(-1);

        // ---------------- 驱动状态查询 ----------------

        public static DriverStatus QueryStatus(out Version driverVersion)
        {
            var status = DriverStatus.INACCESSIBLE;
            driverVersion = new Version(0, 0, 0, 0);

            var guid = new Guid(CLASS_GUID_STRING);
            IntPtr devInfo = Native.SetupDiGetClassDevsW(ref guid, IntPtr.Zero, IntPtr.Zero, Native.DIGCF_PRESENT);
            if (!IsValidHandle(devInfo)) return status;

            try
            {
                status = DriverStatus.NOT_INSTALLED;
                var hwid = new byte[512];
                var devInfoData = new Native.SP_DEVINFO_DATA { cbSize = Marshal.SizeOf<Native.SP_DEVINFO_DATA>() };

                for (uint index = 0; Native.SetupDiEnumDeviceInfo(devInfo, index, ref devInfoData); index++)
                {
                    uint regType = 0;
                    if (!Native.SetupDiGetDeviceRegistryPropertyW(devInfo, ref devInfoData,
                            Native.SPDRP_HARDWAREID, out regType, hwid, hwid.Length, IntPtr.Zero))
                        continue;

                    if (regType != Native.REG_SZ && regType != Native.REG_MULTI_SZ) continue;

                    // 取第一个 NUL 结尾的字符串
                    string first = ReadFirstString(hwid);
                    if (!string.Equals(first, HARDWARE_ID, StringComparison.OrdinalIgnoreCase)) continue;

                    status = GetDeviceStatus(devInfoData.DevInst);
                    driverVersion = GetDeviceDriverVersion(devInfoData.DevInst);
                    break;
                }
            }
            finally
            {
                Native.SetupDiDestroyDeviceInfoList(devInfo);
            }
            return status;
        }

        private static string ReadFirstString(byte[] buffer)
        {
            // UTF-16: 按 NUL 字符截断 (不能按 NUL 字节, ASCII 高字节就是 0)
            string s = Encoding.Unicode.GetString(buffer);
            int nul = s.IndexOf('\0');
            if (nul > 0) s = s.Substring(0, nul);
            return s.TrimEnd('\0');
        }

        private static DriverStatus GetDeviceStatus(uint devInst)
        {
            if (Native.CM_Get_DevNode_Status(out uint devStatus, out uint problem, devInst, 0) != Native.CR_SUCCESS)
                return DriverStatus.NOT_INSTALLED;

            if ((devStatus & (Native.DN_DRIVER_LOADED | Native.DN_STARTED)) != 0)
                return DriverStatus.OK;

            if ((devStatus & Native.DN_HAS_PROBLEM) != 0)
            {
                switch (problem)
                {
                    case Native.CM_PROB_NEED_RESTART: return DriverStatus.RESTART_REQUIRED;
                    case Native.CM_PROB_DISABLED:
                    case Native.CM_PROB_HARDWARE_DISABLED: return DriverStatus.DISABLED;
                    case Native.CM_PROB_DISABLED_SERVICE: return DriverStatus.DISABLED_SERVICE;
                    default:
                        return problem == Native.CM_PROB_FAILED_POST_START
                            ? DriverStatus.DRIVER_ERROR : DriverStatus.UNKNOWN_PROBLEM;
                }
            }
            return DriverStatus.UNKNOWN;
        }

        private static Version GetDeviceDriverVersion(uint devInst)
        {
            try
            {
                var key = new Native.DEVPROPKEY
                {
                    fmtid = new Guid("{A8B865DD-2E3D-4094-AD97-E593A70C75D6}"),
                    pid = 3
                };
                var buffer = new byte[128];
                int len = buffer.Length;
                if (Native.CM_Get_DevNode_PropertyW(devInst, ref key, out uint _, buffer, ref len, 0) == Native.CR_SUCCESS)
                {
                    string v = Encoding.Unicode.GetString(buffer, 0, len).TrimEnd('\0');
                    return new Version(v);
                }
            }
            catch { }
            return new Version(0, 0, 0, 0);
        }

        // ---------------- 设备句柄 ----------------

        public static bool OpenHandle(out IntPtr handle)
        {
            handle = IntPtr.Zero;
            var interfaceGuid = new Guid(ADAPTER_GUID_STRING);
            IntPtr devInfo = Native.SetupDiGetClassDevsW(ref interfaceGuid, IntPtr.Zero, IntPtr.Zero,
                Native.DIGCF_PRESENT | Native.DIGCF_DEVICEINTERFACE);
            if (!IsValidHandle(devInfo)) return false;

            try
            {
                var devInterface = new Native.SP_DEVICE_INTERFACE_DATA
                { cbSize = Marshal.SizeOf<Native.SP_DEVICE_INTERFACE_DATA>() };

                for (uint i = 0; Native.SetupDiEnumDeviceInterfaces(devInfo, IntPtr.Zero, ref interfaceGuid, i, ref devInterface); ++i)
                {
                    // 第一次调用取所需缓冲区大小
                    if (Native.SetupDiGetDeviceInterfaceDetailW(devInfo, ref devInterface,
                            IntPtr.Zero, 0, out int detailSize, IntPtr.Zero) && detailSize <= 0)
                        continue;

                    var detail = Marshal.AllocHGlobal(detailSize);
                    try
                    {
                        // 关键: ANSI 版 cbSize=5, Unicode 版 cbSize=8 (int + 首个 WCHAR)
                        Marshal.WriteInt32(detail, 8);
                        if (Native.SetupDiGetDeviceInterfaceDetailW(devInfo, ref devInterface,
                                detail, detailSize, out _, IntPtr.Zero))
                        {
                            // DevicePath 从 cbSize(4) 之后开始, 以 NUL 结尾
                            string path = Marshal.PtrToStringUni(detail + 4);
                            if (!string.IsNullOrEmpty(path))
                            {
                                handle = Native.CreateFileW(path,
                                    Native.GENERIC_READ | Native.GENERIC_WRITE,
                                    Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE,
                                    IntPtr.Zero, Native.OPEN_EXISTING,
                                    Native.FILE_ATTRIBUTE_NORMAL | Native.FILE_FLAG_NO_BUFFERING |
                                    Native.FILE_FLAG_OVERLAPPED | Native.FILE_FLAG_WRITE_THROUGH,
                                    IntPtr.Zero);

                                if (IsValidHandle(handle)) return true;
                            }
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(detail);
                    }
                }
            }
            finally
            {
                Native.SetupDiDestroyDeviceInfoList(devInfo);
            }
            return false;
        }

        public static void CloseHandle(IntPtr handle)
        {
            if (IsValidHandle(handle)) Native.CloseHandle(handle);
        }

        // ---------------- IOCTL ----------------

        /// <summary>创建虚拟屏, 返回驱动内索引</summary>
        public static bool AddDisplay(IntPtr vdd, out int index)
        {
            if (IoControl(vdd, IOCTL_ADD, null, out int idx, 5000))
            {
                Update(vdd);
                index = idx;
                return true;
            }
            index = -1;
            return false;
        }

        /// <summary>按驱动索引移除虚拟屏</summary>
        public static bool RemoveDisplay(IntPtr vdd, int index)
        {
            // 索引按 16 位大端写入 (原实现只写低字节)
            var input = new byte[] { (byte)((index >> 8) & 0xFF), (byte)(index & 0xFF) };
            if (IoControl(vdd, IOCTL_REMOVE, input, 1000))
            {
                Update(vdd);
                return true;
            }
            return false;
        }

        /// <summary>
        /// 心跳保活: 必须 <=200ms 调用一次, 停止约 1 秒驱动看门狗会拔掉所有虚拟屏。
        /// 热路径优化: 事件句柄与输入缓冲区常驻复用 (锁串行), 消除 10 次/秒的
        /// 内核对象创建/销毁开销。
        /// </summary>
        private static readonly object HotGate = new object();
        private static IntPtr _hotEvent = IntPtr.Zero;
        private static IntPtr _hotBuffer = IntPtr.Zero;

        public static void Update(IntPtr vdd)
        {
            lock (HotGate)
            {
                if (!IsValidHandle(vdd)) return;
                if (_hotEvent == IntPtr.Zero)
                {
                    _hotBuffer = Marshal.AllocHGlobal(32);
                    for (int i = 0; i < 32; i += 8) Marshal.WriteInt64(_hotBuffer, i, 0);
                    _hotEvent = Native.CreateEventW(IntPtr.Zero, false, false, null);
                    if (_hotEvent == IntPtr.Zero)
                    {
                        Marshal.FreeHGlobal(_hotBuffer);
                        _hotBuffer = IntPtr.Zero;
                        return;
                    }
                }
                for (int i = 0; i < 32; i += 8) Marshal.WriteInt64(_hotBuffer, i, 0); // 输入清零

                var overlapped = new Native.OVERLAPPED { hEvent = _hotEvent };
                Native.DeviceIoControl(vdd, IOCTL_UPDATE, _hotBuffer, 32, IntPtr.Zero, 0, IntPtr.Zero, ref overlapped);
                if (!Native.GetOverlappedResultEx(vdd, ref overlapped, out _, 1000, false))
                {
                    Native.CancelIoEx(vdd, ref overlapped);
                    Native.GetOverlappedResult(vdd, ref overlapped, out _, true);
                }
            }
        }

        public static bool GetVersion(IntPtr vdd, out string version)
        {
            if (IoControl(vdd, IOCTL_VERSION, null, out int vernum, 100))
            {
                version = $"{(vernum >> 16) & 0xFFFF}.{vernum & 0xFFFF}";
                return true;
            }
            version = "(unknown)";
            return false;
        }

        /// <summary>
        /// 发送 IOCTL (32 字节输入缓冲 + OVERLAPPED 异步, 与原实现语义一致:
        /// 无论 DeviceIoControl 返回值如何都必须轮询 overlapped 结果;
        /// 等待失败时 Cancel 后阻塞取结果, 防止内核延后写入栈内存)。
        /// </summary>
        private static bool IoControl(IntPtr handle, uint code, byte[] input, int timeout)
        {
            if (!IsValidHandle(handle)) return false;
            return IoControl(handle, code, input, out _, timeout);
        }

        private static bool IoControl(IntPtr handle, uint code, byte[] input, out int result, int timeout)
        {
            result = 0;
            if (!IsValidHandle(handle)) return false;

            const int inSize = 32;
            IntPtr inBuffer = Marshal.AllocHGlobal(inSize);
            IntPtr outBuffer = IntPtr.Zero;
            var overlapped = new Native.OVERLAPPED();

            try
            {
                // 清零输入缓冲 (原实现每次都 new byte[32])
                for (int i = 0; i < inSize; i += 8) Marshal.WriteInt64(inBuffer, i, 0);
                if (input != null && input.Length > 0)
                    for (int i = 0; i < Math.Min(input.Length, inSize); i++)
                        Marshal.WriteByte(inBuffer, i, input[i]);

                bool wantResult = code == IOCTL_ADD || code == IOCTL_VERSION;
                int outSize = wantResult ? 4 : 0;
                if (wantResult) outBuffer = Marshal.AllocHGlobal(4);

                overlapped.hEvent = Native.CreateEventW(IntPtr.Zero, false, false, null);
                if (overlapped.hEvent == IntPtr.Zero) return false;

                Native.DeviceIoControl(handle, code, inBuffer, inSize,
                    outBuffer, outSize, IntPtr.Zero, ref overlapped);

                bool success = Native.GetOverlappedResultEx(handle, ref overlapped,
                    out _, timeout, false);

                if (!success)
                {
                    Native.CancelIoEx(handle, ref overlapped);
                    Native.GetOverlappedResult(handle, ref overlapped, out _, true);
                }

                if (success && wantResult)
                    result = Marshal.ReadInt32(outBuffer);

                return success;
            }
            finally
            {
                if (overlapped.hEvent != IntPtr.Zero) Native.CloseHandle(overlapped.hEvent);
                if (outBuffer != IntPtr.Zero) Marshal.FreeHGlobal(outBuffer);
                Marshal.FreeHGlobal(inBuffer);
            }
        }

        // ---------------- Win32 ----------------
        internal static class Native
        {
            public static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

            public const uint GENERIC_READ = 0x80000000;
            public const uint GENERIC_WRITE = 0x40000000;
            public const uint FILE_SHARE_READ = 0x1;
            public const uint FILE_SHARE_WRITE = 0x2;
            public const uint OPEN_EXISTING = 3;
            public const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;
            public const uint FILE_FLAG_NO_BUFFERING = 0x20000000;
            public const uint FILE_FLAG_OVERLAPPED = 0x40000000;
            public const uint FILE_FLAG_WRITE_THROUGH = 0x80000000;

            public const uint DIGCF_PRESENT = 0x2;
            public const uint DIGCF_DEVICEINTERFACE = 0x10;

            public const uint SPDRP_HARDWAREID = 0x1;
            public const uint REG_SZ = 1;
            public const uint REG_MULTI_SZ = 7;
            public const uint CR_SUCCESS = 0;

            public const uint CM_PROB_NEED_RESTART = 0x0000000E;
            public const uint CM_PROB_DISABLED = 0x00000016;
            public const uint CM_PROB_HARDWARE_DISABLED = 0x0000001D;
            public const uint CM_PROB_DISABLED_SERVICE = 0x00000020;
            public const uint CM_PROB_FAILED_POST_START = 0x0000002B;

            public const uint DN_DRIVER_LOADED = 0x00000002;
            public const uint DN_STARTED = 0x00000008;
            public const uint DN_HAS_PROBLEM = 0x00000400;

            [StructLayout(LayoutKind.Sequential)]
            public struct OVERLAPPED
            {
                public IntPtr Internal;
                public IntPtr InternalHigh;
                public IntPtr Pointer;
                public IntPtr hEvent;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct SP_DEVICE_INTERFACE_DATA
            {
                public int cbSize;
                public Guid InterfaceClassGuid;
                public uint Flags;
                public IntPtr Reserved;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct SP_DEVINFO_DATA
            {
                public int cbSize;
                public Guid ClassGuid;
                public uint DevInst;
                public IntPtr Reserved;
            }

            [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            public static extern IntPtr SetupDiGetClassDevsW(ref Guid classGuid, IntPtr enumerator, IntPtr hwndParent, uint flags);

            [DllImport("setupapi.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool SetupDiEnumDeviceInfo(IntPtr deviceInfoSet, uint memberIndex, ref SP_DEVINFO_DATA deviceInfoData);

            [DllImport("setupapi.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool SetupDiGetDeviceRegistryPropertyW(
                IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData, uint property,
                out uint propertyRegDataType, byte[] propertyBuffer, int propertyBufferSize, IntPtr requiredSize);

            [DllImport("setupapi.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool SetupDiEnumDeviceInterfaces(
                IntPtr deviceInfoSet, IntPtr deviceInfoData, ref Guid interfaceClassGuid,
                uint memberIndex, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

            [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool SetupDiGetDeviceInterfaceDetailW(
                IntPtr deviceInfoSet, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData,
                IntPtr deviceInterfaceDetailData, int deviceInterfaceDetailDataSize,
                out int requiredSize, IntPtr deviceInfoData);

            [DllImport("setupapi.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            public static extern IntPtr CreateFileW(
                string lpFileName, uint dwDesiredAccess, uint dwShareMode,
                IntPtr lpSecurityAttributes, uint dwCreationDisposition,
                uint dwFlagsAndAttributes, IntPtr hTemplateFile);

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            public static extern IntPtr CreateEventW(IntPtr lpEventAttributes, bool bManualReset, bool bInitialState, string lpName);

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool CloseHandle(IntPtr handle);

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool DeviceIoControl(
                IntPtr hDevice, uint ioControlCode,
                IntPtr lpInBuffer, int nInBufferSize,
                IntPtr lpOutBuffer, int nOutBufferSize,
                IntPtr lpBytesReturned, ref OVERLAPPED lpOverlapped);

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool GetOverlappedResultEx(
                IntPtr hDevice, ref OVERLAPPED lpOverlapped,
                out uint lpNumberOfBytesTransferred, int dwMilliseconds, bool bAlertable);

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool GetOverlappedResult(
                IntPtr hDevice, ref OVERLAPPED lpOverlapped,
                out uint lpNumberOfBytesTransferred, bool bWait);

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool CancelIoEx(IntPtr hDevice, ref OVERLAPPED lpOverlapped);

            [DllImport("cfgmgr32.dll")]
            public static extern uint CM_Get_DevNode_Status(
                out uint pulStatus, out uint pulProblemNumber, uint dnDevInst, uint ulFlags);

            [StructLayout(LayoutKind.Sequential)]
            public struct DEVPROPKEY
            {
                public Guid fmtid;
                public uint pid;
            }

            [DllImport("cfgmgr32.dll")]
            public static extern uint CM_Get_DevNode_PropertyW(
                uint dnDevInst, ref DEVPROPKEY propertyKey, out uint propertyType,
                byte[] propertyBuffer, ref int propertyBufferSize, uint ulFlags);
        }
    }
}
