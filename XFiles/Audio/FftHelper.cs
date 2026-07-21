using System;

namespace XFiles.Audio
{
    internal static class FftHelper
    {
        public static void Compute(float[] real, float[] imag, bool inverse)
        {
            int n = real.Length;
            if (n == 0 || (n & (n - 1)) != 0)
                throw new ArgumentException("FFT length must be a power of 2.");

            BitReverse(real, imag, n);

            for (int size = 2; size <= n; size *= 2)
            {
                int half = size / 2;
                double angle = (inverse ? 2.0 : -2.0) * Math.PI / size;
                float wr = (float)Math.Cos(angle);
                float wi = (float)Math.Sin(angle);

                for (int i = 0; i < n; i += size)
                {
                    float curWr = 1f;
                    float curWi = 0f;

                    for (int j = 0; j < half; j++)
                    {
                        int uIdx = i + j;
                        int tIdx = i + j + half;

                        float tR = curWr * real[tIdx] - curWi * imag[tIdx];
                        float tI = curWr * imag[tIdx] + curWi * real[tIdx];

                        real[tIdx] = real[uIdx] - tR;
                        imag[tIdx] = imag[uIdx] - tI;
                        real[uIdx] += tR;
                        imag[uIdx] += tI;

                        float newWr = curWr * wr - curWi * wi;
                        curWi = curWr * wi + curWi * wr;
                        curWr = newWr;
                    }
                }
            }

            if (inverse)
            {
                float scale = 1f / n;
                for (int i = 0; i < n; i++)
                {
                    real[i] *= scale;
                    imag[i] *= scale;
                }
            }
        }

        private static void BitReverse(float[] real, float[] imag, int n)
        {
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                while ((j & bit) != 0)
                {
                    j ^= bit;
                    bit >>= 1;
                }
                j ^= bit;

                if (i < j)
                {
                    float tmpR = real[i];
                    float tmpI = imag[i];
                    real[i] = real[j];
                    imag[i] = imag[j];
                    real[j] = tmpR;
                    imag[j] = tmpI;
                }
            }
        }

        public static void ApplyHammingWindow(float[] data, int length)
        {
            int n = Math.Min(length, data.Length);
            for (int i = 0; i < n; i++)
            {
                float window = 0.54f - 0.46f * (float)Math.Cos(2.0 * Math.PI * i / (n - 1));
                data[i] *= window;
            }
        }

        public static float[] ComputeMagnitudes(float[] real, float[] imag, int binCount)
        {
            int count = Math.Min(binCount, real.Length / 2);
            float[] magnitudes = new float[count];
            for (int i = 0; i < count; i++)
            {
                magnitudes[i] = (float)Math.Sqrt(real[i] * real[i] + imag[i] * imag[i]);
            }
            return magnitudes;
        }
    }
}
