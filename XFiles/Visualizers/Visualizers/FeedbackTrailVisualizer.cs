using System;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Windows.Foundation;
using Windows.UI;

namespace XFiles.Visualizers.Visualizers
{
    public sealed class FeedbackTrailVisualizer : IAudioVisualizer
    {
        public string Name => "Feedback Trail";
        public string Id => "feedback-trail";

        private CanvasDevice _device;
        private float _width, _height, _time;
        private Random _rng;

        private readonly float[] _smoothBands = new float[AudioData.BandCount];
        private float _smoothBass, _smoothMid, _smoothTreble, _smoothBeat, _smoothAvg;
        private const float AudioSmooth = 0.25f;

        private const int PointsPerSegment = 80;
        private float _rotation;
        private float _driftX, _driftY;

        private float[,] _trailX, _trailY;
        private int _trailHead;
        private const int TrailDepth = 35;
        private readonly float[] _frameX = new float[PointsPerSegment];
        private readonly float[] _frameY = new float[PointsPerSegment];
        private readonly float[] _frameA = new float[PointsPerSegment];

        private struct Particle
        {
            public float X, Y, VX, VY, Life, MaxLife, Size;
            public float Hue;
        }
        private const int MaxParticles = 60;
        private readonly Particle[] _particles = new Particle[MaxParticles];
        private float _explodeTimer;

        public void Initialize(CanvasDevice device)
        {
            _device = device;
            _rng = new Random();
            _trailX = new float[TrailDepth, PointsPerSegment];
            _trailY = new float[TrailDepth, PointsPerSegment];
        }

        public void Update(AudioData data, TimeSpan elapsed)
        {
            _time = data.Time;
            float dt = (float)elapsed.TotalSeconds;

            float bass = 0, mid = 0, treble = 0, avg = 0;
            for (int i = 0; i < 6; i++) bass += data.BandLevels[i]; bass /= 6f;
            for (int i = 10; i < 16; i++) mid += data.BandLevels[i]; mid /= 6f;
            for (int i = 20; i < 26; i++) treble += data.BandLevels[i]; treble /= 6f;
            for (int i = 0; i < AudioData.BandCount; i++) avg += data.BandLevels[i]; avg /= AudioData.BandCount;
            _smoothBass += (bass - _smoothBass) * AudioSmooth;
            _smoothMid += (mid - _smoothMid) * AudioSmooth;
            _smoothTreble += (treble - _smoothTreble) * AudioSmooth;
            _smoothBeat += (data.Beat - _smoothBeat) * 0.4f;
            _smoothAvg += (avg - _smoothAvg) * AudioSmooth;
            for (int i = 0; i < AudioData.BandCount; i++)
                _smoothBands[i] += (data.BandLevels[i] - _smoothBands[i]) * AudioSmooth;

            _rotation += (0.5f + _smoothBass * 2f + _smoothBeat * 1.5f) * dt;

            float driftSpeed = 0.8f + _smoothBass * 1.5f;
            _driftX += (float)Math.Sin(_time * driftSpeed * 0.5f) * 300f * dt;
            _driftY += (float)Math.Cos(_time * driftSpeed * 0.4f) * 250f * dt;
            _driftX += (float)Math.Sin(_time * 0.1f) * 120f * dt;
            _driftY += (float)Math.Cos(_time * 0.13f) * 100f * dt;
            _driftX += (float)Math.Sin(_time * 2.5f + _smoothBass * 4f) * 180f * dt * _smoothBass;
            _driftY += (float)Math.Cos(_time * 1.9f + _smoothBeat * 5f) * 150f * dt * _smoothBeat;
            float maxDrift = Math.Min(_width, _height) * 0.50f;
            _driftX = Math.Clamp(_driftX, -maxDrift, maxDrift);
            _driftY = Math.Clamp(_driftY, -maxDrift, maxDrift);

            ComputeCurrentFrame(_frameX, _frameY, _frameA);

            for (int t = TrailDepth - 1; t > 0; t--)
                for (int p = 0; p < PointsPerSegment; p++)
                {
                    _trailX[t, p] = _trailX[t - 1, p];
                    _trailY[t, p] = _trailY[t - 1, p];
                }
            for (int p = 0; p < PointsPerSegment; p++)
            {
                _trailX[0, p] = _frameX[p];
                _trailY[0, p] = _frameY[p];
            }

            UpdateParticles(dt);
        }

