using System;

namespace XFiles
{
    /// <summary>
    /// Test-side stub for XFiles.Log (Serilog static facade).
    /// Allows linking UWP source files that reference Log without pulling in Serilog.
    /// </summary>
    public static class Log
    {
        public static void Verb(string message, params object[] args) { }
        public static void Dbg(string message, params object[] args) { }
        public static void Info(string message, params object[] args) { }
        public static void Warn(string message, params object[] args) { }
        public static void Warn(string message, Exception ex, params object[] args) { }
        public static void Err(string message, Exception ex = null, params object[] args) { }
    }
}
