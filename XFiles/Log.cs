using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Windows.Storage;

namespace XFiles
{
    public static class Log
    {
        private static Logger _logger;
        public static Logger Logger => _logger;
        public static ScreenLogger Screen { get; private set; }

        public static void Init()
        {
            string logsDir = Path.Combine(
                ApplicationData.Current.LocalFolder.Path, "logs");
            Directory.CreateDirectory(logsDir);

            string logPath = Path.Combine(logsDir, "xfiles-.log");

            Screen = new ScreenLogger();

            _logger = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .Enrich.With<CallerEnricher>()
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

        public static void Verbose(string message, params object[] args)
            => _logger?.Verbose(message, args);

        public static void Debug(string message, params object[] args)
            => _logger?.Debug(message, args);

        public static void Information(string message, params object[] args)
            => _logger?.Information(message, args);

        public static void Warning(string message, params object[] args)
            => _logger?.Warning(message, args);

        public static void Error(string message, Exception ex = null, params object[] args)
        {
            if (ex != null)
                _logger?.Error(ex, message, args);
            else
                _logger?.Error(message, args);
        }

        public static void Fatal(string message, Exception ex = null, params object[] args)
        {
            if (ex != null)
                _logger?.Fatal(ex, message, args);
            else
                _logger?.Fatal(message, args);
        }

        public static void CloseAndFlush()
        {
            _logger?.Information("Log system shutting down");
            _logger?.Dispose();
            _logger = null;
        }
    }

    /// <summary>
    /// Walks the stack to find the first non-Serilog caller method name.
    /// </summary>
    public class CallerEnricher : ILogEventEnricher
    {
        public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
        {
            string caller = "Unknown";
            try
            {
                var trace = new StackTrace();
                foreach (var frame in trace.GetFrames())
                {
                    var method = frame?.GetMethod();
                    if (method == null) continue;
                    var declaringType = method.DeclaringType;
                    var typeName = declaringType?.FullName ?? "";
                    // Skip Serilog internals and this Log wrapper class
                    if (typeName.StartsWith("Serilog") || typeName == "XFiles.Log" || typeName == "XFiles.CallerEnricher") continue;
                    caller = method.DeclaringType?.Name + "." + method.Name;
                    break;
                }
            }
            catch { }
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("Caller", caller));
        }
    }
}
