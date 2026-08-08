using System;
using System.Diagnostics;

namespace XFiles.FileSystem
{
    /// <summary>
    /// Accumulates per-chunk transfer timing (read vs write split, min/max/avg chunk
    /// times, request-size histogram, spikes) and formats periodic + final diagnostic
    /// lines. Every chunk is timed so the final split shows whether the source read,
    /// the destination write, or app-side overhead (progress dispatch, scheduling, GC)
    /// is the bottleneck. Levels: per-chunk Verb, periodic Dbg, final Info (Dbg for
    /// small/fast transfers). Distinct from XFiles.Controls.TransferStats (UI speed +
    /// ETA estimator).
    /// </summary>
    internal sealed class TransferMeter
    {
        public const int SpikeThresholdMs = 250;

        private readonly Stopwatch _sw = new Stopwatch();
        private long _lastSampleTs;
        private long _lastSampleBytes;

        public string Source;
        public string Dest;
        public int BufferSize;

        public long TotalBytes;
        public long ChunkCount;
        public long ReadTicks;
        public long WriteTicks;
        public long SpikeCount;
        public double MinChunkMs = double.MaxValue;
        public double MaxChunkMs;
        public double SumChunkMs;

        public long RequestCount;
        public long RequestSum;
        public int MinRequest = int.MaxValue;
        public int MaxRequest;

        public void Start()
        {
            _sw.Start();
            _lastSampleTs = Stopwatch.GetTimestamp();
        }

        public double ElapsedMs => _sw.Elapsed.TotalMilliseconds;

        public double AvgMbPerSec => ElapsedMs > 0
            ? TotalBytes / (1024.0 * 1024.0) / (ElapsedMs / 1000.0)
            : 0;

        public void TrackChunk(int bytes, long readTicks, long writeTicks)
        {
            if (bytes <= 0) return;
            TotalBytes += bytes;
            ChunkCount++;
            ReadTicks += readTicks;
            WriteTicks += writeTicks;
            double chunkMs = (readTicks + writeTicks) * 1000.0 / Stopwatch.Frequency;
            SumChunkMs += chunkMs;
            if (chunkMs < MinChunkMs) MinChunkMs = chunkMs;
            if (chunkMs > MaxChunkMs) MaxChunkMs = chunkMs;
            if (chunkMs > SpikeThresholdMs) SpikeCount++;
        }

        /// <summary>
        /// Records how many bytes a single read was asked for (the HTTP layer's chunk
        /// request size on uploads). A consistently small value points at the caller
        /// (HTTP transport), not the disk.
        /// </summary>
        public void TrackRequest(int requestedBytes)
        {
            if (requestedBytes <= 0) return;
            RequestCount++;
            RequestSum += requestedBytes;
            if (requestedBytes < MinRequest) MinRequest = requestedBytes;
            if (requestedBytes > MaxRequest) MaxRequest = requestedBytes;
        }

        /// <summary>Periodic sampling line (Debug level, ~1s cadence).</summary>
        public void LogProgress(string operation, string label)
        {
            long now = Stopwatch.GetTimestamp();
            double winSec = (now - _lastSampleTs) / (double)Stopwatch.Frequency;
            double inst = winSec > 0 ? (TotalBytes - _lastSampleBytes) / (1024.0 * 1024.0) / winSec : 0;
            _lastSampleTs = now;
            _lastSampleBytes = TotalBytes;
            Log.Dbg("Transfer.{Operation}: {Label} — {Bytes} bytes, {Inst:0.0} MB/s instant, {Avg:0.0} MB/s avg, {Chunks} chunks, {Elapsed:0.0}s",
                operation, label, TotalBytes, inst, AvgMbPerSec, ChunkCount, ElapsedMs / 1000.0);
        }

        public void LogSummary(string operation, string label)
            => Log.Info("Transfer.{Operation}: {Summary}", operation, FormatSummary(label));

        public void LogSummaryDbg(string operation, string label)
            => Log.Dbg("Transfer.{Operation}: {Summary}", operation, FormatSummary(label));

        private string FormatSummary(string label)
        {
            double elapsed = ElapsedMs;
            double freq = Stopwatch.Frequency;
            double readMs = ReadTicks * 1000.0 / freq;
            double writeMs = WriteTicks * 1000.0 / freq;
            double otherMs = Math.Max(0, elapsed - readMs - writeMs);
            double readPct = elapsed > 0 ? readMs / elapsed * 100.0 : 0;
            double writePct = elapsed > 0 ? writeMs / elapsed * 100.0 : 0;
            double otherPct = elapsed > 0 ? otherMs / elapsed * 100.0 : 0;
            double avgChunkMs = ChunkCount > 0 ? SumChunkMs / ChunkCount : 0;
            long avgChunkBytes = ChunkCount > 0 ? TotalBytes / ChunkCount : 0;
            string req = RequestCount > 0
                ? $", req {MinRequest}-{MaxRequest}B (avg {RequestSum / RequestCount}B)"
                : "";
            double minMs = MinChunkMs == double.MaxValue ? 0 : MinChunkMs;

            return $"{label} COMPLETE — {TotalBytes} bytes in {elapsed / 1000.0:0.0}s ({AvgMbPerSec:0.00} MB/s avg), " +
                   $"{ChunkCount} chunks (avg {avgChunkBytes}B), buf {BufferSize}B | " +
                   $"read {readMs:0.0}ms ({readPct:0}%), write {writeMs:0.0}ms ({writePct:0}%), other {otherMs:0.0}ms ({otherPct:0}%) | " +
                   $"chunk min {minMs:0.00}ms max {MaxChunkMs:0.00}ms avg {avgChunkMs:0.00}ms, spikes>{SpikeThresholdMs}ms: {SpikeCount}{req} | " +
                   $"{Source} -> {Dest}";
        }
    }
}
