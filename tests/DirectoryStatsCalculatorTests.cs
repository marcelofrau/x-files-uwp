using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XFiles.FileSystem;

namespace XFiles.Tests
{
    [TestClass]
    public class DirectoryStatsCalculatorTests
    {
        private string _root;

        [TestInitialize]
        public void Setup()
        {
            _root = Path.Combine(Path.GetTempPath(), "xf-stats-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TestCleanup]
        public void Cleanup()
        {
            try { Directory.Delete(_root, true); } catch { }
        }

        private void CreateFile(string relative, byte[] content = null)
        {
            string full = Path.Combine(_root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllBytes(full, content ?? new byte[] { 1 });
        }

        [TestMethod]
        public async Task ComputeAsync_CountsFilesFoldersAndTotalBytes()
        {
            CreateFile("a.txt", new byte[100]);
            CreateFile("b.txt", new byte[150]);
            CreateFile("sub\\c.bin", new byte[250]);
            CreateFile("sub\\deep\\d.bin", new byte[50]);

            var stats = await DirectoryStatsCalculator.ComputeAsync(_root);

            Assert.AreEqual(4, stats.FileCount);
            Assert.AreEqual(2, stats.FolderCount, "sub and sub/deep");
            Assert.AreEqual(100 + 150 + 250 + 50, stats.TotalBytes);
            Assert.IsTrue(stats.Complete);
            Assert.IsFalse(stats.IsPartial);
            Assert.IsFalse(stats.Cancelled);
        }

        [TestMethod]
        public async Task ComputeAsync_EmptyFolder_CompletesWithZeroes()
        {
            var stats = await DirectoryStatsCalculator.ComputeAsync(_root);

            Assert.AreEqual(0, stats.FileCount);
            Assert.AreEqual(0, stats.TotalBytes);
            Assert.IsTrue(stats.Complete);
        }

        [TestMethod]
        public async Task ComputeAsync_NullOrEmptyPath_Completes()
        {
            var empty = await DirectoryStatsCalculator.ComputeAsync("");
            Assert.IsTrue(empty.Complete);
            Assert.AreEqual(0, empty.FileCount);
        }

        // Synchronous inline progress — Progress<T> posts asynchronously when
        // there is no SynchronizationContext (unit-test thread), which races the
        // assertions below.
        private sealed class InlineProgress<T> : IProgress<T>
        {
            public Action<T> Handler;
            public void Report(T value) => Handler?.Invoke(value);
        }

        [TestMethod]
        public async Task ComputeAsync_ReportsPartialSnapshots_GrowingTotals()
        {
            for (int i = 0; i < 40; i++)
            {
                CreateFile("f" + i + ".bin", new byte[10]);
            }

            var partials = new List<DirectoryStatsSnapshot>();
            var reporter = new InlineProgress<DirectoryStatsSnapshot>() { Handler = s => partials.Add(s) };

            var final = await DirectoryStatsCalculator.ComputeAsync(_root, reporter);

            Assert.IsTrue(partials.Count >= 1, "should have reported at least one partial");
            Assert.AreEqual(40, final.FileCount);
            for (int i = 1; i < partials.Count; i++)
            {
                Assert.IsTrue(partials[i].FileCount >= partials[i - 1].FileCount,
                    "partials must be monotonically non-decreasing");
            }
            Assert.IsTrue(partials[partials.Count - 1].FileCount <= final.FileCount);
        }

        [TestMethod]
        public async Task ComputeAsync_Cancelled_FlagsCancelledAndPartial()
        {
            for (int i = 0; i < 1000; i++)
            {
                CreateFile("f" + i + ".bin", new byte[10]);
            }

            var cts = new CancellationTokenSource();
            var reporter = new InlineProgress<DirectoryStatsSnapshot>() { Handler = s => cts.Cancel() };

            var stats = await DirectoryStatsCalculator.ComputeAsync(_root, reporter, cts.Token);

            Assert.IsTrue(stats.Cancelled);
            Assert.IsTrue(stats.IsPartial);
            Assert.IsTrue(stats.FileCount < 1000, "cancel mid-scan should stop before all files");
        }

        [TestMethod]
        public async Task ComputeAsync_PrecancelledToken_ReturnsCancelledImmediately()
        {
            CreateFile("a.txt");

            var cts = new CancellationTokenSource();
            cts.Cancel();

            var stats = await DirectoryStatsCalculator.ComputeAsync(_root, null, cts.Token);

            Assert.IsTrue(stats.Cancelled);
            Assert.AreEqual(0, stats.FileCount);
        }

        [TestMethod]
        public async Task ComputeAsync_Cap_StopsAndFlagsIncomplete()
        {
            for (int i = 0; i < 60; i++)
            {
                CreateFile("f" + i + ".bin", new byte[10]);
            }

            var stats = await DirectoryStatsCalculator.ComputeAsync(_root, null, CancellationToken.None, maxEntries: 50);
            Console.WriteLine($"DEBUG cap: files={stats.FileCount} complete={stats.Complete} cancelled={stats.Cancelled} partial={stats.IsPartial}");

            Assert.IsTrue(stats.FileCount >= 1 && stats.FileCount <= 50);
            Assert.IsFalse(stats.Complete);
            Assert.IsTrue(stats.IsPartial);
            Assert.IsFalse(stats.Cancelled);
        }

        [TestMethod]
        public async Task ComputeAsync_LargeTree_ManySubfolders()
        {
            for (int d = 0; d < 5; d++)
                for (int f = 0; f < 10; f++)
                    CreateFile($"dir{d}\\file{f}.dat", new byte[7]);

            var stats = await DirectoryStatsCalculator.ComputeAsync(_root);

            Assert.AreEqual(50, stats.FileCount);
            Assert.AreEqual(5, stats.FolderCount);
            Assert.AreEqual(50L * 7, stats.TotalBytes);
            Assert.IsTrue(stats.Complete);
        }
    }
}