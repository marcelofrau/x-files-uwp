using System;

namespace XFiles.FileSystem
{
    /// <summary>
    /// Pure decision logic for the pre-operation free-space check. No P/Invoke, no UWP —
    /// unit-testable on desktop. The P/Invoke volume query lives in FileOperations.
    /// </summary>
    public static class DiskSpaceGuard
    {
        /// <summary>
        /// True when the destination volume does not have enough free bytes for the
        /// operation and the user should be warned before continuing.
        /// </summary>
        public static bool IsInsufficient(long freeBytes, long requiredBytes)
        {
            return requiredBytes > 0 && freeBytes >= 0 && freeBytes < requiredBytes;
        }

        /// <summary>
        /// Human-readable warning message listing required vs free sizes.
        /// </summary>
        public static string BuildWarning(long freeBytes, long requiredBytes)
        {
            return $"Not enough free space: need {Formatting.FormatSize(requiredBytes)}, " +
                   $"only {Formatting.FormatSize(freeBytes)} free on the destination.";
        }
    }
}
