using System;
using System.Collections.Generic;

namespace XFiles.Controls
{
    /// <summary>
    /// Windowed speed + ETA estimation from byte-progress samples. Pure math, no UI —
    /// unit-testable on desktop.
    /// </summary>
    public sealed class TransferStats
    {
        private const double WindowSeconds = 4.0;
        private readonly List<(double Sec, long Bytes)> _samples = new List<(double, long)>();
        private double _lastSec;
        private long _lastBytes;

        /// <summary>
        /// Records a (elapsedSeconds, bytesCopied) observation. Old samples outside the
        /// sliding window are dropped. Values are clamped to be monotonic.
        /// </summary>
        public void Sample(double nowSec, long bytesCopied)
        {
            if (nowSec < _lastSec) nowSec = _lastSec;
            if (bytesCopied < _lastBytes) bytesCopied = _lastBytes;
            _lastSec = nowSec;
            _lastBytes = bytesCopied;

            _samples.Add((nowSec, bytesCopied));
            _samples.RemoveAll(s => nowSec - s.Sec > WindowSeconds);
        }

        /// <summary>
        /// Bytes/second computed from the first and last samples within the requested
        /// window (default 4s). Returns 0 when there is not enough data. A longer
        /// window smooths burst noise (the chart uses ~2s, the ETA/number uses 4s).
        /// </summary>
        public double SpeedBytesPerSecond(double windowSeconds = WindowSeconds)
        {
            if (_samples.Count < 2) return 0;
            var last = _samples[_samples.Count - 1];
            double cutoff = last.Sec - windowSeconds;
            int firstIdx = _samples.Count - 1;
            while (firstIdx > 0 && _samples[firstIdx - 1].Sec >= cutoff) firstIdx--;
            var first = _samples[firstIdx];
            double dt = last.Sec - first.Sec;
            double db = last.Bytes - first.Bytes;
            if (dt <= 0.05 || db <= 0) return 0;
            return db / dt;
        }

        /// <summary>
        /// Instantaneous speed from the LAST two samples only (delta bytes / delta
        /// time). This reflects real throughput fluctuations; the windowed average is
        /// smoother but flattens the chart into a near-constant line.
        /// </summary>
        public double IntervalSpeedBytesPerSecond()
        {
            if (_samples.Count < 2) return 0;
            var a = _samples[_samples.Count - 2];
            var b = _samples[_samples.Count - 1];
            double dt = b.Sec - a.Sec;
            double db = b.Bytes - a.Bytes;
            if (dt <= 0.05 || db <= 0) return 0;
            return db / dt;
        }

        /// <summary>
        /// Estimated seconds remaining. Returns -1 when speed is unknown (<= 0).
        /// </summary>
        public double EtaSeconds(long totalBytes, long bytesCopied)
        {
            double speed = SpeedBytesPerSecond();
            if (speed <= 0) return -1;
            double remaining = Math.Max(0, totalBytes - bytesCopied);
            return remaining / speed;
        }

        public void Reset()
        {
            _samples.Clear();
            _lastSec = 0;
            _lastBytes = 0;
        }
    }
}
