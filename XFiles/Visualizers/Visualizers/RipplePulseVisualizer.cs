using System;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Windows.Foundation;
using Windows.UI;

namespace XFiles.Visualizers.Visualizers
{
    public sealed class RipplePulseVisualizer : IAudioVisualizer
    {
        public string Name => "Ripple Pulse";
        public string Id => "ripple-pulse";

        private CanvasDevice _device;
        private float _width, _height, _time;

        private float _smoothBass, _smoothMid, _smoothTreble, _smoothBeat;
        private const float AudioSmooth = 0.3f;

        private const int MaxRipples = 12;
        private float[] _rippleRadius, _rippleAlpha, _rippleHue;
        private int _nextRipple;
        private float _rippleCooldown;

        public void Initialize(CanvasDevice device) { _device = device; InitRipples(); }

        public void Update(AudioData data, TimeSpan elapsed)
        {
            _time = data.Time;
            float bass = 0, mid = 0, treble = 0;
            for (int i = 0; i < 6; i++) bass += data.BandLevels[i]; bass /= 6f;
            for (int i = 10; i < 16; i++) mid += data.BandLevels[i]; mid /= 6f;
            for (int i = 20; i < 26; i++) treble += data.BandLevels[i]; treble /= 6f;
            _smoothBass += (bass - _smoothBass) * AudioSmooth;
            _smoothMid += (mid - _smoothMid) * AudioSmooth;
            _smoothTreble += (treble - _smoothTreble) * AudioSmooth;
            _smoothBeat += (data.Beat - _smoothBeat) * 0.4f;

            float maxR = Math.Max(_width, _height) * 0.7f;
            float speed = 150f + _smoothBass * 200f;

            for (int i = 0; i < MaxRipples; i++)
            {
                if (_rippleAlpha[i] <= 0) continue;
                _rippleRadius[i] += speed * (float)elapsed.TotalSeconds;
                _rippleAlpha[i] -= 0.25f * (float)elapsed.TotalSeconds;
                if (_rippleAlpha[i] < 0) _rippleAlpha[i] = 0;
            }

            _rippleCooldown -= (float)elapsed.TotalSeconds;
            if (data.Beat > 0.6f && _rippleCooldown <= 0)
            {
                SpawnRipple();
                _rippleCooldown = 0.08f;
            }

            if (_rippleCooldown <= 0 && _smoothBass > 0.4f)
            {
                SpawnRipple();
                _rippleCooldown = 0.15f;
            }
        }

        public void Draw(CanvasDrawingSession ds)
        {
            if (_device == null || _width == 0 || _height == 0) return;
            ds.Clear(Color.FromArgb(255, 2, 2, 5));

            float cx = _width * 0.5f, cy = _height * 0.5f;

            DrawRipples(ds, cx, cy);
            DrawCenterOrb(ds, cx, cy);
            DrawRadialGlows(ds, cx, cy);
        }

        public void Resize(float width, float height) { _width = width; _height = height; }
        public void Dispose() { _device = null; }

        private void InitRipples()
        {
            _rippleRadius = new float[MaxRipples];
            _rippleAlpha = new float[MaxRipples];
            _rippleHue = new float[MaxRipples];
        }

        private void SpawnRipple()
        {
            _rippleRadius[_nextRipple] = 5f;
            _rippleAlpha[_nextRipple] = 0.9f;
            _rippleHue[_nextRipple] = (_time * 0.1f + _nextRipple * 0.08f) % 1.0f;
            _nextRipple = (_nextRipple + 1) % MaxRipples;
        }

        private void DrawRipples(CanvasDrawingSession ds, float cx, float cy)
        {
            for (int i = 0; i < MaxRipples; i++)
            {
                float alpha = _rippleAlpha[i];
                if (alpha <= 0) continue;
                float r = _rippleRadius[i];
                float hue = _rippleHue[i];

                Color c = HslToRgb(hue, 0.85f, 0.5f + alpha * 0.3f);
                byte a = (byte)(alpha * 180);
                var geo = CanvasGeometry.CreateEllipse(ds, cx, cy, r, r);
                ds.DrawGeometry(geo, Color.FromArgb(a, c.R, c.G, c.B), 2f + alpha * 2f);

                // Extra-wide glow pass
                Color gc = HslToRgb(hue, 0.8f, 0.4f);
                byte ga = (byte)(alpha * 25);
                var glowGeo = CanvasGeometry.CreateEllipse(ds, cx, cy, r * 1.15f, r * 1.15f);
                ds.DrawGeometry(glowGeo, Color.FromArgb(ga, gc.R, gc.G, gc.B), 10f);

                // Outer haze pass
                byte ha = (byte)(alpha * 12);
                var hazeGeo = CanvasGeometry.CreateEllipse(ds, cx, cy, r * 1.3f, r * 1.3f);
                ds.DrawGeometry(hazeGeo, Color.FromArgb(ha, gc.R, gc.G, gc.B), 18f);
            }
        }

