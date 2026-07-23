using System;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Geometry;
using Windows.Foundation;
using Windows.UI;

namespace XFiles.Visualizers.Visualizers
{
    public sealed class RadialSpectrumVisualizer : IAudioVisualizer
    {
        public string Name => "Radial Spectrum";
        public string Id => "radial-spectrum";

        private CanvasDevice _device;
        private float _width, _height, _time;
        private readonly float[] _smoothBands = new float[AudioData.BandCount];
        private readonly float[] _smoothPeaks = new float[AudioData.BandCount];
        private float _smoothBeat, _smoothBass;
        private const float SmoothFactor = 0.3f;
        private static readonly Color[] BandColors = GenerateBandColors();

        public void Initialize(CanvasDevice device) { _device = device; }

        public void Update(AudioData data, TimeSpan elapsed)
        {
            _time = data.Time;
            float bass = 0;
            for (int i = 0; i < 6; i++) bass += data.BandLevels[i]; bass /= 6f;
            _smoothBass += (bass - _smoothBass) * 0.3f;
            _smoothBeat += (data.Beat - _smoothBeat) * 0.4f;
            for (int i = 0; i < AudioData.BandCount; i++)
            {
                _smoothBands[i] += (data.BandLevels[i] - _smoothBands[i]) * SmoothFactor;
                _smoothPeaks[i] += (data.BandPeaks[i] - _smoothPeaks[i]) * SmoothFactor;
            }
        }

        public void Draw(CanvasDrawingSession ds)
        {
            if (_device == null || _width == 0 || _height == 0) return;
            ds.Clear(Color.FromArgb(255, 5, 5, 8));
            DrawPsychedelicBackground(ds);
            DrawRadialBars(ds);
            DrawPeaks(ds);
        }

        public void Resize(float width, float height) { _width = width; _height = height; }
        public void Dispose() { _device = null; }

        private void DrawRadialBars(CanvasDrawingSession ds)
        {
            float cx = _width * 0.5f, cy = _height * 0.5f;
            float minDim = Math.Min(_width, _height);
            float innerRadius = minDim * 0.15f;
            float maxBarHeight = minDim * 0.30f;
            float barWidth = (2f * (float)Math.PI * innerRadius) / AudioData.BandCount * 0.6f;
            var strokeStyle = new CanvasStrokeStyle { StartCap = CanvasCapStyle.Round, EndCap = CanvasCapStyle.Round };

            for (int i = 0; i < AudioData.BandCount; i++)
            {
                float angle = (float)i / AudioData.BandCount * 2f * (float)Math.PI - (float)Math.PI / 2f;
                float barHeight = Math.Max(1f, _smoothBands[i] * maxBarHeight);
                float cosA = (float)Math.Cos(angle), sinA = (float)Math.Sin(angle);
                float x1 = cx + cosA * innerRadius, y1 = cy + sinA * innerRadius;
                float x2 = cx + cosA * (innerRadius + barHeight), y2 = cy + sinA * (innerRadius + barHeight);
                ds.DrawLine(x1, y1, x2, y2, BandColors[i], barWidth, strokeStyle);
            }

            var circleGeo = CanvasGeometry.CreateCircle(ds, cx, cy, innerRadius - 2f);
            ds.DrawGeometry(circleGeo, Color.FromArgb(80, 255, 255, 255), 1.5f);
        }

        private void DrawPeaks(CanvasDrawingSession ds)
        {
            float cx = _width * 0.5f, cy = _height * 0.5f;
            float minDim = Math.Min(_width, _height);
            float innerRadius = minDim * 0.15f;
            float maxBarHeight = minDim * 0.30f;

            for (int i = 0; i < AudioData.BandCount; i++)
            {
                float peak = _smoothPeaks[i];
                if (peak < 0.05f) continue;
                float angle = (float)i / AudioData.BandCount * 2f * (float)Math.PI - (float)Math.PI / 2f;
                float r = innerRadius + peak * maxBarHeight;
                float px = cx + (float)Math.Cos(angle) * r, py = cy + (float)Math.Sin(angle) * r;
                var geo = CanvasGeometry.CreateCircle(ds, px, py, 2.5f);
                ds.FillGeometry(geo, Color.FromArgb(200, 255, 255, 255));
            }
        }

