using System;
using System.Diagnostics;
using System.IO;
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

        private static string GetCaller()
        {
            try
            {
                var trace = new StackTrace(2, false);
                var frames = trace.GetFrames();
                if (frames != null)
                {
                    foreach (var frame in frames)
                    {
                        var method = frame?.GetMethod();
                        if (method == null) continue;
                        var typeName = method.DeclaringType?.FullName ?? "";
                        if (typeName.StartsWith("Serilog") || typeName == "XFiles.Log") continue;
                        return $"{method.DeclaringType?.Name}.{method.Name}";
                    }
                }
            }
            catch { }
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
