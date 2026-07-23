using System;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Windows.Foundation;
using Windows.UI;

namespace XFiles.Visualizers.Visualizers
{
    public sealed class NeonGlareVisualizer : IAudioVisualizer
    {
        public string Name => "Neon Glare";
        public string Id => "neon-glare";

        private CanvasDevice _device;
        private float _width, _height, _time;

        private readonly float[] _smoothBands = new float[AudioData.BandCount];
        private float _smoothBeat;
        private float _smoothSubBass;
        private float _beatFlash;
        private const float SmoothFactor = 0.35f;

        private const int BarCount = 32;
        private const float BarGapRatio = 0.15f;
        private const float GlowWidthMul = 2.0f;
        private const float GlowAlphaMul = 0.10f;
        private const float BeatFlareDecay = 0.88f;

        public void Initialize(CanvasDevice device) { _device = device; }

        public void Update(AudioData data, TimeSpan elapsed)
        {
            _time = data.Time;
            _smoothBeat += (data.Beat - _smoothBeat) * 0.4f;
            _beatFlash *= BeatFlareDecay;

            float subBass = 0;
            for (int i = 0; i < 3; i++) subBass += data.BandLevels[i];
            _smoothSubBass += (subBass / 3f - _smoothSubBass) * 0.25f;

            if (data.Beat > 0.7f) _beatFlash = Math.Min(1f, _beatFlash + data.Beat * 0.6f);

            for (int i = 0; i < AudioData.BandCount; i++)
                _smoothBands[i] += (data.BandLevels[i] - _smoothBands[i]) * SmoothFactor;
        }

        public void Draw(CanvasDrawingSession ds)
        {
            if (_device == null || _width == 0 || _height == 0) return;

            float bgPulse = 0.5f + _smoothSubBass * 0.5f;
            byte bgB = (byte)(12 + bgPulse * 15);
            byte bgR = (byte)(5 + bgPulse * 8);
            ds.Clear(Color.FromArgb(255, bgR, 5, bgB));

            float barAreaWidth = _width * 0.88f;
            float startX = (_width - barAreaWidth) * 0.5f;
            float maxBarHeight = _height * 0.65f;
            float barWidth = barAreaWidth / BarCount * (1f - BarGapRatio);
            float gap = barAreaWidth / BarCount * BarGapRatio;
            float bottomY = _height * 0.82f;

            for (int i = 0; i < BarCount; i++)
            {
                int bandIdx = (int)((float)i / BarCount * AudioData.BandCount);
                bandIdx = Math.Min(bandIdx, AudioData.BandCount - 1);
                float level = _smoothBands[bandIdx];
                float barH = Math.Max(2f, level * maxBarHeight);
                float x = startX + i * (barWidth + gap);
                float barT = (float)i / (BarCount - 1);

                Color barColor = HslToRgb(300f - barT * 180f, 0.85f + _smoothBeat * 0.1f, 0.5f + level * 0.2f);

                Color glowColor = Color.FromArgb(
                    (byte)(barColor.A * GlowAlphaMul),
                    barColor.R, barColor.G, barColor.B);
                float glowW = barWidth * GlowWidthMul;
                float glowX = x - (glowW - barWidth) * 0.5f;
                ds.FillRectangle(glowX, bottomY - barH - 4f, glowW, barH + 8f, glowColor);

                ds.FillRectangle(x, bottomY - barH, barWidth, barH, barColor);

                Color topColor = Color.FromArgb(220,
                    (byte)Math.Min(255, (int)(barColor.R * 1.4f)),
                    (byte)Math.Min(255, (int)(barColor.G * 1.4f)),
                    (byte)Math.Min(255, (int)(barColor.B * 1.4f)));
                ds.FillRectangle(x, bottomY - barH, barWidth, 3f, topColor);
            }

            if (_beatFlash > 0.05f)
            {
                DrawLensFlare(ds, _width * 0.5f, bottomY - maxBarHeight * 0.5f, _beatFlash);
            }

            ds.DrawLine(startX - 10f, bottomY, startX + barAreaWidth + 10f, bottomY, Color.FromArgb(60, 0, 255, 255), 1.5f);
        }

        public void Resize(float width, float height) { _width = width; _height = height; }
        public void Dispose() { _device = null; }

        private void DrawLensFlare(CanvasDrawingSession ds, float cx, float cy, float intensity)
        {
            float radius = 60f * intensity;
            var flareGeo = CanvasGeometry.CreateEllipse(ds, cx, cy, radius, radius);
            Color flareColor = Color.FromArgb((byte)(intensity * 25), 255, 200, 255);
            ds.FillGeometry(flareGeo, flareColor);

            float lineLen = radius * 1.5f;
            var strokeStyle = new CanvasStrokeStyle { StartCap = CanvasCapStyle.Round, EndCap = CanvasCapStyle.Round };
            int lineCount = 8;
            for (int i = 0; i < lineCount; i++)
            {
                float angle = (float)i / lineCount * 2f * (float)Math.PI;
                float ex = cx + (float)Math.Cos(angle) * lineLen;
                float ey = cy + (float)Math.Sin(angle) * lineLen;
                byte a = (byte)(intensity * 30);
                ds.DrawLine(cx, cy, ex, ey, Color.FromArgb(a, 255, 200, 255), 1f, strokeStyle);
            }
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
            pipeline.BloomAmount = 0.01f;
            pipeline.BloomBlur = 1.5f;
            pipeline.BloomThreshold = 0.7f;
        }
    }
}
