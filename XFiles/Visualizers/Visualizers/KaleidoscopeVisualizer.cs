using System;
using System.Numerics;
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
        private const float AudioSmooth = 0.25f;

        private const int Symmetry = 12;
        private readonly Vector2[] _polyBuffer = new Vector2[4];
        private readonly Vector2[] _triBuffer = new Vector2[3];

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

            ds.Clear(Color.FromArgb(255, 3, 1, 8));

            float cx = _width * 0.5f;
            float cy = _height * 0.5f;
            float maxRadius = Math.Max(_width, _height) * 0.7f;

            DrawBackgroundKaleidoscope(ds, cx, cy, maxRadius);
            DrawReflectedLines(ds, cx, cy, maxRadius);
            DrawCrystalMandala(ds, cx, cy, maxRadius * 0.55f);
            DrawCoreAndRays(ds, cx, cy, maxRadius * 0.25f);
        }

        public void Resize(float width, float height) { _width = width; _height = height; }
        public void Dispose() { _device = null; }

        private void DrawBackgroundKaleidoscope(CanvasDrawingSession ds, float cx, float cy, float maxR)
        {
            float angleStep = (float)(Math.PI * 2.0 / Symmetry);
            float bgRotation = -_time * 0.08f;

            int ringCount = 6;
            for (int ring = ringCount; ring >= 1; ring--)
            {
                float ringT = (float)ring / ringCount;
                int bandIdx = Math.Min((int)(ringT * AudioData.BandCount), AudioData.BandCount - 1);
                float level = _smoothBands[bandIdx];
                float r = maxR * ringT * (0.5f + level * 0.8f);

                float hue = (ringT * 0.4f + _time * 0.03f) % 1.0f;
                Color baseColor = HslToRgb(hue, 0.8f, 0.25f + level * 0.35f);
                byte alpha = (byte)(100 + level * 100);

                for (int i = 0; i < Symmetry; i++)
                {
                    float dir = (i % 2 == 0) ? 1f : -1f;
                    float a1 = i * angleStep + bgRotation * dir;
                    float a2 = a1 + angleStep * 0.8f;

                    float pSize = (15f + level * 35f) * ringT;

                    _polyBuffer[0] = new Vector2(cx + (float)Math.Cos(a1) * (r - pSize), cy + (float)Math.Sin(a1) * (r - pSize));
                    _polyBuffer[1] = new Vector2(cx + (float)Math.Cos(a2) * r, cy + (float)Math.Sin(a2) * r);
                    _polyBuffer[2] = new Vector2(cx + (float)Math.Cos(a1) * (r + pSize), cy + (float)Math.Sin(a1) * (r + pSize));
                    _polyBuffer[3] = new Vector2(cx + (float)Math.Cos(a2) * (r - pSize), cy + (float)Math.Sin(a2) * (r - pSize));

                    using (var geo = Microsoft.Graphics.Canvas.Geometry.CanvasGeometry.CreatePolygon(ds, _polyBuffer))
                    {
                        ds.FillGeometry(geo, Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B));
                    }
                }
            }
        }

        private void DrawCrystalMandala(CanvasDrawingSession ds, float cx, float cy, float radius)
        {
            float angleStep = (float)(Math.PI * 2.0 / Symmetry);
            float fgRotation = _time * 0.12f + _smoothMid * 0.2f;

            for (int i = 0; i < Symmetry; i++)
            {
                float baseAngle = i * angleStep + fgRotation;
                float nextAngle = baseAngle + angleStep;

                float level = _smoothBands[(i * 3) % AudioData.BandCount];
                float rInner = radius * 0.2f;
                float rOuter = radius * (0.6f + level * 0.5f);

                float hue = ((float)i / Symmetry + _time * 0.05f) % 1.0f;
                Color col = HslToRgb(hue, 0.9f, 0.4f + level * 0.4f);

                Vector2 pCenter = new Vector2(cx + (float)Math.Cos(baseAngle + angleStep * 0.5f) * rOuter,
                                              cy + (float)Math.Sin(baseAngle + angleStep * 0.5f) * rOuter);
                Vector2 pLeft = new Vector2(cx + (float)Math.Cos(baseAngle) * rInner,
                                            cy + (float)Math.Sin(baseAngle) * rInner);
                Vector2 pRight = new Vector2(cx + (float)Math.Cos(nextAngle) * rInner,
                                             cy + (float)Math.Sin(nextAngle) * rInner);

                _triBuffer[0] = pLeft;
                _triBuffer[1] = pCenter;
                _triBuffer[2] = pRight;

                using (var geo = Microsoft.Graphics.Canvas.Geometry.CanvasGeometry.CreatePolygon(ds, _triBuffer))
                {
                    ds.FillGeometry(geo, Color.FromArgb(180, col.R, col.G, col.B));
                }

                float orbRadius = 3f + level * 8f;
                ds.FillCircle(pCenter, orbRadius, Color.FromArgb(220, 255, 255, 200));
            }
        }

        private void DrawCoreAndRays(CanvasDrawingSession ds, float cx, float cy, float coreR)
        {
            float beatPulse = 1.0f + _smoothBeat * 0.5f;
            float r = coreR * beatPulse;

            Color coreCol = HslToRgb((_time * 0.1f) % 1.0f, 1.0f, 0.65f);

            ds.FillCircle(cx, cy, r * 2.5f, Color.FromArgb(25, coreCol.R, coreCol.G, coreCol.B));
            ds.FillCircle(cx, cy, r * 1.5f, Color.FromArgb(60, coreCol.R, coreCol.G, coreCol.B));
            ds.FillCircle(cx, cy, r, coreCol);
            ds.FillCircle(cx, cy, r * 0.4f, Colors.White);
        }

        private void DrawReflectedLines(CanvasDrawingSession ds, float cx, float cy, float maxR)
        {
            float angleStep = (float)(Math.PI * 2.0 / Symmetry);
            float lineRotation = _time * 0.15f;
            var strokeStyle = new Microsoft.Graphics.Canvas.Geometry.CanvasStrokeStyle
            {
                StartCap = CanvasCapStyle.Round,
                EndCap = CanvasCapStyle.Round
            };

            for (int ring = 1; ring <= 4; ring++)
            {
                float ringR = maxR * ring * 0.15f;
                int bandIdx = Math.Min((int)(ring * AudioData.BandCount * 0.25f), AudioData.BandCount - 1);
                float level = _smoothBands[bandIdx];
                float hue = (ring * 0.15f + _time * 0.04f) % 1.0f;
                Color c = HslToRgb(hue, 0.8f, 0.35f + level * 0.3f);
                byte a = (byte)(40 + level * 80);

                for (int i = 0; i < Symmetry; i++)
                {
                    float a1 = i * angleStep + lineRotation;
                    float a2 = a1 + angleStep * 0.4f;

                    float innerR = ringR * 0.6f;
                    float outerR = ringR * (1.2f + level * 0.5f);

                    float lx1 = cx + (float)Math.Cos(a1) * innerR;
                    float ly1 = cy + (float)Math.Sin(a1) * innerR;
                    float lx2 = cx + (float)Math.Cos(a1) * outerR;
                    float ly2 = cy + (float)Math.Sin(a1) * outerR;
                    ds.DrawLine(lx1, ly1, lx2, ly2, Color.FromArgb(a, c.R, c.G, c.B), 0.8f + level * 1.2f, strokeStyle);

                    float mx1 = cx + (float)Math.Cos(a2) * innerR;
                    float my1 = cy + (float)Math.Sin(a2) * innerR;
                    float mx2 = cx + (float)Math.Cos(a2) * outerR;
                    float my2 = cy + (float)Math.Sin(a2) * outerR;
                    ds.DrawLine(mx1, my1, mx2, my2, Color.FromArgb((byte)(a / 2), c.R, c.G, c.B), 0.5f, strokeStyle);

                    float crossR = (innerR + outerR) * 0.5f;
                    float crossAngle = a1 + angleStep * 0.2f;
                    float cvx = cx + (float)Math.Cos(crossAngle) * crossR;
                    float cvy = cy + (float)Math.Sin(crossAngle) * crossR;
                    float cvSize = 3f + level * 8f;
                    ds.FillCircle(cvx, cvy, cvSize, Color.FromArgb((byte)(a / 2), c.R, c.G, c.B));
                }
            }

            for (int i = 0; i < Symmetry; i++)
            {
                float angle = i * angleStep + lineRotation + (float)Math.PI / Symmetry;
                float len = maxR * 0.85f;
                float hue = ((float)i / Symmetry + _time * 0.03f) % 1.0f;
                Color lineColor = HslToRgb(hue, 0.6f, 0.3f);
                byte lineA = (byte)(25 + _smoothBeat * 30);

                float x1 = cx + (float)Math.Cos(angle) * maxR * 0.1f;
                float y1 = cy + (float)Math.Sin(angle) * maxR * 0.1f;
                float x2 = cx + (float)Math.Cos(angle) * len;
                float y2 = cy + (float)Math.Sin(angle) * len;
                ds.DrawLine(x1, y1, x2, y2, Color.FromArgb(lineA, lineColor.R, lineColor.G, lineColor.B), 0.4f, strokeStyle);
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
            pipeline.Rotation = 0.005f;
            pipeline.SlideX = 0f;
            pipeline.SlideY = 0f;
            pipeline.FeedbackOpacity = 0.58f;
            pipeline.FeedbackZoom = 1.015f;
            pipeline.FeedbackDecay = 0.02f;
            pipeline.BloomAmount = 0.07f;
            pipeline.BloomBlur = 3f;
            pipeline.BloomThreshold = 0.4f;
        }
    }
}