        private void DrawCenterOrb(CanvasDrawingSession ds, float cx, float cy)
        {
            float r = 8f + _smoothBass * 15f + _smoothBeat * 10f;
            float hue = (_time * 0.08f) % 1.0f;
            Color c = HslToRgb(hue, 1f, 0.75f + _smoothBeat * 0.25f);

            ds.FillGeometry(CanvasGeometry.CreateEllipse(ds, cx, cy, r * 5f, r * 5f),
                Color.FromArgb(10, c.R, c.G, c.B));
            ds.FillGeometry(CanvasGeometry.CreateEllipse(ds, cx, cy, r * 3f, r * 3f),
                Color.FromArgb(15, c.R, c.G, c.B));
            ds.FillGeometry(CanvasGeometry.CreateEllipse(ds, cx, cy, r * 1.5f, r * 1.5f),
                Color.FromArgb(40, c.R, c.G, c.B));
            ds.FillGeometry(CanvasGeometry.CreateEllipse(ds, cx, cy, r, r), c);
            ds.FillGeometry(CanvasGeometry.CreateEllipse(ds, cx, cy, r * 0.3f, r * 0.3f), Colors.White);

            // Lens flare streaks
            float flareLen = r * 5f * (1f + _smoothBeat * 0.5f);
            byte flareAlpha = (byte)Math.Min(255, (int)(50 + _smoothBeat * 70));
            var fStyle = new CanvasStrokeStyle { StartCap = CanvasCapStyle.Round, EndCap = CanvasCapStyle.Round };
            Color fColor = Color.FromArgb(flareAlpha, c.R, c.G, c.B);
            ds.DrawLine(cx - flareLen, cy, cx + flareLen, cy, fColor, 1.5f, fStyle);
            ds.DrawLine(cx, cy - flareLen, cx, cy + flareLen, fColor, 1.5f, fStyle);
            float d = flareLen * 0.6f;
            byte da2 = (byte)(flareAlpha / 2);
            ds.DrawLine(cx - d, cy - d, cx + d, cy + d, Color.FromArgb(da2, c.R, c.G, c.B), 1f, fStyle);
            ds.DrawLine(cx + d, cy - d, cx - d, cy + d, Color.FromArgb(da2, c.R, c.G, c.B), 1f, fStyle);
        }

        private void DrawRadialGlows(CanvasDrawingSession ds, float cx, float cy)
        {
            float maxR = Math.Min(_width, _height) * 0.45f;
            var strokeStyle = new CanvasStrokeStyle { StartCap = CanvasCapStyle.Round, EndCap = CanvasCapStyle.Round };
            int lineCount = 16;
            for (int i = 0; i < lineCount; i++)
            {
                float angle = (float)i / lineCount * 2f * (float)Math.PI;
                int bandIdx = (int)((float)i / lineCount * AudioData.BandCount) % AudioData.BandCount;
                float level = _smoothBeat * 0.5f;
                if (level < 0.05f) continue;
                float innerR = 20f + _smoothBass * 30f;
                float outerR = innerR + maxR * 0.3f * level;
                Color c = HslToRgb((float)i / lineCount, 0.7f, 0.5f);
                byte a = (byte)(level * 100);
                ds.DrawLine(
                    cx + (float)Math.Cos(angle) * innerR, cy + (float)Math.Sin(angle) * innerR,
                    cx + (float)Math.Cos(angle) * outerR, cy + (float)Math.Sin(angle) * outerR,
                    Color.FromArgb(a, c.R, c.G, c.B), 1.5f, strokeStyle);
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
        }
    }
}
