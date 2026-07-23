using System;
using Microsoft.Graphics.Canvas;
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

        private readonly Random _rng = new Random();

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
                EmitBurst(40 + (int)(_smoothBass * 50));
                _burstCooldown = 0.08f;
            }

            float emitRate = 25f + _smoothBass * 60f;
            _emitAccum += emitRate * _deltaTime;
            while (_emitAccum >= 1f) { EmitParticle(false); _emitAccum -= 1f; }

            for (int i = 0; i < MaxParticles; i++)
            {
                if (!_active[i]) continue;
                _life[i] -= _deltaTime;
                if (_life[i] <= 0) { _active[i] = false; continue; }

                float turbulence = (float)Math.Sin(_time * 6f + _py[i] * 0.02f + i) * 25f;
                _vx[i] += turbulence * _deltaTime;

                float lifeRatio = _life[i] / _maxLife[i];
                _vy[i] -= (30f + lifeRatio * 50f) * _deltaTime;

                _vx[i] *= 0.97f;
                _vy[i] *= 0.98f;

                _px[i] += _vx[i] * _deltaTime;
                _py[i] += _vy[i] * _deltaTime;

                _size[i] = Math.Max(0.5f, (0.2f + 0.8f * lifeRatio) * (4f + _smoothBass * 6f));
            }
        }

        public void Draw(CanvasDrawingSession ds)
        {
            if (_device == null || _width == 0 || _height == 0) return;

            ds.Clear(Color.FromArgb(255, 4, 1, 2));

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

            float spread = _width * 0.35f;
            _px[i] = _width * 0.5f + (float)(_rng.NextDouble() * 2.0 - 1.0) * spread;
            _py[i] = _height * 0.92f + (float)_rng.NextDouble() * _height * 0.08f;

            float speed = isBurst ? 220f + _smoothBass * 180f : 70f + _smoothBass * 60f;
            float angle = -((float)Math.PI * 0.5f) + (float)(_rng.NextDouble() * 2.0 - 1.0) * (isBurst ? 1.0f : 0.4f);

            _vx[i] = (float)Math.Cos(angle) * speed;
            _vy[i] = (float)Math.Sin(angle) * speed;

            float life = isBurst ? 0.4f + (float)_rng.NextDouble() * 0.6f : 0.6f + (float)_rng.NextDouble() * 0.8f;
            _life[i] = life;
            _maxLife[i] = life;
            _size[i] = 4f + _smoothBass * 6f;
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
                byte a = (byte)Math.Min(255, (int)(255 * lifeRatio * 0.22f));
                float glowSz = _size[i] * 3.5f;

                ds.FillCircle(_px[i], _py[i], glowSz, Color.FromArgb(a, color.R, color.G, color.B));
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
                byte a = (byte)Math.Min(255, (int)(255 * (0.3f + 0.7f * lifeRatio)));
                float sz = _size[i];

                ds.FillCircle(_px[i], _py[i], sz, Color.FromArgb(a, color.R, color.G, color.B));

                if (lifeRatio > 0.65f)
                {
                    byte ca = (byte)Math.Min(255, (int)(a * 0.85f));
                    ds.FillCircle(_px[i], _py[i], sz * 0.4f, Color.FromArgb(ca, 255, 255, 220));
                }
            }
        }

        private void DrawEmbers(CanvasDrawingSession ds)
        {
            int emberCount = 20 + (int)(_smoothBass * 15);
            for (int i = 0; i < emberCount; i++)
            {
                float seed = i * 133.7f;
                float x = (_width * 0.1f) + ((seed + _time * 15f) % (_width * 0.8f));
                float yBase = _height * 0.85f;
                float yProgress = ((_time * 40f + seed * 3f) % (_height * 0.75f));
                float y = yBase - yProgress;

                float xOscillation = (float)Math.Sin(_time * 2.5f + seed) * 20f;

                float flicker = 0.4f + 0.6f * (float)Math.Sin(_time * 10f + seed);
                byte a = (byte)Math.Min(255, (int)(180 * flicker * (1f - (yProgress / (_height * 0.75f)))));

                ds.FillCircle(x + xOscillation, y, 1.2f + (i % 2) * 0.8f, Color.FromArgb(a, 255, 160, 40));
            }
        }

        private static Color HeatmapColor(float t)
        {
            t = Math.Max(0, Math.Min(1, t));

            if (t > 0.75f)
            {
                float f = (t - 0.75f) / 0.25f;
                return Color.FromArgb(255, 255, (byte)(160 + f * 95), (byte)(40 + f * 160));
            }
            if (t > 0.45f)
            {
                float f = (t - 0.45f) / 0.30f;
                return Color.FromArgb(255, 255, (byte)(40 + f * 120), (byte)(10 + f * 30));
            }
            if (t > 0.20f)
            {
                float f = (t - 0.20f) / 0.25f;
                return Color.FromArgb(255, (byte)(160 + f * 95), 0, (byte)(90 - f * 80));
            }

            float fEnd = t / 0.20f;
            return Color.FromArgb(255, (byte)(60 * fEnd), 0, (byte)(100 * fEnd));
        }

        public void ConfigurePipeline(PostProcessPipeline pipeline)
        {
            pipeline.Rotation = 0f;
            pipeline.SlideX = 0f;
            pipeline.SlideY = -0.002f;
            pipeline.FeedbackOpacity = 0.50f;
            pipeline.FeedbackZoom = 1.003f;
            pipeline.FeedbackDecay = 0.03f;
            pipeline.BloomAmount = 0.06f;
            pipeline.BloomBlur = 3f;
            pipeline.BloomThreshold = 0.5f;
        }
    }
}
