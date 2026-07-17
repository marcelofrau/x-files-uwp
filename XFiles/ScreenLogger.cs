using System;
using System.Collections.Generic;
using Serilog.Core;
using Serilog.Events;

namespace XFiles
{
    public class ScreenLogger : ILogEventSink
    {
        private const int MaxLines = 80;
        private readonly object _lock = new object();
        private readonly Queue<string> _lines = new Queue<string>();

        public event Action<string> OnLogLine;

        public void Emit(LogEvent logEvent)
        {
            var ts = logEvent.Timestamp.ToString("HH:mm:ss.fff");
            var level = GetShortLevel(logEvent.Level);
            var caller = logEvent.Properties.TryGetValue("Caller", out var c) ? c.ToString().Trim('"') : "?";
            var msg = logEvent.RenderMessage();
            var line = $"[{ts} {level}] [{caller}] {msg}";

            if (logEvent.Exception != null)
                line += $"\n  {logEvent.Exception.Message}";

            lock (_lock)
            {
                _lines.Enqueue(line);
                while (_lines.Count > MaxLines)
                    _lines.Dequeue();
            }

            OnLogLine?.Invoke(line);
        }

        public string[] GetLines()
        {
            lock (_lock)
                return _lines.ToArray();
        }

        private static string GetShortLevel(LogEventLevel level)
        {
            switch (level)
            {
                case LogEventLevel.Verbose: return "VRB";
                case LogEventLevel.Debug: return "DBG";
                case LogEventLevel.Information: return "INF";
                case LogEventLevel.Warning: return "WRN";
                case LogEventLevel.Error: return "ERR";
                case LogEventLevel.Fatal: return "FTL";
                default: return "???";
            }
        }
    }
}
