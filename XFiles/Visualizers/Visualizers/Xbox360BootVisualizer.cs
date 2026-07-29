using System;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Windows.UI;

namespace XFiles.Visualizers.Visualizers
{
    public sealed class Xbox360BootVisualizer : IAudioVisualizer
    {
        public string Name => "Xbox 360 Boot";
        public string Id => "xbox360-boot";

        private CanvasDevice _device;
        private float _width, _height, _time;
        private float _cx, _cy, _radius;

        private readonly float[] _smoothBands = new float[AudioData.BandCount];
        private float _smoothBass, _smoothBeat, _smoothAvg;
        private float[] _waveform;
        private int _waveformCount;
        private const float AudioSmooth = 0.18f;

        private const int RayCount = 36;
        private const int WaveformPoints = 120;
        private const int RingCount = 4;

        private struct RingPulse { public float Phase, Speed, MaxRadius, Width; }
        private readonly RingPulse[] _rings = new RingPulse[RingCount];

        public void Initialize(CanvasDevice device)
        {
            _device = device;
            for (int i = 0; i < RingCount; i++)
            {
                _rings[i] = new RingPulse
                {
                    Phase = (float)i / RingCount,
                    Speed = 0.3f + i * 0.08f,
                    MaxRadius = 0.4f + i * 0.2f,
                    Width = 1.5f + i * 1f
                };
            }
        }

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

            _waveform = data.Waveform;
            _waveformCount = data.WaveformCount;

            for (int i = 0; i < RingCount; i++)
            {
                _rings[i].Phase += _rings[i].Speed * dt * (1f + _smoothBeat * 0.5f);
                if (_rings[i].Phase > 1f) _rings[i].Phase -= 1f;
            }
        }

        public void Draw(CanvasDrawingSession ds)
        {
            if (_device == null || _width == 0 || _height == 0) return;

            _cx = _width * 0.5f;
            _cy = _height * 0.5f;
            _radius = Math.Min(_width, _height) * 0.15f;

            DrawBackground(ds);
            DrawGlowRings(ds);
            DrawRays(ds);
            DrawSphereGlow(ds);
            DrawSphere(ds);
            DrawWaveformRing(ds);
        }

        private void DrawBackground(CanvasDrawingSession ds)
        {
            float beatFlash = _smoothBeat > 0.3f ? _smoothBeat * 0.15f : 0f;
            for (int y = 0; y < _height; y += 2)
            {
                float t = y / _height;
                float invT = 1f - t;
                byte r = (byte)Math.Min(255, (int)((5 + invT * 20 + beatFlash * 30) * (0.6f + _smoothAvg * 0.4f)));
                byte g = (byte)Math.Min(255, (int)((20 + invT * 50 + _smoothBass * 20 + beatFlash * 40) * (0.6f + _smoothAvg * 0.4f)));
                byte b = (byte)Math.Min(255, (int)((3 + invT * 15) * (0.6f + _smoothAvg * 0.4f)));
                ds.DrawLine(0, y, _width, y, Color.FromArgb(255, r, g, b));
            }

            float glowR = _radius * 3f * (1f + _smoothBass * 0.4f + _smoothBeat * 0.3f);
            byte glowA = (byte)Math.Min(200, (int)(30 + _smoothBeat * 60 + _smoothBass * 30));
            var glow = CanvasGeometry.CreateCircle(ds, _cx, _cy, glowR);
            ds.FillGeometry(glow, Color.FromArgb(glowA, 30, 180, 30));
        }

        private void DrawGlowRings(CanvasDrawingSession ds)
        {
            for (int i = 0; i < RingCount; i++)
            {
                var r = _rings[i];
                float progress = r.Phase;
                float ringR = _radius * r.MaxRadius * (0.6f + progress * 0.4f) * (1f + _smoothBass * 0.2f);
                float alpha = (1f - progress) * (0.4f + _smoothBeat * 0.3f);
                if (alpha < 0.01f) continue;

                float thickness = r.Width * (1f + _smoothBeat * 1.5f);
                byte a = (byte)(alpha * 180);
                ds.DrawCircle(_cx, _cy, ringR, Color.FromArgb(a, 100, 255, 100), thickness);
            }
        }

        private void DrawRays(CanvasDrawingSession ds)
        {
            float rayLength = _radius * 3.5f * (1f + _smoothBass * 0.3f);
            float rotation = _time * 0.3f;
            float beatFlash = _smoothBeat > 0.3f ? _smoothBeat : 0f;

            for (int i = 0; i < RayCount; i++)
            {
                float angle = (float)i / RayCount * MathF.PI * 2f + rotation;
                float bandIdx = (float)i / RayCount * AudioData.BandCount;
                int b0 = (int)bandIdx;
                int b1 = Math.Min(b0 + 1, AudioData.BandCount - 1);
                float bandT = bandIdx - b0;
                float bandVal = _smoothBands[b0] * (1f - bandT) + _smoothBands[b1] * bandT;

                float varLen = rayLength * (0.4f + bandVal * 1.2f + beatFlash * 0.5f);
                float baseAlpha = 0.15f + bandVal * 0.5f + beatFlash * 0.3f;
                byte a = (byte)(baseAlpha * 200);

                float sin = MathF.Sin(angle);
                float cos = MathF.Cos(angle);
                float thickness = 1f + bandVal * 2f + beatFlash * 1f;

                ds.DrawLine(
                    _cx, _cy,
                    _cx + cos * varLen, _cy + sin * varLen,
                    Color.FromArgb(a, 120, 255, 120), thickness);
            }
        }

