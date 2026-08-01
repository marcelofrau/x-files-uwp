using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XFiles.Audio;

namespace XFiles.Tests
{
    [TestClass]
    public class FftHelperTests
    {
        private const int N = 1024;

        [TestMethod]
        public void Compute_SineWave_PeakAtExpectedBin()
        {
            const int bin = 4;
            float[] real = new float[N];
            float[] imag = new float[N];
            for (int i = 0; i < N; i++)
                real[i] = (float)Math.Sin(2.0 * Math.PI * bin * i / N);

            FftHelper.Compute(real, imag, inverse: false);

            float[] mag = FftHelper.ComputeMagnitudes(real, imag, N / 2);
            int peak = 0;
            for (int i = 1; i < mag.Length; i++)
                if (mag[i] > mag[peak]) peak = i;

            Assert.AreEqual(bin, peak);
            Assert.IsTrue(mag[bin] > 100f, $"expected strong bin {bin}, got {mag[bin]}");
        }

        [TestMethod]
        public void Compute_Impulse_FlatSpectrum()
        {
            float[] real = new float[N];
            float[] imag = new float[N];
            real[0] = 1f;

            FftHelper.Compute(real, imag, inverse: false);

            float[] mag = FftHelper.ComputeMagnitudes(real, imag, N / 2);
            Assert.AreEqual(1f, mag[0], 1e-3f);
            for (int i = 1; i < mag.Length; i++)
                Assert.AreEqual(1f, mag[i], 1e-3f);
        }

        [TestMethod]
        public void Compute_NonPowerOfTwo_Throws()
        {
            float[] real = new float[100];
            float[] imag = new float[100];
            Assert.ThrowsException<ArgumentException>(() => FftHelper.Compute(real, imag, false));
        }

        [TestMethod]
        public void Compute_Empty_Throws()
        {
            Assert.ThrowsException<ArgumentException>(() => FftHelper.Compute(new float[0], new float[0], false));
        }

        [TestMethod]
        public void Compute_RoundTrip_ReconstructsSignal()
        {
            float[] real = new float[N];
            float[] imag = new float[N];
            for (int i = 0; i < N; i++)
                real[i] = (float)Math.Sin(2.0 * Math.PI * 8 * i / N) + 0.5f * (float)Math.Cos(2.0 * Math.PI * 16 * i / N);

            float[] original = (float[])real.Clone();
            float[] origImag = new float[N];

            FftHelper.Compute(real, imag, inverse: false);
            FftHelper.Compute(real, imag, inverse: true);

            for (int i = 0; i < N; i++)
            {
                Assert.AreEqual(original[i], real[i], 1e-2f);
                Assert.AreEqual(origImag[i], imag[i], 1e-2f);
            }
        }

        [TestMethod]
        public void ApplyHammingWindow_IsSymmetricAndAttenuatesEnds()
        {
            float[] data = Enumerable.Repeat(1f, N).ToArray();

            FftHelper.ApplyHammingWindow(data, N);

            Assert.AreEqual(data[1], data[N - 2], 1e-4f);
            Assert.AreEqual(data[0], data[N - 1], 1e-4f);
            Assert.IsTrue(data[0] < 0.15f, $"window end too high: {data[0]}");
            Assert.IsTrue(data[N / 2] > 0.9f, $"window center too low: {data[N / 2]}");
        }

        [TestMethod]
        public void ComputeMagnitudes_RespectsBinCount()
        {
            float[] real = new float[N];
            float[] imag = new float[N];
            real[0] = 1f;

            float[] mag = FftHelper.ComputeMagnitudes(real, imag, 16);

            Assert.AreEqual(16, mag.Length);
            Assert.AreEqual(1f, mag[0]);
        }
    }
}
