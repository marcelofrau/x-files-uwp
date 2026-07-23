using System;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Windows.Foundation;
using Windows.UI;

namespace XFiles.Visualizers.Visualizers
{
    public sealed class SpiralSpectrumVisualizer : IAudioVisualizer
    {
        public string Name => "Spiral Spectrum";
        public string Id => "spiral-spectrum";

        private CanvasDevice _device;
        private float _width, _height, _time;

        private readonly float[] _smoothBands = new float[AudioData.BandCount];
        private float _smoothBeat;
        private float[] _waveform;
        private int _waveformCount;
        private float _scrollOffset;
        private const float AudioSmooth = 0.25f;
        private const float BaseRadius = 0.08f;
        private const float MaxSpiralRadius = 0.38f;
        private const float SpiralTightness = 0.25f;
        private const int PointsPerBand = 8;

        public void Initialize(CanvasDevice device) { _device = device; }

        public void Update(AudioData data, TimeSpan elapsed)
        {
            _time = data.Time;
            _waveform = data.Waveform;
            _waveformCount = data.WaveformCount;
            _scrollOffset += (0.05f + _smoothBeat * 0.1f) * (float)elapsed.TotalSeconds;
            for (int i = 0; i < AudioData.BandCount; i++)
                _smoothBands[i] += (data.BandLevels[i] - _smoothBands[i]) * AudioSmooth;
            _smoothBeat += (data.Beat - _smoothBeat) * 0.35f;
        }

        public void Draw(CanvasDrawingSession ds)
        {
            if (_device == null || _width == 0 || _height == 0) return;
            ds.Clear(Color.FromArgb(255, 3, 3, 8));
            DrawSpiralGlow(ds);
            DrawSpiral(ds);
            DrawWaveform(ds);
        }

        public void Resize(float width, float height) { _width = width; _height = height; }

        public void Dispose() { _device = null; }

        private void DrawSpiralGlow(CanvasDrawingSession ds)
        {
            float cx = _width * 0.5f, cy = _height * 0.5f;
            float minDim = Math.Min(_width, _height);
            float timeAngle = _scrollOffset;
            float burstScale = 1f + _smoothBeat * 0.3f;
            var strokeStyle = new CanvasStrokeStyle { StartCap = CanvasCapStyle.Round, EndCap = CanvasCapStyle.Round };
            int totalPoints = AudioData.BandCount * PointsPerBand;

            for (int p = 0; p < totalPoints - 1; p++)
            {
                float t0 = (float)p / totalPoints, t1 = (float)(p + 1) / totalPoints;
                int band0 = Math.Min((int)(t0 * AudioData.BandCount), AudioData.BandCount - 1);
                int band1 = Math.Min((int)(t1 * AudioData.BandCount), AudioData.BandCount - 1);
                float mag0 = _smoothBands[band0], mag1 = _smoothBands[band1];
                float angle0 = timeAngle + t0 * AudioData.BandCount * SpiralTightness * 2f * (float)Math.PI;
                float angle1 = timeAngle + t1 * AudioData.BandCount * SpiralTightness * 2f * (float)Math.PI;
                float r0 = (BaseRadius + mag0 * MaxSpiralRadius) * minDim * burstScale;
                float r1 = (BaseRadius + mag1 * MaxSpiralRadius) * minDim * burstScale;
                float x0 = cx + (float)Math.Cos(angle0) * r0, y0 = cy + (float)Math.Sin(angle0) * r0;
                float x1 = cx + (float)Math.Cos(angle1) * r1, y1 = cy + (float)Math.Sin(angle1) * r1;
                float hue = (t0 + _time * 0.05f) % 1.0f;
                float sat = 0.75f + _smoothBeat * 0.25f;
                float lum = 0.45f + mag0 * 0.35f;
                float avgR = (r0 + r1) * 0.5f;
                float depthFade = Math.Max(0.3f, avgR / (minDim * MaxSpiralRadius));
                float thickness = (1.5f + mag0 * 4f) * 4f;
                Color c = HslToRgb(hue, sat, lum);
                byte a = (byte)Math.Min(255, (int)(30 * depthFade));
                ds.DrawLine(x0, y0, x1, y1, Color.FromArgb(a, c.R, c.G, c.B), thickness, strokeStyle);
            }
        }