        public void Draw(CanvasDrawingSession ds)
        {
            if (_device == null || _width == 0 || _height == 0) return;
            ds.Clear(Color.FromArgb(255, 2, 2, 5));

            float cx = _width * 0.5f + _driftX, cy = _height * 0.5f + _driftY;

            for (int t = TrailDepth - 1; t >= 0; t--)
            {
                float age = (float)t / TrailDepth;
                float alpha = (1f - age) * 0.6f;
                float hue = (_time * 0.05f + age * 0.25f + _smoothBeat * 0.1f) % 1.0f;
                float lum = 0.25f + (1f - age) * 0.5f + _smoothBeat * 0.15f;
                Color c = HslToRgb(hue, 0.9f, lum);
                byte a = (byte)(alpha * 255);
                float thickness = (1.5f + (1f - age) * 5f) * (1f + _smoothBeat * 0.8f + _smoothBass * 0.5f);

                var strokeStyle = AudioVisualizerBase.RoundCapStroke;
                for (int p = 0; p < PointsPerSegment - 1; p++)
                {
                    float fade = 0.5f + 0.5f * _frameA[p];
                    byte pa = (byte)(a * fade);
                    ds.DrawLine(_trailX[t, p], _trailY[t, p],
                        _trailX[t, p + 1], _trailY[t, p + 1],
                        Color.FromArgb(pa, c.R, c.G, c.B), thickness, strokeStyle);
                }
            }

            DrawCenterFlash(ds, cx, cy);
            DrawParticles(ds);
        }

        public void Resize(float width, float height) { _width = width; _height = height; }
        public void Dispose() { _device = null; }

        private void ComputeCurrentFrame(float[] frameX, float[] frameY, float[] frameA)
        {
            float cx = _width * 0.5f + _driftX, cy = _height * 0.5f + _driftY;
            float minDim = Math.Min(_width, _height);
            float baseRadius = minDim * 0.45f;
            float beatPulse = 1f + _smoothBeat * 0.6f;

            // Triângulo rotativo deformável (3 lóbulos principais)
            for (int p = 0; p < PointsPerSegment; p++)
            {
                float t = (float)p / PointsPerSegment;
                float angle = _rotation + t * 2f * (float)Math.PI;

                int bandIdx = Math.Min((int)(t * AudioData.BandCount), AudioData.BandCount - 1);
                float level = _smoothBands[bandIdx];

                // Deformação multimodo
                float wobble1 = (float)Math.Sin(t * 5f + _time * 2.5f) * 30f * (1f + level * 2f);
                float wobble2 = (float)Math.Cos(t * 7f - _time * 1.8f + _smoothBass * 3f) * 20f * _smoothBass;
                float wobble3 = (float)Math.Sin(angle * 3f + _time * 1.2f) * 25f * (1f + _smoothMid);
                float bassPulse = (float)Math.Sin(t * 4f + _time * 3f) * 30f * _smoothBass;

                float radius = baseRadius * beatPulse * (0.4f + level * 0.8f) + wobble1 + wobble2 + wobble3 + bassPulse;
                radius = Math.Max(radius, 5f);

                frameX[p] = cx + (float)Math.Cos(angle) * radius;
                frameY[p] = cy + (float)Math.Sin(angle) * radius;
                frameA[p] = level;
            }
        }

        private void DrawCenterFlash(CanvasDrawingSession ds, float cx, float cy)
        {
            float r = 8f + _smoothBeat * 18f + _smoothBass * 10f;
            float hue = (_time * 0.1f + _smoothBeat * 0.2f) % 1.0f;
            Color c = HslToRgb(hue, 1f, 0.9f);
            ds.FillEllipse(cx, cy, r * 3f, r * 3f,
                Color.FromArgb(30, c.R, c.G, c.B));
            ds.FillEllipse(cx, cy, r, r, c);
            ds.FillEllipse(cx, cy, r * 0.25f, r * 0.25f, Colors.White);

            // Ring pulse
            float ringR = r * 4f * (1f + _smoothBeat * 0.5f);
            ds.DrawCircle(cx, cy, ringR,
                Color.FromArgb((byte)(40 * _smoothBeat), 255, 255, 255), 2f);
        }

