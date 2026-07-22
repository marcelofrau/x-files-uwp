using System;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Windows.Foundation;
using Windows.UI;

namespace XFiles.Visualizers.Visualizers
{
    public sealed class ParticleBurstVisualizer : IAudioVisualizer
    {
        public string Name => "Particle Burst";
        public string Id => "particle-burst";

        private CanvasDevice _device;
        private float _width, _height, _time, _deltaTime;

        private const int MaxParticles = 1500;
        private float[] _px, _py, _vx, _vy, _life, _maxLife, _size;
        private float[] _hue;
        private bool[] _active;

        private float _smoothBass, _smoothBeat, _smoothMid;
        private const float AudioSmooth = 0.3f;
        private int _nextParticle;
        private float _burstCooldown;

        private const float Gravity = 120f;
        private const float TrailAlpha = 0.12f;

        public void Initialize(CanvasDevice device) { _device = device; InitParticles(); }

        public void Update(AudioData data, TimeSpan elapsed)
        {
            float dt = (float)elapsed.TotalSeconds;
            _deltaTime = Math.Min(dt, 0.05f);
            _time = data.Time;

            float bass = 0, mid = 0;
            for (int i = 0; i < 6; i++) bass += data.BandLevels[i]; bass /= 6f;
            for (int i = 10; i < 16; i++) mid += data.BandLevels[i]; mid /= 6f;
            _smoothBass += (bass - _smoothBass) * AudioSmooth;
            _smoothMid += (mid - _smoothMid) * AudioSmooth;
            _smoothBeat += (data.Beat - _smoothBeat) * 0.4f;

            _burstCooldown -= _deltaTime;
            if (data.Beat > 0.55f && _burstCooldown <= 0)
            {
                EmitBurst(80 + (int)(_smoothBass * 80));
                _burstCooldown = 0.1f;
            }

            float emitRate = 10f + _smoothBass * 30f;
            float emitAccum = emitRate * _deltaTime;
            while (emitAccum >= 1f) { EmitAmbient(); emitAccum -= 1f; }

            for (int i = 0; i < MaxParticles; i++)
            {
                if (!_active[i]) continue;
                _life[i] -= _deltaTime;
                if (_life[i] <= 0) { _active[i] = false; continue; }
                _vy[i] += Gravity * _deltaTime;
                _vx[i] *= 0.995f;
                _vy[i] *= 0.995f;
                _px[i] += _vx[i] * _deltaTime;
                _py[i] += _vy[i] * _deltaTime;
                float lifeRatio = _life[i] / _maxLife[i];
                _size[i] = Math.Max(0.3f, lifeRatio * (2.5f + _smoothBass * 3f));
            }
        }

        public void Draw(CanvasDrawingSession ds)
        {
            if (_device == null || _width == 0 || _height == 0) return;

            ds.FillRectangle(0, 0, _width, _height, Color.FromArgb(255, 3, 2, 6));

            DrawTrailLayer(ds);
            DrawParticlesGlow(ds);
            DrawParticlesSharp(ds);
        }

        public void Resize(float width, float height) { _width = width; _height = height; }
        public void Dispose() { _device = null; }

        private void InitParticles()
        {
            _px = new float[MaxParticles]; _py = new float[MaxParticles];
            _vx = new float[MaxParticles]; _vy = new float[MaxParticles];
            _life = new float[MaxParticles]; _maxLife = new float[MaxParticles];
            _size = new float[MaxParticles]; _hue = new float[MaxParticles];
            _active = new bool[MaxParticles];
        }

