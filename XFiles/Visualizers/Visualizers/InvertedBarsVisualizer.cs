using System;
using Microsoft.Graphics.Canvas;
using Windows.UI;

namespace XFiles.Visualizers.Visualizers
{
    public sealed class InvertedBarsVisualizer : IAudioVisualizer
    {
        public string Name => "Inverted Bars";
        public string Id => "inverted-bars";

        private CanvasDevice _device;
        private float _width, _height, _time;

        private const int Cols = 16;
        private const int Rows = 12;

        private readonly float[] _smoothBands = new float[AudioData.BandCount];
        private float _smoothBass, _smoothBeat, _smoothAvg;
        private const float AudioSmooth = 0.18f;

        public void Initialize(CanvasDevice device) { _device = device; }

        public void Update(AudioData data, TimeSpan elapsed)
        {
            if (data.BandLevels == null || data.BandLevels.Length == 0) return;
            _time = data.Time;
            float dt = (float)elapsed.TotalSeconds;

            float bass = 0f, avg = 0f;
            int halfBands = Math.Min(6, data.BandLevels.Length);
            for (int i = 0; i < halfBands; i++) bass += data.BandLevels[i];
            bass /= halfBands;
            for (int i = 0; i < Math.Min(AudioData.BandCount, data.BandLevels.Length); i++)
                avg += data.BandLevels[i];
            avg /= AudioData.BandCount;

            _smoothBass += (bass - _smoothBass) * AudioSmooth;
            _smoothBeat += (data.Beat - _smoothBeat) * 0.35f;
            _smoothAvg += (avg - _smoothAvg) * AudioSmooth;

            for (int i = 0; i < Math.Min(AudioData.BandCount, data.BandLevels.Length); i++)
                _smoothBands[i] += (data.BandLevels[i] - _smoothBands[i]) * AudioSmooth;
        }

        public void Draw(CanvasDrawingSession ds)
        {
            if (_device == null || _width == 0 || _height == 0) return;

            float margin = _width * 0.04f;
            float areaW = _width - margin * 2f;
            float areaH = _height - margin * 2f;
            float tileW = areaW / Cols;
            float tileH = areaH / Rows;
            float gap = Math.Min(tileW, tileH) * 0.1f;
            float beatPulse = 1f + _smoothBeat * 0.2f;
            float hueOffset = (_time * 0.025f) % 1f;

            for (int c = 0; c < Cols; c++)
            {
                float bandCenter = (float)c / Cols * AudioData.BandCount;
                int b0 = (int)bandCenter;
                int b1 = Math.Min(b0 + 1, AudioData.BandCount - 1);
                float bf = bandCenter - b0;
                float bandVal = _smoothBands[b0] * (1f - bf) + _smoothBands[b1] * bf;

                for (int r = 0; r < Rows; r++)
                {
                    float threshold = (float)r / Rows;
                    float intensity = (bandVal - threshold) * 2.5f;
                    intensity = Math.Max(0f, Math.Min(1f, intensity * beatPulse));

                    float x = margin + c * tileW;
                    float y = margin + r * tileH;
                    float pw = tileW - gap;
                    float ph = tileH - gap;
                    float scale = 0.25f + intensity * 0.75f;
                    float sw = pw * scale;
                    float sh = ph * scale;
                    float sx = x + (pw - sw) * 0.5f;
                    float sy = y + (ph - sh) * 0.5f;

                    float hue = (hueOffset + (float)c / Cols * 0.55f) % 1f;
                    float sat = 0.65f + intensity * 0.35f;
                    float light = 0.08f + intensity * 0.75f;
                    var color = HslToRgb(hue, sat, light);

                    byte a = (byte)(160 + (int)(intensity * 95));
                    ds.FillRectangle(sx, sy, sw, sh, Color.FromArgb(a, color.R, color.G, color.B));
                }
            }
        }

        public void Resize(float width, float height) { _width = width; _height = height; }
        public void Dispose() { _device = null; }

        private static Color HslToRgb(float h, float s, float l)
        {
            h -= MathF.Floor(h); float hue = h * 360f;
            float c = (1f - MathF.Abs(2f * l - 1f)) * s;
            float x = c * (1f - MathF.Abs((hue / 60f) % 2f - 1f));
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
            pipeline.FeedbackOpacity = 0.06f;
            pipeline.FeedbackZoom = 1.0002f;
            pipeline.BloomAmount = 0.3f;
            pipeline.BloomBlur = 2f;
            pipeline.BloomThreshold = 0.25f;
            pipeline.VignetteEnabled = true;
        }
    }
}
