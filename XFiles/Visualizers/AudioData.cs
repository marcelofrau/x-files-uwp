using System;
using System.Buffers;

namespace XFiles.Visualizers
{
    public readonly struct AudioData
    {
        public const int BandCount = 26;
        public const int FftBinCount = 1024;

        public readonly float[] BandLevels;
        public readonly float[] BandPeaks;
        public readonly float[] Magnitudes;
        public readonly float[] Waveform;
        public readonly int WaveformCount;
        public readonly float Beat;
        public readonly float Time;

        public AudioData(
            float[] bandLevels,
            float[] bandPeaks,
            float[] magnitudes,
            float[] waveform,
            int waveformCount,
            float beat,
            float time)
        {
            BandLevels = bandLevels;
            BandPeaks = bandPeaks;
            Magnitudes = magnitudes;
            Waveform = waveform;
            WaveformCount = waveformCount;
            Beat = beat;
            Time = time;
        }

        public static AudioData FromService(Audio.AudioLevelService service, float time,
            float[] bands, float[] peaks, float[] mags, float[] wave)
        {
            if (service.BandLevels.Length < BandCount || service.BandPeaks.Length < BandCount ||
                service.Magnitudes.Length < FftBinCount || service.Waveform.Length < Audio.AudioLevelService.FftSize)
            {
                Log.Warn("AudioData.FromService: array size mismatch — " +
                    "bandLevels={SLen} bands={BLen} bandPeaks={SPeaksLen} peaks={BPeaksLen} " +
                    "magnitudes={SMagLen} mags={BMagLen} waveform={SWaveLen} wave={BWaveLen}",
                    service.BandLevels.Length, bands.Length,
                    service.BandPeaks.Length, peaks.Length,
                    service.Magnitudes.Length, mags.Length,
                    service.Waveform.Length, wave.Length);
                return new AudioData(
                    bands, peaks, mags, wave,
                    service.WaveformCount,
                    service.Beat,
                    time);
            }
            System.Array.Copy(service.BandLevels, bands, BandCount);
            System.Array.Copy(service.BandPeaks, peaks, BandCount);
            System.Array.Copy(service.Magnitudes, mags, FftBinCount);
            System.Array.Copy(service.Waveform, wave, Audio.AudioLevelService.FftSize);

            return new AudioData(
                bands, peaks, mags, wave,
                service.WaveformCount,
                service.Beat,
                time);
        }
    }
}