        private void EmitBurst(int count)
        {
            float cx = _width * 0.5f, cy = _height * 0.5f;
            float baseHue = (_time * 0.15f) % 1.0f;
            for (int n = 0; n < count; n++)
            {
                int i = _nextParticle;
                _nextParticle = (_nextParticle + 1) % MaxParticles;
                _active[i] = true;
                float angle = (float)(new Random().NextDouble() * 2.0 * Math.PI);
                float speed = 100f + (float)new Random().NextDouble() * 300f + _smoothBass * 120f;
                _px[i] = cx + (float)Math.Cos(angle) * 5f;
                _py[i] = cy + (float)Math.Sin(angle) * 5f;
                _vx[i] = (float)Math.Cos(angle) * speed;
                _vy[i] = (float)Math.Sin(angle) * speed;
                float life = 0.6f + (float)new Random().NextDouble() * 1.2f;
                _life[i] = life; _maxLife[i] = life;
                _size[i] = 2f + _smoothBass * 3f;
                _hue[i] = (baseHue + (float)new Random().NextDouble() * 0.15f) % 1.0f;
            }
        }

        private void EmitAmbient()
        {
            int i = _nextParticle;
            _nextParticle = (_nextParticle + 1) % MaxParticles;
            _active[i] = true;
            var rng = new Random();
            _px[i] = (float)rng.NextDouble() * _width;
            _py[i] = _height * 0.7f + (float)rng.NextDouble() * _height * 0.3f;
            _vx[i] = (float)(rng.NextDouble() * 2.0 - 1.0) * 20f;
            _vy[i] = -(20f + (float)rng.NextDouble() * 60f);
            float life = 0.4f + (float)rng.NextDouble() * 0.6f;
            _life[i] = life; _maxLife[i] = life;
            _size[i] = 1f + (float)rng.NextDouble() * 2f;
            _hue[i] = (0.05f + (float)rng.NextDouble() * 0.1f) % 1.0f;
        }

        private void DrawTrailLayer(CanvasDrawingSession ds)
        {
            for (int i = 0; i < MaxParticles; i++)
            {
                if (!_active[i]) continue;
                float lifeRatio = _life[i] / _maxLife[i];
                if (lifeRatio <= 0) continue;
                Color c = HslToRgb(_hue[i], 0.85f, 0.5f + lifeRatio * 0.3f);
                float trailLen = 8f + lifeRatio * 15f;
                float dx = -_vx[i] * 0.015f;
                float dy = -_vy[i] * 0.015f;
                float len = (float)Math.Sqrt(dx * dx + dy * dy);
                if (len > 0.01f) { dx /= len; dy /= len; }
                byte a = (byte)(TrailAlpha * 255 * lifeRatio);
                ds.DrawLine(_px[i], _py[i], _px[i] + dx * trailLen, _py[i] + dy * trailLen,
                    Color.FromArgb(a, c.R, c.G, c.B), _size[i] * 0.8f);
            }
        }

        private void DrawParticlesGlow(CanvasDrawingSession ds)
        {
            for (int i = 0; i < MaxParticles; i++)
            {
                if (!_active[i]) continue;
                float lifeRatio = _life[i] / _maxLife[i];
                if (lifeRatio <= 0) continue;
                Color c = HslToRgb(_hue[i], 0.85f, 0.5f + lifeRatio * 0.3f);
                byte a = (byte)(lifeRatio * 80);
                float glowSz = _size[i] * 5f;
                ds.FillGeometry(CanvasGeometry.CreateCircle(ds, _px[i], _py[i], glowSz),
                    Color.FromArgb(a, c.R, c.G, c.B));
            }
        }

        private void DrawParticlesSharp(CanvasDrawingSession ds)
        {
            for (int i = 0; i < MaxParticles; i++)
            {
                if (!_active[i]) continue;
                float lifeRatio = _life[i] / _maxLife[i];
                if (lifeRatio <= 0) continue;
                Color c = HslToRgb(_hue[i], 0.9f, 0.55f + lifeRatio * 0.35f);
                byte a = (byte)Math.Min(255, (int)(255 * lifeRatio));
                ds.FillGeometry(CanvasGeometry.CreateCircle(ds, _px[i], _py[i], _size[i]),
                    Color.FromArgb(a, c.R, c.G, c.B));
                if (lifeRatio > 0.7f && _size[i] > 1.5f)
                {
                    ds.FillGeometry(CanvasGeometry.CreateCircle(ds, _px[i], _py[i], _size[i] * 0.35f),
                        Color.FromArgb((byte)Math.Min(255, a + 30), 255, 255, 220));
                }
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
    }
}