        private void DrawPsychedelicBackground(CanvasDrawingSession ds)
        {
            float cx = _width * 0.5f, cy = _height * 0.5f;
            float minDim = Math.Min(_width, _height);
            var strokeStyle = new CanvasStrokeStyle { StartCap = CanvasCapStyle.Round, EndCap = CanvasCapStyle.Round };

            // Pulsing concentric rings with color rotation
            for (int ring = 1; ring <= 5; ring++)
            {
                float ringR = minDim * 0.08f * ring * (1f + _smoothBass * 0.3f);
                float hue = (ring * 0.12f + _time * 0.06f) % 1.0f;
                Color c = HslToRgb(hue, 0.8f, 0.3f + _smoothBeat * 0.2f);
                byte a = (byte)(30 + _smoothBeat * 40);
                var geo = CanvasGeometry.CreateCircle(ds, cx, cy, ringR);
                ds.DrawGeometry(geo, Color.FromArgb(a, c.R, c.G, c.B), 1.5f);
            }

            // Rotating radial spokes with shifting hue
            int spokeCount = 16;
            float rotAngle = _time * 0.3f;
            for (int i = 0; i < spokeCount; i++)
            {
                float angle = (float)i / spokeCount * 2f * (float)Math.PI + rotAngle;
                float hue = ((float)i / spokeCount + _time * 0.05f) % 1.0f;
                Color c = HslToRgb(hue, 0.7f, 0.35f);
                byte a = (byte)(20 + _smoothBeat * 25);

                float len = minDim * 0.45f * (1f + _smoothBass * 0.2f);
                float x1 = cx + (float)Math.Cos(angle) * minDim * 0.06f;
                float y1 = cy + (float)Math.Sin(angle) * minDim * 0.06f;
                float x2 = cx + (float)Math.Cos(angle) * len;
                float y2 = cy + (float)Math.Sin(angle) * len;
                ds.DrawLine(x1, y1, x2, y2, Color.FromArgb(a, c.R, c.G, c.B), 1.2f, strokeStyle);
            }

            // Central glow that pulses with beat
            float glowR = minDim * 0.12f * (1f + _smoothBeat * 0.5f);
            float glowHue = (_time * 0.08f) % 1.0f;
            Color glowC = HslToRgb(glowHue, 0.9f, 0.4f);
            byte glowA = (byte)(15 + _smoothBeat * 30);
            ds.FillCircle(cx, cy, glowR, Color.FromArgb(glowA, glowC.R, glowC.G, glowC.B));
        }

        private static Color[] GenerateBandColors()
        {
            var colors = new Color[AudioData.BandCount];
            for (int i = 0; i < AudioData.BandCount; i++)
                colors[i] = HslToRgb(240f - (float)i / (AudioData.BandCount - 1) * 240f, 0.85f, 0.55f);
            return colors;
        }

        private static Color HslToRgb(float h, float s, float l)
        {
            h = h % 360f; if (h < 0f) h += 360f;
            float c = (1f - Math.Abs(2f * l - 1f)) * s;
            float x = c * (1f - Math.Abs((h / 60f) % 2f - 1f));
            float m = l - c / 2f;
            float r, g, b;
            if (h < 60) { r = c; g = x; b = 0; }
            else if (h < 120) { r = x; g = c; b = 0; }
            else if (h < 180) { r = 0; g = c; b = x; }
            else if (h < 240) { r = 0; g = x; b = c; }
            else if (h < 300) { r = x; g = 0; b = c; }
            else { r = c; g = 0; b = x; }
            return Color.FromArgb(255, (byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
        }

        public void ConfigurePipeline(PostProcessPipeline pipeline)
        {
            pipeline.Rotation = 0.3f;
            pipeline.FeedbackOpacity = 0.48f;
            pipeline.FeedbackZoom = 1.002f;
            pipeline.FeedbackDecay = 0f;
            pipeline.SlideX = 0f;
            pipeline.SlideY = 0f;
            pipeline.BloomAmount = 0.07f;
            pipeline.BloomBlur = 3f;
            pipeline.BloomThreshold = 0.45f;
            pipeline.ScanlinesEnabled = true;
            pipeline.ScanlineIntensity = 0.06f;
            pipeline.ScanlineCount = 500f;
        }
    }
}