        private void DrawSphereGlow(CanvasDrawingSession ds)
        {
            float pulseR = _radius * (1.4f + _smoothBass * 0.5f + _smoothBeat * 0.4f);
            byte alpha = (byte)(40 + _smoothBeat * 60 + _smoothBass * 30);
            var glow = CanvasGeometry.CreateCircle(ds, _cx, _cy, pulseR);
            ds.FillGeometry(glow, Color.FromArgb(alpha, 180, 255, 180));

            float innerR = _radius * 1.1f;
            byte innerA = (byte)(60 + _smoothBeat * 50);
            var innerGlow = CanvasGeometry.CreateCircle(ds, _cx, _cy, innerR);
            ds.FillGeometry(innerGlow, Color.FromArgb(innerA, 220, 255, 220));
        }

        private void DrawSphere(CanvasDrawingSession ds)
        {
            float r = _radius * (1f + _smoothBass * 0.15f + _smoothBeat * 0.10f);
            float pr = r / _radius;
            float cx = _cx, cy = _cy;

            float shadowOff = r * 0.08f;
            var shadow = CanvasGeometry.CreateCircle(ds, cx + shadowOff, cy + shadowOff * 0.5f, r);
            ds.FillGeometry(shadow, Color.FromArgb((byte)(50 * pr), 0, 0, 0));

            var sphere = CanvasGeometry.CreateCircle(ds, cx, cy, r);
            ds.FillGeometry(sphere, Color.FromArgb(255, 190, 210, 200));

            byte hiA = (byte)(180 * pr);
            var highlight = CanvasGeometry.CreateEllipse(ds,
                cx - r * 0.3f, cy - r * 0.35f,
                r * 0.5f, r * 0.3f);
            ds.FillGeometry(highlight, Color.FromArgb(hiA, 255, 255, 255));

            var hiSoft = CanvasGeometry.CreateEllipse(ds,
                cx - r * 0.25f, cy - r * 0.30f,
                r * 0.7f, r * 0.45f);
            ds.FillGeometry(hiSoft, Color.FromArgb((byte)(35 * pr), 255, 255, 255));

            byte edgeA = (byte)(90 * pr);
            ds.DrawCircle(cx, cy, r, Color.FromArgb(edgeA, 160, 190, 180), 1.5f);
        }

        private void DrawWaveformRing(CanvasDrawingSession ds)
        {
            if (_waveform == null || _waveformCount <= 1) return;

            float r = _radius * 2.2f * (1f + _smoothBass * 0.15f);
            float waveScale = r * 0.25f;
            int step = Math.Max(1, _waveformCount / WaveformPoints);
            byte alpha = (byte)(80 + _smoothBeat * 60);
            float rotation = _time * 0.2f;
            float beatThick = 1.5f + _smoothBeat * 2f;

            float prevX = 0f, prevY = 0f;
            bool first = true;

            for (int i = 0; i <= _waveformCount; i += step)
            {
                int idx = Math.Min(i, _waveformCount - 1);
                float t = (float)idx / _waveformCount;
                float angle = t * MathF.PI * 2f + rotation;
                float waveVal = _waveform[idx] * waveScale;
                float x = _cx + (r + waveVal) * MathF.Cos(angle);
                float y = _cy + (r + waveVal) * MathF.Sin(angle);

                if (!first)
                {
                    float bandT = t * AudioData.BandCount;
                    int b0 = (int)bandT;
                    int b1 = Math.Min(b0 + 1, AudioData.BandCount - 1);
                    float bf = bandT - b0;
                    float bandVal = _smoothBands[b0] * (1f - bf) + _smoothBands[b1] * bf;

                    byte ca = (byte)(alpha * (0.5f + bandVal * 0.8f));
                    byte cg = (byte)(150 + (int)(bandVal * 105));
                    ds.DrawLine(prevX, prevY, x, y,
                        Color.FromArgb(ca, 80, cg, 80), beatThick);
                }
                else first = false;
                prevX = x; prevY = y;
            }
        }

        public void Resize(float width, float height) { _width = width; _height = height; }
        public void Dispose() { _device = null; }

        public void ConfigurePipeline(PostProcessPipeline pipeline)
        {
            pipeline.FeedbackOpacity = 0.08f;
            pipeline.FeedbackZoom = 1.0004f;
            pipeline.BloomAmount = 0.15f;
            pipeline.BloomBlur = 3f;
            pipeline.BloomThreshold = 0.35f;
            pipeline.VignetteEnabled = true;
        }
    }
}