        private void DrawSpiral(CanvasDrawingSession ds)
        {
            float cx = _width * 0.5f, cy = _height * 0.5f;
            float minDim = Math.Min(_width, _height);
            float timeAngle = _scrollOffset;
            float burstScale = 1f + _smoothBeat * 0.3f;
            var strokeStyle = new CanvasStrokeStyle { StartCap = CanvasCapStyle.Round, EndCap = CanvasCapStyle.Round };
            int totalPoints = AudioData.BandCount * PointsPerBand;

            for (int p = 0; p < totalPoints - 1; p++)
            {
                float t0 = (float)p / totalPoints, t1 = (float)(p + 1) / totalPoints;
                int band0 = Math.Min((int)(t0 * AudioData.BandCount), AudioData.BandCount - 1);
                int band1 = Math.Min((int)(t1 * AudioData.BandCount), AudioData.BandCount - 1);
                float mag0 = _smoothBands[band0], mag1 = _smoothBands[band1];
                float angle0 = timeAngle + t0 * AudioData.BandCount * SpiralTightness * 2f * (float)Math.PI;
                float angle1 = timeAngle + t1 * AudioData.BandCount * SpiralTightness * 2f * (float)Math.PI;
                float r0 = (BaseRadius + mag0 * MaxSpiralRadius) * minDim * burstScale;
                float r1 = (BaseRadius + mag1 * MaxSpiralRadius) * minDim * burstScale;
                float x0 = cx + (float)Math.Cos(angle0) * r0, y0 = cy + (float)Math.Sin(angle0) * r0;
                float x1 = cx + (float)Math.Cos(angle1) * r1, y1 = cy + (float)Math.Sin(angle1) * r1;
                float hue = (t0 + _time * 0.05f) % 1.0f;
                float sat = 0.75f + _smoothBeat * 0.25f;
                float lum = 0.45f + mag0 * 0.35f;
                float avgR = (r0 + r1) * 0.5f;
                float depthFade = Math.Max(0.3f, avgR / (minDim * MaxSpiralRadius));
                float thickness = 1.5f + mag0 * 4f;
                Color c = HslToRgb(hue, sat, lum);
                ds.DrawLine(x0, y0, x1, y1, Color.FromArgb((byte)Math.Min(255, (int)(255 * depthFade)), c.R, c.G, c.B), thickness, strokeStyle);
            }

            float orbRadius = minDim * 0.03f * (1f + _smoothBeat * 0.5f);
            float orbHue = (_time * 0.1f) % 1.0f;
            var orbGeo = CanvasGeometry.CreateEllipse(ds, cx, cy, orbRadius, orbRadius);
            ds.FillGeometry(orbGeo, HslToRgb(orbHue, 0.9f, 0.7f + _smoothBeat * 0.3f));
            var coreGeo = CanvasGeometry.CreateEllipse(ds, cx, cy, orbRadius * 0.4f, orbRadius * 0.4f);
            ds.FillGeometry(coreGeo, Color.FromArgb(255, 255, 255, 255));
            DrawBandRing(ds, cx, cy, minDim);
        }

