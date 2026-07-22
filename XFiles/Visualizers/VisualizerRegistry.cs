using System;
using System.Collections.Generic;

namespace XFiles.Visualizers
{
    /// <summary>
    /// Registry of available audio visualizer implementations.
    /// Add new visualizer types here to make them available in the cycling order.
    /// </summary>
    public static class VisualizerRegistry
    {
        private static readonly Type[] VisualizerTypes = new[]
        {
            typeof(Visualizers.RadialSpectrumVisualizer),
            typeof(Visualizers.WaveformVisualizer),
            // Future visualizers go here:
            // typeof(Visualizers.PlasmaVisualizer),
        };

        /// <summary>Total number of registered visualizers.</summary>
        public static int Count => VisualizerTypes.Length;

        /// <summary>Create a visualizer instance by index.</summary>
        public static IAudioVisualizer Create(int index)
        {
            if (index < 0 || index >= VisualizerTypes.Length)
                throw new ArgumentOutOfRangeException(nameof(index));

            return (IAudioVisualizer)Activator.CreateInstance(VisualizerTypes[index]);
        }

        /// <summary>
        /// Create a visualizer for the given mode.
        /// Returns null for AudioFullscreenMode.Default (caller shows default UI).
        /// </summary>
        public static IAudioVisualizer Resolve(AudioFullscreenMode mode)
        {
            int index;
            switch (mode)
            {
                case AudioFullscreenMode.RadialSpectrum: index = 0; break;
                case AudioFullscreenMode.Waveform: index = 1; break;
                case AudioFullscreenMode.Plasma: index = 2; break;
                default: index = -1; break;
            }
            if (index < 0 || index >= VisualizerTypes.Length)
                return null;
            return (IAudioVisualizer)Activator.CreateInstance(VisualizerTypes[index]);
        }
    }
}
