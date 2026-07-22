using System;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Windows.Foundation;
using Windows.UI;

namespace XFiles.Visualizers.Visualizers
{
    public sealed class FireParticlesVisualizer : IAudioVisualizer
    {
        public string Name => "Fire Particles";
        public string Id => "fire-particles";

        private CanvasDevice _device;
        private float _width, _height, _time, _deltaTime;

        private const int MaxParticles = 1200;
        private float[] _px, _py, _vx, _vy, _life, _maxLife, _size;
        private bool[] _active;

        private float _smoothBass, _smoothBeat, _smoothAvgFreq;
        private const float AudioSmooth = 0.25f;
        private int _nextParticle;
        private float _emitAccum, _burstCooldown;

        public void Initialize(CanvasDevice device) { _device = device; InitParticles(); }

        public void Update(AudioData data, TimeSpan elapsed)
        {
            float dt = (float)elapsed.TotalSeconds;
            _deltaTime = Math.Min(dt, 0.05f);
            _time = data.Time;

            float bass = 0;
            for (int i = 0; i < 6; i++) bass += data.BandLevels[i]; bass /= 6f;
            float avg = 0;
            for (int i = 0; i < AudioData.BandCount; i++) avg += data.BandLevels[i]; avg /= AudioData.BandCount;

            _smoothBass += (bass - _smoothBass) * AudioSmooth;
            _smoothBeat += (data.Beat - _smoothBeat) * 0.4f;
            _smoothAvgFreq += (avg - _smoothAvgFreq) * AudioSmooth;

            _burstCooldown -= _deltaTime;
            if (data.Beat > 0.55f && _burstCooldown <= 0)
            {
                EmitBurst(60 + (int)(_smoothBass * 60));
                _burstCooldown = 0.1f;
            }

            float emitRate = 15f + _smoothBass * 40f;
            _emitAccum += emitRate * _deltaTime;
            while (_emitAccum >= 1f) { EmitParticle(false); _emitAccum -= 1f; }

            for (int i = 0; i < MaxParticles; i++)
            {
                if (!_active[i]) continue;
                _life[i] -= _deltaTime;
                if (_life[i] <= 0) { _active[i] = false; continue; }
                float turbulence = (float)Math.Sin(_time * 3f + _px[i] * 0.01f) * 15f;
                _vx[i] += turbulence * _deltaTime;
                _vy[i] -= 40f * _deltaTime;
                _vx[i] *= 0.98f; _vy[i] *= 0.98f;
                _px[i] += _vx[i] * _deltaTime;
                _py[i] += _vy[i] * _deltaTime;
                float lifeRatio = _life[i] / _maxLife[i];
                _size[i] = Math.Max(0.5f, lifeRatio * (3f + _smoothBass * 4f));
            }
        }

        public void Draw(CanvasDrawingSession ds)
        {
            if (_device == null || _width == 0 || _height == 0) return;
            ds.Clear(Color.FromArgb(255, 5, 2, 1));
            DrawParticlesGlow(ds);
            DrawParticlesSharp(ds);
            DrawEmbers(ds);
        }

        public void Resize(float width, float height) { _width = width; _height = height; }
        public void Dispose() { _device = null; }

        private void InitParticles()
        {
            _px = new float[MaxParticles]; _py = new float[MaxParticles];
            _vx = new float[MaxParticles]; _vy = new float[MaxParticles];
            _life = new float[MaxParticles]; _maxLife = new float[MaxParticles];
            _size = new float[MaxParticles]; _active = new bool[MaxParticles];
        }

        private void EmitParticle(bool isBurst)
        {
            int i = _nextParticle;
            _nextParticle = (_nextParticle + 1) % MaxParticles;
            _active[i] = true;
            var rng = new Random();
            float spread = _width * 0.4f;
            _px[i] = _width * 0.5f + (float)(rng.NextDouble() * 2.0 - 1.0) * spread;
            _py[i] = _height * 0.85f + (float)rng.NextDouble() * _height * 0.15f;
            float speed = isBurst ? 200f + _smoothBass * 150f : 60f + _smoothBass * 50f;
            float angle = -((float)Math.PI * 0.5f) + (float)(rng.NextDouble() * 2.0 - 1.0) * (isBurst ? 1.2f : 0.6f);
            _vx[i] = (float)Math.Cos(angle) * speed;
            _vy[i] = (float)Math.Sin(angle) * speed;
            float life = isBurst ? 0.5f + (float)rng.NextDouble() * 0.8f : 0.7f + (float)rng.NextDouble() * 1.0f;
            _life[i] = life; _maxLife[i] = life;
            _size[i] = 3.5f + _smoothBass * 5f;
        }

