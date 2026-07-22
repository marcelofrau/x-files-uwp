using System;
using System.Collections.Generic;

namespace XFiles.Visualizers
{
    public static class VisualizerRegistry
    {
        private static readonly Type[] VisualizerTypes = new[]
        {
            typeof(Visualizers.RadialSpectrumVisualizer),
            typeof(Visualizers.WaveformVisualizer),
            typeof(Visualizers.PlasmaVisualizer),
            typeof(Visualizers.StarfieldVisualizer),
            typeof(Visualizers.SpiralSpectrumVisualizer),
            typeof(Visualizers.MirrorTunnelVisualizer),
            typeof(Visualizers.FireParticlesVisualizer),
            typeof(Visualizers.LissajousVisualizer),
            typeof(Visualizers.TerrainGeneratorVisualizer),
            typeof(Visualizers.OrbitingCirclesVisualizer),
            typeof(Visualizers.IsometricEqualizerVisualizer),
            typeof(Visualizers.NeonGlareVisualizer),
            typeof(Visualizers.KaleidoscopeVisualizer),
            typeof(Visualizers.ParticleBurstVisualizer),
            typeof(Visualizers.RipplePulseVisualizer),
            typeof(Visualizers.FeedbackTrailVisualizer),
            typeof(Visualizers.VoxelMatrixVisualizer),
            typeof(Visualizers.AnalogVUMeterVisualizer),
            typeof(Visualizers.CircularRadialSpectrumVisualizer),
            typeof(Visualizers.RetroOscilloscopeVisualizer),
            typeof(Visualizers.InfernoCoreVisualizer),
        };

        public static int Count => VisualizerTypes.Length;

        public static IAudioVisualizer Create(int index)
        {
            if (index < 0 || index >= VisualizerTypes.Length)
                throw new ArgumentOutOfRangeException(nameof(index));
            return (IAudioVisualizer)Activator.CreateInstance(VisualizerTypes[index]);
        }

        public static IAudioVisualizer Resolve(AudioFullscreenMode mode)
        {
            int index;
            switch (mode)
            {
                case AudioFullscreenMode.RadialSpectrum: index = 0; break;
                case AudioFullscreenMode.Waveform: index = 1; break;
                case AudioFullscreenMode.Plasma: index = 2; break;
                case AudioFullscreenMode.Starfield: index = 3; break;
                case AudioFullscreenMode.SpiralSpectrum: index = 4; break;
                case AudioFullscreenMode.MirrorTunnel: index = 5; break;
                case AudioFullscreenMode.FireParticles: index = 6; break;
                case AudioFullscreenMode.Lissajous: index = 7; break;
                case AudioFullscreenMode.TerrainGenerator: index = 8; break;
                case AudioFullscreenMode.OrbitingCircles: index = 9; break;
                case AudioFullscreenMode.IsometricEqualizer: index = 10; break;
                case AudioFullscreenMode.NeonGlare: index = 11; break;
                case AudioFullscreenMode.Kaleidoscope: index = 12; break;
                case AudioFullscreenMode.ParticleBurst: index = 13; break;
                case AudioFullscreenMode.RipplePulse: index = 14; break;
                case AudioFullscreenMode.FeedbackTrail: index = 15; break;
                case AudioFullscreenMode.VoxelMatrix: index = 16; break;
                case AudioFullscreenMode.AnalogVUMeter: index = 17; break;
                case AudioFullscreenMode.CircularRadialSpectrum: index = 18; break;
                case AudioFullscreenMode.RetroOscilloscope: index = 19; break;
                case AudioFullscreenMode.InfernoCore: index = 20; break;
                default: index = -1; break;
            }
            if (index < 0 || index >= VisualizerTypes.Length)
                return null;
            return (IAudioVisualizer)Activator.CreateInstance(VisualizerTypes[index]);
        }
    }
}
