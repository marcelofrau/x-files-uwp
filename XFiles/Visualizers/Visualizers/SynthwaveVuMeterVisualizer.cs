using System;
using Microsoft.Graphics.Canvas;
using Windows.UI;

namespace XFiles.Visualizers.Visualizers
{
    public sealed class SynthwaveVuMeterVisualizer : IAudioVisualizer
    {
        public string Name => "Synthwave Peak VU Meter";
        public string Id => "synthwave-vumeter";

        private CanvasDevice _device;
        private float _width, _height, _time;

        private const int BarCount = 32;
        private const int SegmentCount = 28;

        private readonly float[] _smoothBands = new float[BarCount];
        private readonly float[] _peakHeights = new float[BarCount];
        private readonly float[] _peakVelocities = new float[BarCount];

        private float _smoothBass, _smoothBeat;
        private const float AudioSmooth = 0.22f;
        private const float Gravity = 1.8f;

        public void Initialize(CanvasDevice device) { _device = device; }

        public void Update(AudioData data, TimeSpan elapsed)
        {
            if (data.BandLevels == null || data.BandLevels.Length == 0) return;

            _time = data.Time;
            float dt = (float)elapsed.TotalSeconds;

            float bass = 0;
            int bassBands = Math.Min(4, data.BandLevels.Length);
            for (int i = 0; i < bassBands; i++) bass += data.BandLevels[i];
            bass /= bassBands;

            _smoothBass += (bass - _smoothBass) * AudioSmooth;
            _smoothBeat += (data.Beat - _smoothBeat) * 0.35f;

            for (int i = 0; i < BarCount; i++)
            {
                int srcIdx = (int)((float)i / BarCount * data.BandLevels.Length);
                float targetLevel = data.BandLevels[Math.Min(srcIdx, data.BandLevels.Length - 1)];

                _smoothBands[i] += (targetLevel - _smoothBands[i]) * AudioSmooth;

                if (_smoothBands[i] > _peakHeights[i])
                {
                    _peakHeights[i] = _smoothBands[i];
                    _peakVelocities[i] = 0.8f;
                }
                else
                {
                    _peakVelocities[i] += Gravity * dt;
                    _peakHeights[i] -= _peakVelocities[i] * dt;
                    if (_peakHeights[i] < 0f) _peakHeights[i] = 0f;
                }
            }
        }

        public void Draw(CanvasDrawingSession ds)
        {
            if (_device == null || _width == 0 || _height == 0) return;

            float cx = _width * 0.5f;
            float cy = _height * 0.5f;

            ds.Clear(Color.FromArgb(255, 8, 3, 20));

            DrawRetroGrid(ds, cx, cy);
            DrawVuBars(ds);
        }

        private void DrawVuBars(CanvasDrawingSession ds)
        {
            float totalWidth = _width * 0.82f;
            float barPadding = 5f;
            float barWidth = (totalWidth - (BarCount * barPadding)) / BarCount;
            float startX = (_width - totalWidth) * 0.5f;

            float maxBarHeight = _height * 0.45f;
            float baseY = _height * 0.65f;
            float segmentGap = 2.5f;
            float segmentHeight = (maxBarHeight - (SegmentCount * segmentGap)) / SegmentCount;

            for (int i = 0; i < BarCount; i++)
            {
                float x = startX + i * (barWidth + barPadding);
                float level = Math.Clamp(_smoothBands[i], 0f, 1f);
                int activeSegments = (int)(level * SegmentCount);

                for (int s = 0; s < SegmentCount; s++)
                {
                    float y = baseY - (s * (segmentHeight + segmentGap)) - segmentHeight;
                    float segRatio = (float)s / SegmentCount;
                    Color segColor = GetSynthwaveColor(segRatio);

                    if (s < activeSegments)
                    {
                        ds.FillRectangle(x, y, barWidth, segmentHeight, segColor);
                    }
                    else
                    {
                        Color inactiveColor = Color.FromArgb(25, segColor.R, segColor.G, segColor.B);
                        ds.FillRectangle(x, y, barWidth, segmentHeight, inactiveColor);
                    }
                }

                float peakLevel = Math.Clamp(_peakHeights[i], 0f, 1f);
                int peakSegment = (int)(peakLevel * SegmentCount);
                if (peakSegment >= SegmentCount) peakSegment = SegmentCount - 1;

                float peakY = baseY - (peakSegment * (segmentHeight + segmentGap)) - segmentHeight;
                Color peakColor = Color.FromArgb(255, 255, 240, 120);

                ds.FillRectangle(x, peakY - 1.5f, barWidth, 3f, peakColor);

                for (int s = 0; s < activeSegments; s++)
                {
                    float reflY = baseY + (s * (segmentHeight + segmentGap)) + segmentGap;
                    float segRatio = (float)s / SegmentCount;

                    float fade = Math.Clamp(1.0f - (segRatio * 0.85f), 0.05f, 0.45f);
                    Color segColor = GetSynthwaveColor(segRatio);
                    Color reflColor = Color.FromArgb((byte)(segColor.A * fade), segColor.R, segColor.G, segColor.B);

                    ds.FillRectangle(x, reflY, barWidth, segmentHeight, reflColor);
                }
            }
        }

        private void DrawRetroGrid(CanvasDrawingSession ds, float cx, float cy)
        {
            float horizonY = _height * 0.65f;

            int lineCount = 10;
            for (int i = 1; i <= lineCount; i++)
            {
                float progress = (float)i / lineCount;
                float y = horizonY + MathF.Pow(progress, 2.2f) * (_height - horizonY);

                Color lineCol = Color.FromArgb((byte)(120 * progress), 255, 0, 180);
                ds.DrawLine(0, y, _width, y, lineCol, 1.2f + _smoothBass * 1.5f);
            }
        }

        private static Color GetSynthwaveColor(float ratio)
        {
            if (ratio < 0.5f)
            {
                float t = ratio / 0.5f;
                byte r = (byte)(0 + t * 255);
                byte g = (byte)(235 - t * 185);
                byte b = (byte)(255 - t * 55);
                return Color.FromArgb(255, r, g, b);
            }
            else
            {
                float t = (ratio - 0.5f) / 0.5f;
                byte r = 255;
                byte g = (byte)(50 + t * 195);
                byte b = (byte)(200 - t * 200);
                return Color.FromArgb(255, r, g, b);
            }
        }

        public void Resize(float width, float height) { _width = width; _height = height; }
        public void Dispose() { _device = null; }

        public void ConfigurePipeline(PostProcessPipeline pipeline)
        {
            pipeline.BloomAmount = 0.70f;
            pipeline.BloomBlur = 3.8f;
            pipeline.BloomThreshold = 0.10f;
        }
    }
}