        private void EmitBurst(int count) { for (int n = 0; n < count; n++) EmitParticle(true); }

        private void DrawParticlesGlow(CanvasDrawingSession ds)
        {
            for (int i = 0; i < MaxParticles; i++)
            {
                if (!_active[i]) continue;
                float lifeRatio = _life[i] / _maxLife[i];
                if (lifeRatio <= 0) continue;
                Color color = HeatmapColor(lifeRatio);
                byte a = (byte)Math.Min(255, (int)(255 * lifeRatio * 1.5f * 0.18f));
                float glowSz = _size[i] * 4f;
                ds.FillGeometry(CanvasGeometry.CreateCircle(ds, _px[i], _py[i], glowSz),
                    Color.FromArgb(a, color.R, color.G, color.B));
            }
        }

        private void DrawParticlesSharp(CanvasDrawingSession ds)
        {
            for (int i = 0; i < MaxParticles; i++)
            {
                if (!_active[i]) continue;
                float lifeRatio = _life[i] / _maxLife[i];
                if (lifeRatio <= 0) continue;
                Color color = HeatmapColor(lifeRatio);
                byte a = (byte)Math.Min(255, (int)(255 * lifeRatio * 1.5f));
                float sz = _size[i];
                ds.FillGeometry(CanvasGeometry.CreateCircle(ds, _px[i], _py[i], sz),
                    Color.FromArgb(a, color.R, color.G, color.B));
                if (sz > 3f && lifeRatio > 0.6f)
                {
                    byte ca = (byte)Math.Min(255, (int)(a * 0.8f));
                    ds.FillGeometry(CanvasGeometry.CreateCircle(ds, _px[i], _py[i], sz * 0.35f),
                        Color.FromArgb(ca, 255, 255, 200));
                }
            }
        }

        private void DrawEmbers(CanvasDrawingSession ds)
        {
            var rng = new Random((int)(_time * 7));
            int emberCount = 15 + (int)(_smoothBass * 10);
            for (int i = 0; i < emberCount; i++)
            {
                float x = (float)rng.NextDouble() * _width;
                float yBase = (float)rng.NextDouble() * _height * 0.8f;
                float yOffset = (float)Math.Sin(_time * 2f + i * 1.7f) * 30f;
                float y = yBase - (_time * 20f + i * 40f) % (_height * 0.8f) + yOffset;
                if (y < 0 || y > _height) continue;
                float flicker = 0.5f + (float)rng.NextDouble() * 0.5f;
                byte a = (byte)Math.Min(255, (int)(200 * flicker));
                float sz = 1f + (float)rng.NextDouble() * 1.5f;
                ds.FillGeometry(CanvasGeometry.CreateCircle(ds, x, y, sz), Color.FromArgb(a, 255, 180, 50));
            }
        }

        private static Color HeatmapColor(float t)
        {
            t = Math.Max(0, Math.Min(1, t));
            if (t > 0.8f) { float f = (t - 0.8f) / 0.2f; return Color.FromArgb(255, 255, (byte)(200 + f * 55), (byte)(100 + f * 100)); }
            if (t > 0.5f) { float f = (t - 0.5f) / 0.3f; return Color.FromArgb(255, 255, (byte)(120 + f * 80), (byte)(f * 100)); }
            if (t > 0.2f) { float f = (t - 0.2f) / 0.3f; return Color.FromArgb(255, (byte)(180 + f * 75), (byte)(f * 120), 0); }
            float f2 = t / 0.2f;
            return Color.FromArgb(255, (byte)(f2 * 180), 0, 0);
        }

        public void ConfigurePipeline(PostProcessPipeline pipeline)
        {
        }
    }
}
