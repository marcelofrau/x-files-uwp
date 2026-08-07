using Microsoft.VisualStudio.TestTools.UnitTesting;
using XFiles.Controls;

namespace XFiles.Tests
{
    [TestClass]
    public class TransferStatsTests
    {
        [TestMethod]
        public void Speed_NoSamples_ReturnsZero()
        {
            var stats = new TransferStats();
            Assert.AreEqual(0, stats.SpeedBytesPerSecond());
        }

        [TestMethod]
        public void Speed_SingleSample_ReturnsZero()
        {
            var stats = new TransferStats();
            stats.Sample(0, 0);
            Assert.AreEqual(0, stats.SpeedBytesPerSecond());
        }

        [TestMethod]
        public void Speed_TwoSamples_ComputesRate()
        {
            var stats = new TransferStats();
            stats.Sample(0, 0);
            stats.Sample(2, 2000);
            Assert.AreEqual(1000, stats.SpeedBytesPerSecond(), 1e-6);
        }

        [TestMethod]
        public void Speed_IgnoresOlderSamplesOutsideWindow()
        {
            var stats = new TransferStats();
            stats.Sample(0, 0);
            stats.Sample(2, 200);
            stats.Sample(5, 700); // 3s after t=2; t=0 sample falls out of the 4s window
            double speed = stats.SpeedBytesPerSecond();
            // Window now spans [1,5]: (700-200)/(5-2) = 166.67
            Assert.AreEqual(166.667, speed, 1e-3);
        }

        [TestMethod]
        public void EtaSeconds_RemainingOverSpeed()
        {
            var stats = new TransferStats();
            stats.Sample(0, 0);
            stats.Sample(2, 1000);
            // speed = 500 B/s; remaining = 1500-1000 = 500 → 1s
            Assert.AreEqual(1, stats.EtaSeconds(1500, 1000), 1e-6);
        }

        [TestMethod]
        public void EtaSeconds_NoSpeed_ReturnsNegative()
        {
            var stats = new TransferStats();
            Assert.AreEqual(-1, stats.EtaSeconds(1000, 0));
        }

        [TestMethod]
        public void Sample_NonMonotonicInput_ClampsToLastValue()
        {
            var stats = new TransferStats();
            stats.Sample(0, 0);
            stats.Sample(1, 100);
            stats.Sample(0.5, 10); // time and bytes go backwards — clamped
            Assert.AreEqual(100, stats.SpeedBytesPerSecond(), 1e-6);
        }

        [TestMethod]
        public void Reset_ClearsHistory()
        {
            var stats = new TransferStats();
            stats.Sample(0, 0);
            stats.Sample(1, 100);
            stats.Reset();
            Assert.AreEqual(0, stats.SpeedBytesPerSecond());
        }
    }
}
