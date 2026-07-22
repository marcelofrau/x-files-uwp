using System;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Windows.Foundation;
using Windows.UI;

namespace XFiles.Visualizers.Visualizers
{
    public sealed class KaleidoscopeVisualizer : IAudioVisualizer
    {
        public string Name => "Kaleidoscope";
        public string Id => "kaleidoscope";

        private CanvasDevice _device;
        private float _width, _height, _time;

        private readonly float[] _smoothBands = new float[AudioData.BandCount];
        private float _smoothBeat, _smoothBass, _smoothMid, _smoothTreble;
        private const float AudioSmooth = 0.3f;

        private const int Segments = 12;
        private const int RaysPerSegment = 4;
        private const int MaxRings = 8;

        public void Initialize(CanvasDevice device) { _device = device; }

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
            for (int i = 0; i < AudioData.BandCount; i++)
                _smoothBands[i] += (data.BandLevels[i] - _smoothBands[i]) * AudioSmooth;
        }

        public void Draw(CanvasDrawingSession ds)
        {
            if (_device == null || _width == 0 || _height == 0) return;
            ds.Clear(Color.FromArgb(255, 3, 2, 5));

            float cx = _width * 0.5f, cy = _height * 0.5f;
            float minDim = Math.Min(_width, _height);

            DrawCenterGlow(ds, cx, cy, minDim);

            float segAngle = 2f * (float)Math.PI / Segments;

            for (int seg = 0; seg < Segments; seg++)
            {
                float baseAngle = seg * segAngle + _time * 0.15f;

                for (int ray = 0; ray < RaysPerSegment; ray++)
                {
                    int bandIdx = (seg * RaysPerSegment + ray) * AudioData.BandCount / (Segments * RaysPerSegment);
                    bandIdx = Math.Min(bandIdx, AudioData.BandCount - 1);
                    float level = _smoothBands[bandIdx];

                    float rayAngle = baseAngle + (float)ray / RaysPerSegment * segAngle;
                    float rayLen = minDim * 0.35f * (0.3f + level * 0.7f);
                    float innerR = minDim * 0.04f;
                    float hue = ((float)seg / Segments + _time * 0.06f) % 1.0f;
                    float sat = 0.8f + _smoothBeat * 0.2f;
                    float lum = 0.35f + level * 0.4f;

                    Color c = HslToRgb(hue, sat, lum);
                    float cosA = (float)Math.Cos(rayAngle), sinA = (float)Math.Sin(rayAngle);
                    float cosA2 = (float)Math.Cos(rayAngle + segAngle * 0.3f);
                    float sinA2 = (float)Math.Sin(rayAngle + segAngle * 0.3f);

                    var geo = CanvasGeometry.CreatePolygon(ds, new[]
                    {
                        new System.Numerics.Vector2(cx + cosA * innerR, cy + sinA * innerR),
                        new System.Numerics.Vector2(cx + cosA2 * innerR, cy + sinA2 * innerR),
                        new System.Numerics.Vector2(cx + cosA2 * rayLen, cy + sinA2 * rayLen),
                        new System.Numerics.Vector2(cx + cosA * rayLen, cy + sinA * rayLen)
                    });
                    byte a = (byte)Math.Min(255, (int)(180 + level * 75));
                    ds.FillGeometry(geo, Color.FromArgb(a, c.R, c.G, c.B));

                    // Glow layer (wider, dimmer) for blur effect
                    float glowScale = 1.4f;
                    var glowGeo = CanvasGeometry.CreatePolygon(ds, new[]
                    {
                        new System.Numerics.Vector2(cx + cosA * innerR * glowScale, cy + sinA * innerR * glowScale),
                        new System.Numerics.Vector2(cx + cosA2 * innerR * glowScale, cy + sinA2 * innerR * glowScale),
                        new System.Numerics.Vector2(cx + cosA2 * rayLen * glowScale, cy + sinA2 * rayLen * glowScale),
                        new System.Numerics.Vector2(cx + cosA * rayLen * glowScale, cy + sinA * rayLen * glowScale)
                    });
                    byte ga = (byte)Math.Min(255, (int)(40 * level));
                    ds.FillGeometry(glowGeo, Color.FromArgb(ga, c.R, c.G, c.B));

                    DrawRings(ds, cx, cy, rayAngle, segAngle, innerR, rayLen, seg, level);
                }
            }

            DrawMirrorDots(ds, cx, cy, minDim);
        }

