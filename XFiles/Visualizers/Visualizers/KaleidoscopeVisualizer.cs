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

        private struct Particle
        {
            public float Angle, Radius, Size;
            public float Speed, Drift;
            public float Hue, Life;
            public float FallY;
        }

        private const int ParticleCount = 40;
        private readonly Particle[] _particles = new Particle[ParticleCount];
        private readonly Random _rand = new Random();
        private float _particleAccum;

        public void Initialize(CanvasDevice device)
        {
            _device = device;
            for (int i = 0; i < ParticleCount; i++)
                _particles[i] = RandomParticle();
        }

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

            float dt = Math.Min((float)elapsed.TotalSeconds, 0.05f);
            _particleAccum += dt * (8f + _smoothBass * 20f);
            while (_particleAccum >= 1f)
            {
                _particleAccum -= 1f;
                ShiftParticles();
                _particles[0] = RandomParticle();
            }

            for (int i = 0; i < ParticleCount; i++)
            {
                var p = _particles[i];
                p.Angle += p.Speed * dt * (1f + _smoothBass * 0.5f);
                p.Radius += p.Drift * dt;
                p.FallY += dt * (30f + _smoothMid * 80f);
                p.Life -= dt * 0.15f;
                if (p.Radius > 1f) p.Radius = 0.05f;
                if (p.Life <= 0f) p = RandomParticle();
                _particles[i] = p;
            }
        }

        public void Draw(CanvasDrawingSession ds)
        {
            if (_device == null || _width == 0 || _height == 0) return;
            ds.Clear(Color.FromArgb(255, 2, 0, 6));

            float cx = _width * 0.5f;
            float cy = _height * 0.5f;
            float maxRadius = Math.Max(_width, _height) * 0.75f;

            DrawMirrorTunnel(ds, cx, cy, maxRadius);
            DrawParticleField(ds, cx, cy, maxRadius);
            DrawBackgroundKaleidoscope(ds, cx, cy, maxRadius);
            DrawCrystalMandala(ds, cx, cy, maxRadius * 0.50f);
            DrawCoreAndRays(ds, cx, cy, maxRadius * 0.20f);
        }

        public void Resize(float width, float height) { _width = width; _height = height; }
        public void Dispose() { _device = null; }

        private void DrawMirrorTunnel(CanvasDrawingSession ds, float cx, float cy, float maxR)
        {
            float rot = _time * 0.03f;
            for (int r = 5; r >= 1; r--)
            {
                float t = (float)r / 5f;
                float radius = maxR * t;
                float hue = (t * 0.3f + _time * 0.02f) % 1f;
                var col = HslToRgb(hue, 0.5f, 0.08f + t * 0.06f);

                using (var geo = CanvasGeometry.CreateEllipse(ds, cx, cy, radius, radius))
                    ds.FillGeometry(geo, Color.FromArgb((byte)(20 * t), col.R, col.G, col.B));

                byte lineA = (byte)(6 + _smoothBeat * 8);
                ds.DrawEllipse(cx, cy, radius, radius,
                    Color.FromArgb(lineA, col.R, col.G, col.B), 0.5f);
            }

            float angleStep = (float)(Math.PI * 2.0 / Symmetry);
            for (int i = 0; i < Symmetry; i++)
            {
                float a = i * angleStep + rot;
                float hue = ((float)i / Symmetry + _time * 0.015f) % 1f;
                var col = HslToRgb(hue, 0.4f, 0.15f);
                float x = cx + (float)Math.Cos(a) * maxR;
                float y = cy + (float)Math.Sin(a) * maxR;
                ds.DrawLine(cx, cy, x, y, Color.FromArgb(8, col.R, col.G, col.B), 0.5f);
            }
        }

        private void DrawParticleField(CanvasDrawingSession ds, float cx, float cy, float maxR)
        {
            float angleStep = (float)(Math.PI * 2.0 / Symmetry);
            float beatPulse = 1f + _smoothBeat * 0.3f;

            for (int i = 0; i < ParticleCount; i++)
            {
                var p = _particles[i];
                float lifeFade = Math.Max(0f, p.Life);
                if (lifeFade < 0.05f) continue;

                for (int s = 0; s < Symmetry; s++)
                {
                    float mirrorAngle = s * angleStep + _time * 0.02f;
                    bool flip = s % 2 == 0;
                    float pa = flip ? p.Angle + mirrorAngle : mirrorAngle - p.Angle;
                    float r = p.Radius * maxR * 0.9f;
                    float x = cx + (float)Math.Cos(pa) * r;
                    float y = cy + (float)Math.Sin(pa) * r + p.FallY;

                    if (x < -50 || x > _width + 50 || y < -50 || y > _height + 50) continue;

                    float hue = (p.Hue + s * 0.03f + _time * 0.02f) % 1f;
                    var col = HslToRgb(hue, 0.9f, 0.3f + p.Life * 0.5f);
                    float size = p.Size * lifeFade * beatPulse;

                    ds.FillCircle(x, y, size, Color.FromArgb((byte)(120 * lifeFade), col.R, col.G, col.B));
                    ds.FillCircle(x, y, size * 0.3f, Color.FromArgb((byte)(60 * lifeFade), 255, 255, 220));
                }
            }
        }

        private void DrawBackgroundKaleidoscope(CanvasDrawingSession ds, float cx, float cy, float maxR)
        {
            float angleStep = (float)(Math.PI * 2.0 / Symmetry);
            float bgRotation = -_time * 0.06f;

            for (int ring = 6; ring >= 1; ring--)
            {
                float ringT = (float)ring / 6f;
                int bandIdx = Math.Min((int)(ringT * AudioData.BandCount), AudioData.BandCount - 1);
                float level = _smoothBands[bandIdx];
                float r = maxR * ringT * (0.4f + level * 0.9f);

                float hue = (ringT * 0.35f + _time * 0.025f) % 1f;
                Color baseColor = HslToRgb(hue, 0.7f, 0.20f + level * 0.30f);
                byte alpha = (byte)(60 + level * 100);

                for (int i = 0; i < Symmetry; i++)
                {
                    float dir = (i % 2 == 0) ? 1f : -1f;
                    float a1 = i * angleStep + bgRotation * dir;
                    float a2 = a1 + angleStep * 0.75f;

                    float pSize = (12f + level * 30f) * ringT;

                    _polyBuffer[0] = new Vector2(cx + MathF.Cos(a1) * (r - pSize), cy + MathF.Sin(a1) * (r - pSize));
                    _polyBuffer[1] = new Vector2(cx + MathF.Cos(a2) * r, cy + MathF.Sin(a2) * r);
                    _polyBuffer[2] = new Vector2(cx + MathF.Cos(a1) * (r + pSize), cy + MathF.Sin(a1) * (r + pSize));
                    _polyBuffer[3] = new Vector2(cx + MathF.Cos(a2) * (r - pSize), cy + MathF.Sin(a2) * (r - pSize));

                    using (var geo = CanvasGeometry.CreatePolygon(ds, _polyBuffer))
                        ds.FillGeometry(geo, Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B));
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
                float rInner = radius * 0.15f;
                float rOuter = radius * (0.5f + level * 0.6f);

                float hue = ((float)i / Symmetry + _time * 0.05f) % 1f;
                Color col = HslToRgb(hue, 0.9f, 0.35f + level * 0.4f);

                Vector2 pCenter = new Vector2(cx + MathF.Cos(baseAngle + angleStep * 0.5f) * rOuter,
                                              cy + MathF.Sin(baseAngle + angleStep * 0.5f) * rOuter);
                Vector2 pLeft = new Vector2(cx + MathF.Cos(baseAngle) * rInner,
                                            cy + MathF.Sin(baseAngle) * rInner);
                Vector2 pRight = new Vector2(cx + MathF.Cos(nextAngle) * rInner,
                                             cy + MathF.Sin(nextAngle) * rInner);

                _triBuffer[0] = pLeft;
                _triBuffer[1] = pCenter;
                _triBuffer[2] = pRight;

                using (var geo = CanvasGeometry.CreatePolygon(ds, _triBuffer))
                    ds.FillGeometry(geo, Color.FromArgb(160, col.R, col.G, col.B));

                float orbRadius = 2f + level * 7f;
                ds.FillCircle(pCenter, orbRadius, Color.FromArgb(200, 255, 255, 220));
            }
        }

        private void DrawCoreAndRays(CanvasDrawingSession ds, float cx, float cy, float coreR)
        {
            float beatPulse = 1f + _smoothBeat * 0.6f;
            float r = coreR * beatPulse;

            Color coreCol = HslToRgb((_time * 0.08f) % 1f, 1f, 0.6f);

            ds.FillCircle(cx, cy, r * 3f, Color.FromArgb(20, coreCol.R, coreCol.G, coreCol.B));
            ds.FillCircle(cx, cy, r * 2f, Color.FromArgb(45, coreCol.R, coreCol.G, coreCol.B));
            ds.FillCircle(cx, cy, r * 1.2f, Color.FromArgb(80, coreCol.R, coreCol.G, coreCol.B));
            ds.FillCircle(cx, cy, r, coreCol);
            ds.FillCircle(cx, cy, r * 0.3f, Colors.White);

            float angleStep = (float)(Math.PI * 2.0 / Symmetry);
            float rayRot = _time * 0.2f;
            for (int i = 0; i < Symmetry; i++)
            {
                float a = i * angleStep + rayRot;
                float len = r * (2.5f + _smoothTreble * 2f);
                float x = cx + MathF.Cos(a) * len;
                float y = cy + MathF.Sin(a) * len;
                ds.DrawLine(cx, cy, x, y, Color.FromArgb((byte)(30 + _smoothBeat * 30), coreCol.R, coreCol.G, coreCol.B), 0.8f);
            }
        }

        private Particle RandomParticle()
        {
            return new Particle
            {
                Angle = (float)(_rand.NextDouble() * Math.PI * 2),
                Radius = 0.05f + (float)_rand.NextDouble() * 0.3f,
                Size = 2f + (float)_rand.NextDouble() * 6f,
                Speed = (0.3f + (float)_rand.NextDouble() * 1.5f) * (_rand.Next(2) == 0 ? 1 : -1),
                Drift = 0.05f + (float)_rand.NextDouble() * 0.15f,
                Hue = (float)_rand.NextDouble(),
                Life = 0.5f + (float)_rand.NextDouble() * 0.8f,
                FallY = -(float)_rand.NextDouble() * 100f
            };
        }

        private void ShiftParticles()
        {
            for (int i = ParticleCount - 1; i > 0; i--)
                _particles[i] = _particles[i - 1];
        }

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
            pipeline.Rotation = 0.003f;
            pipeline.FeedbackOpacity = 0.55f;
            pipeline.FeedbackZoom = 1.012f;
            pipeline.FeedbackDecay = 0.015f;
            pipeline.BloomAmount = 0.08f;
            pipeline.BloomBlur = 3f;
            pipeline.BloomThreshold = 0.35f;
            pipeline.VignetteEnabled = true;
        }
    }
}