        private void DrawWaveform(CanvasDrawingSession ds)
        {
            if (_waveform == null || _waveformCount <= 0) return;
            float cx = _width * 0.5f, cy = _height * 0.5f;
            float minDim = Math.Min(_width, _height);
            float baseRadius = minDim * 0.2f;
            float beatScale = 1f + _smoothBeat * 0.15f;
            var strokeStyle = new CanvasStrokeStyle { StartCap = CanvasCapStyle.Round, EndCap = CanvasCapStyle.Round };

            // Glow pass
            for (int i = 0; i < _waveformCount - 1; i++)
            {
                float a0 = (float)i / _waveformCount * 2f * (float)Math.PI;
                float a1 = (float)(i + 1) / _waveformCount * 2f * (float)Math.PI;
                float r0 = baseRadius + _waveform[i] * minDim * 0.1f * beatScale;
                float r1 = baseRadius + _waveform[i + 1] * minDim * 0.1f * beatScale;
                float x0 = cx + (float)Math.Cos(a0) * r0, y0 = cy + (float)Math.Sin(a0) * r0;
                float x1 = cx + (float)Math.Cos(a1) * r1, y1 = cy + (float)Math.Sin(a1) * r1;
                ds.DrawLine(x0, y0, x1, y1, Color.FromArgb(30, 0, 230, 255), 5f, strokeStyle);
            }
            // Sharp pass
            for (int i = 0; i < _waveformCount - 1; i++)
            {
                float a0 = (float)i / _waveformCount * 2f * (float)Math.PI;
                float a1 = (float)(i + 1) / _waveformCount * 2f * (float)Math.PI;
                float r0 = baseRadius + _waveform[i] * minDim * 0.1f * beatScale;
                float r1 = baseRadius + _waveform[i + 1] * minDim * 0.1f * beatScale;
                float x0 = cx + (float)Math.Cos(a0) * r0, y0 = cy + (float)Math.Sin(a0) * r0;
                float x1 = cx + (float)Math.Cos(a1) * r1, y1 = cy + (float)Math.Sin(a1) * r1;
                ds.DrawLine(x0, y0, x1, y1, Color.FromArgb(180, 200, 255, 255), 2f, strokeStyle);
            }
        }

        private void DrawBandRing(CanvasDrawingSession ds, float cx, float cy, float minDim)
        {
            float ringRadius = minDim * 0.42f;
            var strokeStyle = new CanvasStrokeStyle { StartCap = CanvasCapStyle.Round, EndCap = CanvasCapStyle.Round };
            for (int i = 0; i < AudioData.BandCount; i++)
            {
                float angle = (float)i / AudioData.BandCount * 2f * (float)Math.PI - (float)Math.PI / 2f;
                float nextAngle = (float)(i + 1) / AudioData.BandCount * 2f * (float)Math.PI - (float)Math.PI / 2f;
                float level = _smoothBands[i];
                if (level < 0.02f) continue;
                float hue = (float)i / AudioData.BandCount;
                float lum = 0.4f + level * 0.4f;
                float x1 = cx + (float)Math.Cos(angle) * ringRadius, y1 = cy + (float)Math.Sin(angle) * ringRadius;
                float x2 = cx + (float)Math.Cos(nextAngle) * ringRadius, y2 = cy + (float)Math.Sin(nextAngle) * ringRadius;
                float thickness = 3f + level * 6f;
                ds.DrawLine(x1, y1, x2, y2, HslToRgb(hue, 0.8f, lum), thickness, strokeStyle);
            }
        }

        private static Color HslToRgb(float h, float s, float l)
        {
            h -= (float)Math.Floor(h); float hue = h * 360f;
            float c = (1f - Math.Abs(2f * l - 1f)) * s;
            float x = c * (1f - Math.Abs((hue / 60f) % 2f - 1f));
            float m = l - c / 2f;
            float r, g, b;
            if (hue < 60) { r = c; g = x; b = 0; }
            else if (hue < 120) { r = x; g = c; b = 0; }
            else if (hue < 180) { r = 0; g = c; b = x; }
            else if (hue < 240) { r = 0; g = x; b = c; }
            else if (hue < 300) { r = x; g = 0; b = c; }
            else { r = c; g = 0; b = x; }
            return Color.FromArgb(255, (byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
        }

        public void ConfigurePipeline(PostProcessPipeline pipeline)
        {
            pipeline.FeedbackOpacity = 0.35f;
            pipeline.FeedbackZoom = 1.002f;
            pipeline.BloomAmount = 0.05f;
            pipeline.BloomBlur = 3f;
        }
    }
}
