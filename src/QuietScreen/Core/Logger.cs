using System;
using System.Threading;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;

namespace QuietScreen.Core
{
    /// <summary>文件 + 内存环形缓冲日志, UI 可订阅 LogAdded 实时刷新</summary>
    public static class Logger
    {
        private static readonly object Gate = new object();
        private static string _logFile;
        private static string _fallbackFile;
        private const int RingSize = 500;
        private static readonly List<string> Ring = new List<string>();

        public static event Action<string> LogAdded;

        public static string LogFile => _logFile ?? "";

        public static void Init()
        {
            _logFile = Path.Combine(AppConfig.ConfigDir, "logs", "quietscreen.log");
            _fallbackFile = null;
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

        // 磁盘写入全部走后台队列: 调用者永不阻塞。
        // 关键教训: 日志文件被外部进程(如并发 AI 会话)短暂锁定时,
        // 同步 AppendAllText 会阻塞 → 整个程序(含输入钩子)冻结。
        private static readonly BlockingCollection<string> WriteQueue = new(20000);

        private static void Write(string level, string msg)
        {
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {msg}";
            lock (Gate)
            {
                Ring.Add(line);
                if (Ring.Count > RingSize) Ring.RemoveAt(0);
            }
            try { WriteQueue.TryAdd(line); } catch { }
            LogAdded?.Invoke(line);
        }

        static Logger()
        {
            var writer = new Thread(() =>
            {
                var batch = new List<string>(64);
                while (true)
                {
                    try
                    {
                        batch.Add(WriteQueue.Take());
                        while (WriteQueue.TryTake(out var more)) batch.Add(more);
                        try
                        {
                            if (_logFile != null)
                            {
                                // FileShare.ReadWrite: 与任何并发持有者(杀毒/远控/其他 AI 会话)共存,
                                // 默认 AppendAllLines 的共享模式会在文件被持有时写入失败
                                using (var fs = new FileStream(_logFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                                using (var w = new StreamWriter(fs, new System.Text.UTF8Encoding(false)))
                                {
                                    foreach (var line in batch) w.WriteLine(line);
                                }
                            }
                            _fallbackFile = null;
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        {
                            // 主日志文件不可写(被锁, 或被杀软/HIPS 按 exe 拒绝访问):
                            // 写备用文件, 主文件恢复后自动切回
                            _fallbackFile ??= _logFile + ".alt";
                            try { File.AppendAllLines(_fallbackFile, batch.ToArray()); } catch { }
                        }
                        batch.Clear();
                    }
                    catch { }
                }
            })
            { IsBackground = true, Name = "QuietLogWriter" };
            writer.Start();
        }
    }
}
