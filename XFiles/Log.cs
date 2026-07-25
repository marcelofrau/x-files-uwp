using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using Serilog;
using Serilog.Context;
using Serilog.Core;
using Serilog.Events;
using Windows.Storage;

namespace XFiles
{
    public static class Log
    {
        private static Logger _logger;
        private static LoggingLevelSwitch _levelSwitch;
        public static Logger Logger => _logger;
        public static ScreenLogger Screen { get; private set; }

        public static void Init()
        {
            string logsDir = Path.Combine(
                ApplicationData.Current.LocalFolder.Path, "logs");
            Directory.CreateDirectory(logsDir);

            string logPath = Path.Combine(logsDir, "xfiles-.log");

            Screen = new ScreenLogger();
            _levelSwitch = new LoggingLevelSwitch(LogEventLevel.Information);

            _logger = new LoggerConfiguration()
                .MinimumLevel.ControlledBy(_levelSwitch)
                .WriteTo.Sink(Screen)
                .WriteTo.Debug(
                    outputTemplate: "[{Timestamp:HH:mm:ss.fff} {Level:u3}] [{Caller}] {Message:lj}{NewLine}{Exception}")
                .WriteTo.File(
                    logPath,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 5,
                    outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] [{Caller}] {Message:lj}{NewLine}{Exception}",
                    shared: true)
                .CreateLogger();

            _logger.Information("Log system initialized. Directory: {LogsDir}", logsDir);
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

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string GetCaller()
        {
            try
            {
                var frame = new StackFrame(2, false);
                var method = frame.GetMethod();
                if (method != null)
                {
                    var typeName = method.DeclaringType?.Name ?? "";
                    if (!string.IsNullOrEmpty(typeName) && typeName != "Log")
                        return $"{typeName}.{method.Name}";
                }

                var trace = new StackTrace(2, false);
                var frames = trace.GetFrames();
                if (frames != null)
                {
                    foreach (var f in frames)
                    {
                        var m = f?.GetMethod();
                        if (m == null) continue;
                        var tn = m.DeclaringType?.FullName ?? "";
                        if (tn.StartsWith("Serilog") || tn == "XFiles.Log") continue;
                        var cn = m.DeclaringType?.Name;
                        if (string.IsNullOrEmpty(cn)) continue;
                        return $"{cn}.{m.Name}";
                    }
                }
            }
            catch (Exception ex)
            {
                return $"Error:{ex.Message}";
            }
            return "Unknown";
        }

        public static void Verbose(string message, params object[] args)
        {
            if (_logger == null) return;
            using (LogContext.PushProperty("Caller", GetCaller()))
                _logger.Verbose(message, args);
        }

        public static void Debug(string message, params object[] args)
        {
            if (_logger == null) return;
            using (LogContext.PushProperty("Caller", GetCaller()))
                _logger.Debug(message, args);
        }

        public static void Information(string message, params object[] args)
        {
            if (_logger == null) return;
            using (LogContext.PushProperty("Caller", GetCaller()))
                _logger.Information(message, args);
        }

        public static void Warning(string message, params object[] args)
        {
            if (_logger == null) return;
            using (LogContext.PushProperty("Caller", GetCaller()))
                _logger.Warning(message, args);
        }

        public static void Warning(string message, Exception ex, params object[] args)
        {
            if (_logger == null) return;
            using (LogContext.PushProperty("Caller", GetCaller()))
                _logger.Warning(ex, message, args);
        }

        public static void Error(string message, Exception ex = null, params object[] args)
        {
            if (_logger == null) return;
            using (LogContext.PushProperty("Caller", GetCaller()))
            {
                if (ex != null)
                    _logger.Error(ex, message, args);
                else
                    _logger.Error(message, args);
            }
        }

        public static void Fatal(string message, Exception ex = null, params object[] args)
        {
            if (_logger == null) return;
            using (LogContext.PushProperty("Caller", GetCaller()))
            {
                if (ex != null)
                    _logger.Fatal(ex, message, args);
                else
                    _logger.Fatal(message, args);
            }
        }

        public static void CloseAndFlush()
        {
            _logger?.Information("Log system shutting down");
            _logger?.Dispose();
            _logger = null;
        }
    }
}
