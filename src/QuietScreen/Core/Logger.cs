using System;
using System.Collections.Generic;
using System.IO;

namespace QuietScreen.Core
{
    /// <summary>文件 + 内存环形缓冲日志, UI 可订阅 LogAdded 实时刷新</summary>
    public static class Logger
    {
        private static readonly object Gate = new object();
        private static string _logFile;
        private const int RingSize = 500;
        private static readonly List<string> Ring = new List<string>();

        public static event Action<string> LogAdded;

        public static string LogFile => _logFile ?? "";

        public static void Init()
        {
            _logFile = Path.Combine(AppConfig.ConfigDir, "logs", "quietscreen.log");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_logFile));
                // 带 BOM 的 UTF-8, 记事本/PowerShell 打开不乱码
                if (!File.Exists(_logFile))
                    File.WriteAllText(_logFile, "", new System.Text.UTF8Encoding(true));
            }
            catch { }
        }

        public static void Info(string msg) => Write("INFO", msg);
        public static void Ok(string msg) => Write("OK", msg);
        public static void Warn(string msg) => Write("WARN", msg);
        public static void Error(string msg) => Write("ERROR", msg);

        public static IReadOnlyList<string> Snapshot()
        {
            lock (Gate) { lock (Ring) { return Ring.ToArray(); } }
        }

        private static void Write(string level, string msg)
        {
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {msg}";
            lock (Gate)
            {
                lock (Ring)
                {
                    Ring.Add(line);
                    if (Ring.Count > RingSize) Ring.RemoveAt(0);
                }
                try
                {
                    if (_logFile != null)
                        File.AppendAllText(_logFile, line + Environment.NewLine);
                }
                catch { }
            }
            LogAdded?.Invoke(line);
        }
    }
}
