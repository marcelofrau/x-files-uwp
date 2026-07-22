using System;
using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Geometry;
using Windows.Foundation;
using Windows.UI;

namespace XFiles.Visualizers.Visualizers
{
    /// <summary>
    /// Radial spectrum visualizer: 26 bars arranged in a circle, each bar = 1 frequency band.
    /// Height reactive to BandLevels. Colors shift by frequency (blue → green → yellow → red).
    /// Glow effect via Gaussian blur blend.
    /// </summary>
    public sealed class RadialSpectrumVisualizer : IAudioVisualizer
    {
        public string Name => "Radial Spectrum";
        public string Id => "radial-spectrum";

        private CanvasDevice _device;
        private float _width;
        private float _height;
        private float _time;

        // Smoothed band levels for animation (prevents jitter)
        private readonly float[] _smoothBands = new float[AudioData.BandCount];
        private readonly float[] _smoothPeaks = new float[AudioData.BandCount];
        private const float SmoothFactor = 0.3f;

        // Offscreen render target — persists across frames for effect chain
        private CanvasRenderTarget _offscreen;

        // Band colors: HSL gradient from blue (240°) to red (0°)
        private static readonly Color[] BandColors = GenerateBandColors();

        public void Initialize(CanvasDevice device)
        {
            _device = device;
        }

        public void Update(AudioData data, TimeSpan elapsed)
        {
            _time = data.Time;

            for (int i = 0; i < AudioData.BandCount; i++)
            {
                _smoothBands[i] += (data.BandLevels[i] - _smoothBands[i]) * SmoothFactor;
                _smoothPeaks[i] += (data.BandPeaks[i] - _smoothPeaks[i]) * SmoothFactor;
            }
        }

        public ICanvasImage GetImage()
        {
            if (_device == null || _width == 0 || _height == 0)
                return null;

            // Recreate offscreen target if size changed
            if (_offscreen == null || _offscreen.Size.Width != _width || _offscreen.Size.Height != _height)
            {
                _offscreen?.Dispose();
                _offscreen = new CanvasRenderTarget(_device, _width, _height, 96);
            }

            // Draw bars to offscreen target
            using (var ds = _offscreen.CreateDrawingSession())
            {
                ds.Clear(Windows.UI.Color.FromArgb(255, 5, 5, 8));
                DrawRadialBars(ds);
                DrawPeaks(ds);
            }

            // Compose: sharp bars + blurred glow
            var blur = new GaussianBlurEffect
            {
                Source = _offscreen,
                BlurAmount = 12f,
                BorderMode = EffectBorderMode.Hard
            };

            return new BlendEffect
            {
                Background = _offscreen,
                Foreground = blur,
                Mode = BlendEffectMode.Screen
            };
        }

        public void Resize(float width, float height)
        {
            _width = width;
            _height = height;
        }

        private void DrawRadialBars(CanvasDrawingSession ds)
        {
            float centerX = _width * 0.5f;
            float centerY = _height * 0.5f;
            float minDim = Math.Min(_width, _height);
            float innerRadius = minDim * 0.15f;
            float maxBarHeight = minDim * 0.30f;
            float barWidth = (2f * (float)Math.PI * innerRadius) / AudioData.BandCount * 0.6f;

            for (int i = 0; i < AudioData.BandCount; i++)
            {
                float angle = (float)i / AudioData.BandCount * 2f * (float)Math.PI - (float)Math.PI / 2f;
                float level = _smoothBands[i];
                float barHeight = level * maxBarHeight;

                if (barHeight < 1f) barHeight = 1f;

                float cosA = (float)Math.Cos(angle);
                float sinA = (float)Math.Sin(angle);

                float x1 = centerX + cosA * innerRadius;
                float y1 = centerY + sinA * innerRadius;
                float x2 = centerX + cosA * (innerRadius + barHeight);
                float y2 = centerY + sinA * (innerRadius + barHeight);

                var color = BandColors[i];
                var stroke = new CanvasStrokeStyle
                {
                    StartCap = CanvasCapStyle.Round,
                    EndCap = CanvasCapStyle.Round
                };

                ds.DrawLine(x1, y1, x2, y2, color, barWidth, stroke);
            }

            // Inner circle outline
            var circleGeo = CanvasGeometry.CreateCircle(ds, centerX, centerY, innerRadius - 2f);
            ds.DrawGeometry(circleGeo, Color.FromArgb(80, 255, 255, 255), 1.5f);
        }

        private void DrawPeaks(CanvasDrawingSession ds)
        {
            float centerX = _width * 0.5f;
            float centerY = _height * 0.5f;
            float minDim = Math.Min(_width, _height);
            float innerRadius = minDim * 0.15f;
            float maxBarHeight = minDim * 0.30f;

            for (int i = 0; i < AudioData.BandCount; i++)
            {
                float peak = _smoothPeaks[i];
                if (peak < 0.05f) continue;

                float angle = (float)i / AudioData.BandCount * 2f * (float)Math.PI - (float)Math.PI / 2f;
                float peakRadius = innerRadius + peak * maxBarHeight;

                float cosA = (float)Math.Cos(angle);
                float sinA = (float)Math.Sin(angle);

                float px = centerX + cosA * peakRadius;
                float py = centerY + sinA * peakRadius;

                var peakGeo = CanvasGeometry.CreateCircle(ds, px, py, 2.5f);
                ds.FillGeometry(peakGeo, Color.FromArgb(200, 255, 255, 255));
            }
        }

        private static Color[] GenerateBandColors()
        {
            var colors = new Color[AudioData.BandCount];
            for (int i = 0; i < AudioData.BandCount; i++)
            {
                float t = (float)i / (AudioData.BandCount - 1);
                float hue = 240f - t * 240f;
                colors[i] = HslToRgb(hue, 0.85f, 0.55f);
            }
            return colors;
        }

        private static Color HslToRgb(float h, float s, float l)
        {
            h = h % 360f;
            if (h < 0f) h += 360f;
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

            return Color.FromArgb(255,
                (byte)((r + m) * 255),
                (byte)((g + m) * 255),
                (byte)((b + m) * 255));
        }

        public void Dispose()
        {
            _offscreen?.Dispose();
            _offscreen = null;
            _device = null;
        }
    }
}
