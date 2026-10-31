// ============================================================================
//  VddDisplay - 显示器枚举与 Parsec 虚拟屏识别
//  移植自开源项目 nomi-san/parsec-vdd (MIT License) app/Display.cs
//  https://github.com/nomi-san/parsec-vdd  Copyright (c) 2023 Nguyen Duy
// ============================================================================
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace QuietScreen.Core.Vdd
{
    public class VddDisplayInfo
    {
        public bool Active;
        public bool Primary;
        public string DeviceName;    // \\.\DISPLAY29
        public string DisplayName;   // 监视器代号, Parsec 虚拟屏为 PSCCDD0
        public int Address;          // 设备 UID
        public int Width, Height, Hz;

        /// <summary>驱动内部索引 (IOCTL_REMOVE 用)</summary>
        public int DriverIndex => Address - 0x100;
        public bool IsParsec => string.Equals(DisplayName, VddDriver.DISPLAY_ID, StringComparison.OrdinalIgnoreCase);
        public string Resolution => Width > 0 ? $"{Width}x{Height}" : "";
    }

    /// <summary>GDI 显示器枚举 (两级 EnumDisplayDevices + UID 解析)</summary>
    public static class VddDisplay
    {
        public static List<VddDisplayInfo> GetAllDisplays()
        {
            var map = new Dictionary<string, VddDisplayInfo>(StringComparer.OrdinalIgnoreCase);
            var order = new List<VddDisplayInfo>();

            var dd = new Native.DISPLAY_DEVICE { cb = Marshal.SizeOf<Native.DISPLAY_DEVICE>() };
            for (int i = 0; Native.EnumDisplayDevices(null, i, ref dd, 0); i++)
            {
                bool adapterPrimary = (dd.StateFlags & Native.DISPLAY_DEVICE_PRIMARY_DEVICE) != 0;
                var dd2 = new Native.DISPLAY_DEVICE { cb = Marshal.SizeOf<Native.DISPLAY_DEVICE>() };

                for (int j = 0; Native.EnumDisplayDevices(dd.DeviceName, j, ref dd2, Native.EDD_GET_DEVICE_INTERFACE_NAME); j++)
                {
                    if ((dd2.StateFlags & Native.DISPLAY_DEVICE_ATTACHED) == 0) continue;
                    if (!TryParseInstanceId(dd2.DeviceID, out var instanceId)) continue;
                    if (map.ContainsKey(instanceId)) continue;

                    var info = new VddDisplayInfo
                    {
                        Active = (dd2.StateFlags & Native.DISPLAY_DEVICE_ACTIVE) != 0,
                        Primary = adapterPrimary,
                        DeviceName = dd.DeviceName,
                        DisplayName = ParseDisplayCode(dd2.DeviceID),
                        Address = ParseDisplayAddress(dd2.DeviceID),
                    };

                    if (info.Active)
                    {
                        FetchCurrentMode(info.DeviceName, out var w, out var h, out var hz);
                        info.Width = w; info.Height = h; info.Hz = hz;
                    }

                    map.Add(instanceId, info);
                    order.Add(info);
                }
            }

            // DISPLAY10 应排在 DISPLAY2 之后: 按名称数字自然排序
            order.Sort((a, b) =>
            {
                int na = NameNumber(a.DeviceName), nb = NameNumber(b.DeviceName);
                if (na != nb) return na.CompareTo(nb);
                return string.CompareOrdinal(a.DeviceName, b.DeviceName);
            });
            return order;
        }

        /// <summary>所有活动的 Parsec 虚拟屏</summary>
        public static List<VddDisplayInfo> GetParsecDisplays()
        {
            return GetAllDisplays().FindAll(d => d.Active && d.IsParsec);
        }

        /// <summary>\\?\DISPLAY#PSCCDD0#5&abc&UID256#{guid} → DISPLAY\PSCCDD0\5&abc&UID256</summary>
        private static bool TryParseInstanceId(string interfacePath, out string instanceId)
        {
            instanceId = null;
            if (string.IsNullOrEmpty(interfacePath)) return false;
            int start = interfacePath.StartsWith(@"\\?\") ? 4 : 0;
            int end = interfacePath.IndexOf("#{", start, StringComparison.Ordinal);
            if (end < 0) end = interfacePath.Length;
            if (end <= start) return false;
            instanceId = interfacePath.Substring(start, end - start).Replace('#', '\\');
            return true;
        }

        private static int ParseDisplayAddress(string path)
        {
            if (string.IsNullOrEmpty(path)) return 0;
            int i = path.IndexOf("UID", StringComparison.OrdinalIgnoreCase);
            if (i < 0) return 0;
            i += 3;
            int end = i;
            while (end < path.Length && path[end] >= '0' && path[end] <= '9') end++;
            int.TryParse(path.Substring(i, end - i), out int addr);
            return addr;
        }

        private static string ParseDisplayCode(string id)
        {
            var tokens = id.Split('#');
            return tokens.Length >= 2 ? tokens[1] : tokens[0];
        }

        private static int NameNumber(string deviceName)
        {
            if (string.IsNullOrEmpty(deviceName)) return 0;
            int digits = 0, scale = 1;
            for (int i = deviceName.Length - 1; i >= 0 && char.IsDigit(deviceName[i]); i--)
            {
                digits += (deviceName[i] - '0') * scale;
                scale *= 10;
            }
            return digits;
        }

        private static void FetchCurrentMode(string deviceName, out int w, out int h, out int hz)
        {
            w = h = hz = 0;
            var mode = new Native.DEVMODE { dmSize = (short)Marshal.SizeOf<Native.DEVMODE>() };
            if (Native.EnumDisplaySettings(deviceName, -1 /*ENUM_CURRENT_SETTINGS*/, ref mode))
            {
                w = mode.dmPelsWidth;
                h = mode.dmPelsHeight;
                hz = mode.dmDisplayFrequency;
            }
        }

        private static class Native
        {
            public const uint EDD_GET_DEVICE_INTERFACE_NAME = 0x1;
            public const uint DISPLAY_DEVICE_ACTIVE = 0x1;
            public const uint DISPLAY_DEVICE_ATTACHED = 0x2;
            public const uint DISPLAY_DEVICE_PRIMARY_DEVICE = 0x4;

            [DllImport("user32.dll", CharSet = CharSet.Ansi)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool EnumDisplayDevices(string lpDevice, int iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
            public struct DISPLAY_DEVICE
            {
                public int cb;
                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
                public uint StateFlags;
                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
            }

            [DllImport("user32.dll", CharSet = CharSet.Ansi)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DEVMODE devMode);

            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
            public struct DEVMODE
            {
                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
                public short dmSpecVersion;
                public short dmDriverVersion;
                public short dmSize;
                public short dmDriverExtra;
                public int dmFields;
                public int dmPositionX;
                public int dmPositionY;
                public int dmDisplayOrientation;
                public int dmDisplayFixedOutput;
                public short dmColor;
                public short dmDuplex;
                public short dmYResolution;
                public short dmTTOption;
                public short dmCollate;
                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
                public short dmLogPixels;
                public int dmBitsPerPel;
                public int dmPelsWidth;
                public int dmPelsHeight;
                public int dmDisplayFlags;
                public int dmDisplayFrequency;
                public int dmICMMethod;
                public int dmICMIntent;
                public int dmMediaType;
                public int dmDitherType;
                public int dmReserved1;
                public int dmReserved2;
                public int dmPanningWidth;
                public int dmPanningHeight;
            }
        }
    }
}