        private void UpdateParticles(float dt)
        {
            float cx = _width * 0.5f + _driftX, cy = _height * 0.5f + _driftY;

            // Explosão em beats fortes
            _explodeTimer -= dt;
            if ((_smoothBeat > 0.5f && _explodeTimer <= 0) || _smoothBeat > 0.8f)
            {
                _explodeTimer = 0.15f + (float)_rng.NextDouble() * 0.2f;
                SpawnExplosion(cx, cy);
            }

            // Explosão aleatória ocasional
            if (_rng.NextDouble() < 0.02f)
                SpawnExplosion(
                    cx + (float)(_rng.NextDouble() - 0.5) * _width * 0.8f,
                    cy + (float)(_rng.NextDouble() - 0.5) * _height * 0.8f);

            for (int i = 0; i < MaxParticles; i++)
            {
                if (_particles[i].Life <= 0) continue;
                _particles[i].X += _particles[i].VX * dt;
                _particles[i].Y += _particles[i].VY * dt;
                _particles[i].VY += 60f * dt;
                _particles[i].VX *= (1f - dt * 2f);
                _particles[i].VY *= (1f - dt * 2f);
                _particles[i].Life -= dt;
            }
        }

        private void SpawnExplosion(float cx, float cy)
        {
            int count = 8 + (int)(_smoothBeat * 20) + _rng.Next(6);
            float hueBase = (float)_rng.NextDouble();

            for (int i = 0; i < MaxParticles; i++)
            {
                if (_particles[i].Life > 0) continue;
                if (count <= 0) break;
                count--;

                float angle = (float)(_rng.NextDouble() * Math.PI * 2);
                float speed = 80f + (float)_rng.NextDouble() * 300f + _smoothBass * 200f;
                _particles[i].X = cx + (float)(_rng.NextDouble() - 0.5) * 20f;
                _particles[i].Y = cy + (float)(_rng.NextDouble() - 0.5) * 20f;
                _particles[i].VX = MathF.Cos(angle) * speed;
                _particles[i].VY = MathF.Sin(angle) * speed;
                _particles[i].Life = 0.5f + (float)_rng.NextDouble() * 1.0f;
                _particles[i].MaxLife = _particles[i].Life;
                _particles[i].Size = 1.5f + (float)_rng.NextDouble() * 4f;
                _particles[i].Hue = (hueBase + (float)_rng.NextDouble() * 0.3f) % 1f;
            }
        }

        private void DrawParticles(CanvasDrawingSession ds)
        {
            for (int i = 0; i < MaxParticles; i++)
            {
                if (_particles[i].Life <= 0) continue;
                float age = 1f - _particles[i].Life / _particles[i].MaxLife;
                float alpha = (1f - age) * 0.8f;
                float size = _particles[i].Size * (1f - age * 0.5f);

                float hue = (_particles[i].Hue + age * 0.2f + _time * 0.05f) % 1f;
                Color c = HslToRgb(hue, 1f, 0.7f - age * 0.2f);
                ds.FillCircle(_particles[i].X, _particles[i].Y, size,
                    Color.FromArgb((byte)(alpha * 255), c.R, c.G, c.B));

                if (size > 2f)
                    ds.FillCircle(_particles[i].X, _particles[i].Y, size * 2f,
                        Color.FromArgb((byte)(alpha * 60), c.R, c.G, c.B));
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
            pipeline.FeedbackOpacity = 0.75f;
            pipeline.FeedbackZoom = 1.02f;
            pipeline.FeedbackDecay = 0.025f;
            pipeline.BloomAmount = 0.45f;
            pipeline.BloomBlur = 6f;
            pipeline.BloomThreshold = 0.08f;
            pipeline.VignetteEnabled = true;
        }
    }
}
