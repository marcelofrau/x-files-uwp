using System.Diagnostics;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XFiles.FileSystem;

namespace XFiles.Tests
{
    [TestClass]
    public class TransferMeterTests
    {
        private static long TicksForMs(double ms) => (long)(ms * Stopwatch.Frequency / 1000.0);

        [TestMethod]
        public void ElapsedMs_BeforeStart_IsZero()
        {
            var m = new TransferMeter();
            Assert.AreEqual(0, m.ElapsedMs);
        }

        [TestMethod]
        public void Start_ElapsedIncreases()
        {
            var m = new TransferMeter();
            m.Start();
            Thread.Sleep(20);
            Assert.IsTrue(m.ElapsedMs > 0);
        }

        [TestMethod]
        public void TrackChunk_AccumulatesBytesCountAndTicks()
        {
            var m = new TransferMeter();
            m.TrackChunk(10, 5, 7);
            m.TrackChunk(20, 3, 4);

            Assert.AreEqual(30, m.TotalBytes);
            Assert.AreEqual(2, m.ChunkCount);
            Assert.AreEqual(8, m.ReadTicks);
            Assert.AreEqual(11, m.WriteTicks);
        }

        [TestMethod]
        public void TrackChunk_ZeroOrNegativeBytes_Ignored()
        {
            var m = new TransferMeter();
            m.TrackChunk(0, 5, 5);
            m.TrackChunk(-1, 5, 5);

            Assert.AreEqual(0, m.ChunkCount);
            Assert.AreEqual(0, m.TotalBytes);
            Assert.AreEqual(0, m.ReadTicks);
            Assert.AreEqual(0, m.WriteTicks);
        }

        [TestMethod]
        public void TrackChunk_TracksMinMaxAndSumChunkMs()
        {
            var m = new TransferMeter();
            long ticksA = TicksForMs(10); // read 8ms + write 2ms
            long ticksB = TicksForMs(40); // read 10ms + write 30ms
            m.TrackChunk(100, TicksForMs(8), TicksForMs(2));
            m.TrackChunk(100, TicksForMs(10), TicksForMs(30));

            double msA = ticksA * 1000.0 / Stopwatch.Frequency;
            double msB = ticksB * 1000.0 / Stopwatch.Frequency;
            Assert.AreEqual(msA, m.MinChunkMs, 1e-6);
            Assert.AreEqual(msB, m.MaxChunkMs, 1e-6);
            Assert.AreEqual(msA + msB, m.SumChunkMs, 1e-6);
        }

        [TestMethod]
        public void TrackChunk_SpikeThreshold_CountsSlowChunks()
        {
            var m = new TransferMeter();
            m.TrackChunk(100, TicksForMs(100), 0);          // 100ms total — not a spike
            m.TrackChunk(100, TicksForMs(251), 0);          // 251ms — spike
            m.TrackChunk(100, 0, TicksForMs(249.5));        // 249.5ms — not a spike

            Assert.AreEqual(1, m.SpikeCount);
        }

        [TestMethod]
        public void AvgMbPerSec_ComputesFromBytesAndElapsed()
        {
            var m = new TransferMeter();
            m.Start();
            m.TrackChunk(1024 * 1024, TicksForMs(500), TicksForMs(500));
            Thread.Sleep(10);

            double expected = m.TotalBytes / (1024.0 * 1024.0) / (m.ElapsedMs / 1000.0);
            Assert.AreEqual(expected, m.AvgMbPerSec, expected * 0.05);
        }

        [TestMethod]
        public void AvgMbPerSec_NotStarted_IsZero()
        {
            var m = new TransferMeter();
            m.TrackChunk(1024 * 1024, TicksForMs(1), TicksForMs(1));
            Assert.AreEqual(0, m.AvgMbPerSec);
        }

        [TestMethod]
        public void TrackRequest_AccumulatesHistogram()
        {
            var m = new TransferMeter();
            m.TrackRequest(4096);
            m.TrackRequest(8192);
            m.TrackRequest(16384);

            Assert.AreEqual(3, m.RequestCount);
            Assert.AreEqual(4096 + 8192 + 16384, m.RequestSum);
            Assert.AreEqual(4096, m.MinRequest);
            Assert.AreEqual(16384, m.MaxRequest);
        }

        [TestMethod]
        public void TrackRequest_NonPositive_Ignored()
        {
            var m = new TransferMeter();
            m.TrackRequest(0);
            m.TrackRequest(-5);

            Assert.AreEqual(0, m.RequestCount);
            Assert.AreEqual(0, m.RequestSum);
            Assert.AreEqual(int.MaxValue, m.MinRequest);
            Assert.AreEqual(0, m.MaxRequest);
        }

        [TestMethod]
        public void LogProgress_And_LogSummary_DoNotThrow()
        {
            var m = new TransferMeter
            {
                BufferSize = 1024 * 1024,
                Source = "src.bin",
                Dest = "dest.bin"
            };
            m.Start();
            m.TrackChunk(1024 * 1024, TicksForMs(10), TicksForMs(20));
            m.TrackRequest(65536);

            m.LogProgress("Copy", "file.bin");
            m.LogSummary("Copy", "file.bin");
            m.LogSummaryDbg("Copy", "file.bin");
        }
    }
}