        private void DrawCenterGlow(CanvasDrawingSession ds, float cx, float cy, float minDim)
        {
            float r = minDim * 0.06f * (1f + _smoothBeat * 0.5f);
            Color c = HslToRgb((_time * 0.08f) % 1.0f, 1f, 0.8f + _smoothBeat * 0.2f);
            var outerGeo = CanvasGeometry.CreateEllipse(ds, cx, cy, r * 5f, r * 5f);
            ds.FillGeometry(outerGeo, Color.FromArgb(15, c.R, c.G, c.B));
            var geo = CanvasGeometry.CreateEllipse(ds, cx, cy, r * 3f, r * 3f);
            ds.FillGeometry(geo, Color.FromArgb(30, c.R, c.G, c.B));
            var coreGeo = CanvasGeometry.CreateEllipse(ds, cx, cy, r, r);
            ds.FillGeometry(coreGeo, c);
            var brightGeo = CanvasGeometry.CreateEllipse(ds, cx, cy, r * 0.35f, r * 0.35f);
            ds.FillGeometry(brightGeo, Colors.White);
        }

        private void DrawRings(CanvasDrawingSession ds, float cx, float cy,
            float rayAngle, float segAngle, float innerR, float outerR,
            int seg, float level)
        {
            for (int ring = 1; ring <= MaxRings; ring++)
            {
                float ringT = (float)ring / MaxRings;
                float ringR = innerR + (outerR - innerR) * ringT;
                float ringLevel = _smoothBands[Math.Min((int)(ringT * AudioData.BandCount), AudioData.BandCount - 1)];
                if (ringLevel < 0.08f) continue;

                float hue = ((float)seg / Segments + ringT * 0.2f + _time * 0.06f) % 1.0f;
                float lum = 0.2f + ringLevel * 0.5f;
                Color c = HslToRgb(hue, 0.85f, lum);
                byte a = (byte)Math.Min(255, (int)(120 * ringLevel));

                float dotX = cx + (float)Math.Cos(rayAngle) * ringR;
                float dotY = cy + (float)Math.Sin(rayAngle) * ringR;
                float dotR = 1.5f + ringLevel * 3f;

                var geo = CanvasGeometry.CreateCircle(ds, dotX, dotY, dotR);
                ds.FillGeometry(geo, Color.FromArgb(a, c.R, c.G, c.B));
            }
        }

        private void DrawMirrorDots(CanvasDrawingSession ds, float cx, float cy, float minDim)
        {
            float segAngle = 2f * (float)Math.PI / Segments;
            for (int seg = 0; seg < Segments; seg++)
            {
                float angle = seg * segAngle + _time * 0.15f + segAngle * 0.5f;
                float r = minDim * 0.25f * (1f + _smoothBass * 0.3f);
                float x = cx + (float)Math.Cos(angle) * r;
                float y = cy + (float)Math.Sin(angle) * r;
                float hue = ((float)seg / Segments + _time * 0.04f) % 1.0f;
                Color c = HslToRgb(hue, 0.9f, 0.7f);
                var geo = CanvasGeometry.CreateCircle(ds, x, y, 2f + _smoothBeat * 2f);
                ds.FillGeometry(geo, c);
            }
        }

        public void Resize(float width, float height) { _width = width; _height = height; }
        public void Dispose() { _device = null; }

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
