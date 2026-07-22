namespace XFiles.Visualizers
{
    /// <summary>
    /// Snapshot of audio data for a single frame. Passed to visualizers each update tick.
    /// Arrays are defensive copies — safe to read from UI thread while audio thread writes.
    /// </summary>
    public readonly struct AudioData
    {
        public const int BandCount = 26;
        public const int FftBinCount = 512;

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

        /// <summary>
        /// Create a snapshot from the live AudioLevelService arrays.
        /// Defensive copies ensure thread safety.
        /// </summary>
        public static AudioData FromService(Audio.AudioLevelService service, float time)
        {
            var bands = new float[BandCount];
            var peaks = new float[BandCount];
            var mags = new float[FftBinCount];
            var wave = new float[Audio.AudioLevelService.FftSize];

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
