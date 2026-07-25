using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Windows.Storage;

namespace XFiles
{
    public static class Log
    {
        private const uint GENERIC_READ = 0x80000000;
        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint FILE_SHARE_WRITE = 0x00000002;
        private const uint FILE_SHARE_DELETE = 0x00000004;
        private const uint OPEN_EXISTING = 3;
        private const uint FILE_ATTRIBUTE_NORMAL = 128;
        private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

        [DllImport("api-ms-win-core-file-fromapp-l1-1-0.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateFileFromAppW(
            string fileName, uint desiredAccess, uint shareMode,
            IntPtr securityAttributes, uint creationDisposition,
            uint flagsAndAttributes, IntPtr templateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadFile(
            IntPtr hFile, byte[] lpBuffer, uint nNumberOfBytesToRead,
            out uint lpNumberOfBytesRead, IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetFileSizeEx(IntPtr hFile, out long lpFileSize);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr hObject);

        private static Logger _logger;
        private static LoggingLevelSwitch _levelSwitch;
        private static string _currentLogFile;
        private const int MaxArchivedSessions = 10;
        private const string ActiveLogFile = "xfiles.log";

        public static Logger Logger => _logger;
        public static ScreenLogger Screen { get; private set; }

        public static void Init()
        {
            string logsDir = Path.Combine(
                ApplicationData.Current.LocalFolder.Path, "logs");
            Directory.CreateDirectory(logsDir);

            ArchivePreviousSession(logsDir);
            CleanupOldLogs(logsDir);

            _currentLogFile = Path.Combine(logsDir, ActiveLogFile);

            Screen = new ScreenLogger();
            _levelSwitch = new LoggingLevelSwitch(LogEventLevel.Information);

            _logger = new LoggerConfiguration()
                .MinimumLevel.ControlledBy(_levelSwitch)
                .WriteTo.Sink(Screen)
                .WriteTo.Debug(
                    outputTemplate: "[{Timestamp:HH:mm:ss.fff} {Level:u3}] {Message:lj}{NewLine}{Exception}")
                .WriteTo.File(
                    _currentLogFile,
                    outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] {Message:lj}{NewLine}{Exception}",
                    shared: true)
                .CreateLogger();

            _logger.Information("Log system initialized. File: {File}", _currentLogFile);
        }

        private static void ArchivePreviousSession(string logsDir)
        {
            try
            {
                string activeFile = Path.Combine(logsDir, ActiveLogFile);
                if (File.Exists(activeFile) && new FileInfo(activeFile).Length > 0)
                {
                    string archiveName = $"xfiles-{DateTime.Now:yyyyMMdd-HHmmss}-prev.log";
                    string archivePath = Path.Combine(logsDir, archiveName);
                    File.Move(activeFile, archivePath);
                }

                string legacyFile = Path.Combine(logsDir, "xfiles-.log");
                if (File.Exists(legacyFile) && new FileInfo(legacyFile).Length > 0)
                {
                    string archiveName = $"xfiles-{DateTime.Now:yyyyMMdd-HHmmss}-legacy.log";
                    string archivePath = Path.Combine(logsDir, archiveName);
                    File.Move(legacyFile, archivePath);
                }
            }
            catch { }
        }

        private static void CleanupOldLogs(string logsDir)
        {
            try
            {
                var files = Directory.GetFiles(logsDir, "xfiles-*.log")
                    .OrderByDescending(f => new FileInfo(f).LastWriteTime)
                    .Skip(MaxArchivedSessions)
                    .ToArray();
                foreach (var f in files)
                {
                    try { File.Delete(f); } catch { }
                }
            }
            catch { }
        }

        public static void SetLogLevel(string level)
        {
            if (_levelSwitch == null) return;
            if (Enum.TryParse<LogEventLevel>(level, true, out var logLevel))
            {
                _levelSwitch.MinimumLevel = logLevel;
                _logger?.Information("Log level changed to {Level}", logLevel);
            }
        }

        public static string GetCurrentLevel()
        {
            return _levelSwitch?.MinimumLevel.ToString() ?? "Information";
        }

        public static void Verb(string message, params object[] args)
        {
            if (_logger == null) return;
            _logger.Verbose(message, args);
        }

        public static void Dbg(string message, params object[] args)
        {
            if (_logger == null) return;
            _logger.Debug(message, args);
        }

        public static void Info(string message, params object[] args)
        {
            if (_logger == null) return;
            _logger.Information(message, args);
        }

        public static void Warn(string message, params object[] args)
        {
            if (_logger == null) return;
            _logger.Warning(message, args);
        }

        public static void Warn(string message, Exception ex, params object[] args)
        {
            if (_logger == null) return;
            _logger.Warning(ex, message, args);
        }

        public static void Err(string message, Exception ex = null, params object[] args)
        {
            if (_logger == null) return;
            if (ex != null)
                _logger.Error(ex, message, args);
            else
                _logger.Error(message, args);
        }

        public static string GetLogsDirectory()
        {
            return Path.Combine(ApplicationData.Current.LocalFolder.Path, "logs");
        }

        public static string GetCurrentLogPath()
        {
            return _currentLogFile;
        }

        private static bool Win32FileExists(string path)
        {
            IntPtr h = CreateFileFromAppW(path, GENERIC_READ,
                FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                IntPtr.Zero, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, IntPtr.Zero);
            if (h == INVALID_HANDLE_VALUE) return false;
            CloseHandle(h);
            return true;
        }

        private static string ReadFileWin32(string path)
        {
            IntPtr hFile = CreateFileFromAppW(path, GENERIC_READ,
                FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                IntPtr.Zero, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, IntPtr.Zero);
            if (hFile == INVALID_HANDLE_VALUE)
                return $"[Could not open: {Marshal.GetLastWin32Error()}]";

            try
            {
                if (!GetFileSizeEx(hFile, out long fileSize))
                    return "[Could not get file size]";

                int size = (int)Math.Min(fileSize, 4 * 1024 * 1024);
                if (size <= 0) return string.Empty;

                byte[] buf = new byte[size];
                uint totalRead = 0;
                while (totalRead < size)
                {
                    uint chunk = (uint)(size - totalRead);
                    if (!ReadFile(hFile, buf, chunk, out uint bytesRead, IntPtr.Zero) || bytesRead == 0)
                        break;
                    totalRead += bytesRead;
                }
                return System.Text.Encoding.UTF8.GetString(buf, 0, (int)totalRead);
            }
            finally
            {
                CloseHandle(hFile);
            }
        }

        public static string GetAllLogContent()
        {
            string logPath = GetCurrentLogPath();
            if (logPath == null) return "No log files found.";
            try
            {
                return ReadFileWin32(logPath);
            }
            catch (Exception ex)
            {
                return $"[Could not read: {ex.Message}]";
            }
        }

        public static string GetAllSessionsContent()
        {
            string logsDir = GetLogsDirectory();
            if (!Directory.Exists(logsDir)) return "No logs directory found.";
            var files = Directory.GetFiles(logsDir, "xfiles*.log")
                .OrderByDescending(f => new FileInfo(f).LastWriteTime)
                .ToArray();
            if (files.Length == 0) return "No log files found.";
            Log.Info("GetAllSessionsContent: found {Count} log files in {Dir}", files.Length, logsDir);
            var sb = new System.Text.StringBuilder();
            foreach (var file in files)
            {
                string name = Path.GetFileName(file);
                Log.Info("GetAllSessionsContent: reading {File}", name);
                sb.AppendLine($"=== {name} ===");
                try
                {
                    sb.AppendLine(ReadFileWin32(file));
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"[Could not read: {ex.Message}]");
                }
                sb.AppendLine();
            }
            string result = sb.ToString();
            const int MaxShareBytes = 3 * 1024 * 1024;
            if (result.Length > MaxShareBytes)
            {
                Log.Info("GetAllSessionsContent: truncating {Total} chars to {Max} chars", result.Length, MaxShareBytes);
                result = result.Substring(0, MaxShareBytes);
            }
            return result;
        }

        public static void CloseAndFlush()
        {
            _logger?.Information("Log system shutting down");
            _logger?.Dispose();
            _logger = null;
        }
    }
}
