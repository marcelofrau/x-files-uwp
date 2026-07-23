using System;
using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Windows.UI;

namespace XFiles.Visualizers.Visualizers
{
    public struct FireParticle
    {
        public Vector2 Position;
        public Vector2 Velocity;
        public float Life;
        public float MaxLife;
        public float Size;
        public float Hue;
    }

    public class FireParticleSystem2
    {
        private readonly FireParticle[] _particles = new FireParticle[800];
        private int _count;
        private readonly Random _rand = new Random();

        public int Count => _count;

        public void Emit(Vector2 spawnPos, float hue, float intensity)
        {
            if (_count >= _particles.Length) return;
            int emitCount = (int)(2 + intensity * 6);
            for (int i = 0; i < emitCount && _count < _particles.Length; i++)
            {
                float angle = (float)(_rand.NextDouble() * Math.PI * 2.0);
                float speed = (float)(30 + _rand.NextDouble() * 120 * intensity);
                _particles[_count++] = new FireParticle
                {
                    Position = spawnPos,
                    Velocity = new Vector2(
                        (float)(_rand.NextDouble() - 0.5) * 40f,
                        -1f * (float)(40 + _rand.NextDouble() * 100 * (1.0 + intensity))
                    ),
                    Life = 1.0f,
                    MaxLife = (float)(0.4 + _rand.NextDouble() * 0.6),
                    Size = (float)(2.5 + _rand.NextDouble() * 4.5),
                    Hue = hue
                };
            }
        }

        public void Update(float deltaTime, float time)
        {
            for (int i = _count - 1; i >= 0; i--)
            {
                var p = _particles[i];
                p.Life -= deltaTime / p.MaxLife;
                if (p.Life <= 0)
                {
                    _particles[i] = _particles[--_count];
                    continue;
                }
                float turbulence = (float)Math.Sin(time * 8.0f + p.Position.Y * 0.05f) * 15f;
                p.Velocity.X += turbulence * deltaTime;
                p.Velocity.Y -= 25f * deltaTime;
                p.Position += p.Velocity * deltaTime;
                _particles[i] = p;
            }
        }

        public void Draw(CanvasDrawingSession ds)
        {
            for (int i = 0; i < _count; i++)
            {
                var p = _particles[i];
                float nl = Math.Clamp(p.Life, 0f, 1f);
                float sz = p.Size * (0.3f + 0.7f * nl);
                Color c;
                if (nl > 0.8f)
                {
                    c = Color.FromArgb((byte)(255 * nl), 255, 255, 200);
                }
                else
                {
                    byte a = (byte)(230 * (nl / 0.8f));
                    byte r = (byte)(Math.Sin(p.Hue * 6.28f) * 127 + 128);
                    byte g = (byte)(Math.Sin((p.Hue + 0.33f) * 6.28f) * 127 + 128);
                    byte b = (byte)(Math.Sin((p.Hue + 0.66f) * 6.28f) * 127 + 128);
                    c = Color.FromArgb(a, r, g, b);
                }
                ds.FillCircle(p.Position, sz, c);
            }
        }
    }

    public sealed class InfernoCoreVisualizer2 : IAudioVisualizer
    {
        public string Name => "Inferno Core 2";
        public string Id => "inferno-core-2";

        private CanvasDevice _device;
        private float _width, _height, _time;

        private readonly FireParticleSystem2 _particles = new FireParticleSystem2();
        private readonly float[] _smoothBands = new float[AudioData.BandCount];
        private float _smoothBass, _smoothBeat;
        private const float AudioSmooth = 0.25f;
        private float _emitTimer;
        private float[] _waveform;
        private int _waveformCount;
        private const int GhostFrames = 10;
        private readonly float[][] _ghostWaveforms = new float[GhostFrames][];
        private readonly int[] _ghostCounts = new int[GhostFrames];
        private readonly float[] _ghostYOffsets = new float[GhostFrames];
        private int _ghostIndex;

        public void Initialize(CanvasDevice device)
        {
            _device = device;
            for (int i = 0; i < GhostFrames; i++)
            {
                _ghostWaveforms[i] = new float[2048];
                _ghostCounts[i] = 0;
            }
        }

        public void Update(AudioData data, TimeSpan elapsed)
        {
            float dt = (float)elapsed.TotalSeconds;
            _time = data.Time;

            float bass = 0;
            for (int i = 0; i < 6; i++) bass += data.BandLevels[i]; bass /= 6f;
            _smoothBass += (bass - _smoothBass) * AudioSmooth;
            _smoothBeat += (data.Beat - _smoothBeat) * 0.4f;

            for (int i = 0; i < AudioData.BandCount; i++)
                _smoothBands[i] += (data.BandLevels[i] - _smoothBands[i]) * AudioSmooth;

            // Emit from 6 spawn points along the bottom
            _emitTimer += dt;
            float emitInterval = Math.Max(0.01f, 0.033f - _smoothBeat * 0.015f);
            if (_emitTimer > emitInterval)
            {
                _emitTimer = 0;
                for (int col = 0; col < 6; col++)
                {
                    float spawnX = _width * (0.15f + col * 0.14f);
                    float spawnY = _height * 0.88f;
                    float hue = (float)col / 6f;
                    float bandIntensity = _smoothBands[col * 4 % AudioData.BandCount];
                    float intensity = bandIntensity + _smoothBeat * 0.3f + _smoothBass * 0.2f;
                    _particles.Emit(new Vector2(spawnX, spawnY), hue, Math.Min(1f, intensity));
                }
            }

            _particles.Update(dt, _time);
            _waveform = data.Waveform;
            _waveformCount = data.WaveformCount;
            if (_waveform != null && _waveformCount > 0)
            {
                int count = Math.Min(_waveformCount, 2048);
                _ghostCounts[_ghostIndex] = count;
                _ghostYOffsets[_ghostIndex] = 0f;
                Array.Copy(_waveform, _ghostWaveforms[_ghostIndex], count);
                _ghostIndex = (_ghostIndex + 1) % GhostFrames;

                for (int g = 0; g < GhostFrames; g++)
                {
                    _ghostYOffsets[g] += dt * 25f * (1f + _smoothBass * 0.5f);
                    if (_ghostYOffsets[g] > _height * 0.25f)
                        _ghostYOffsets[g] = 0f;
                }
            }
        }

        public void Draw(CanvasDrawingSession ds)
        {
            if (_device == null || _width == 0 || _height == 0) return;
            ds.Clear(Color.FromArgb(255, 4, 1, 1));

            // Dark ground gradient
            float groundY = _height * 0.85f;
            for (float y = groundY; y < _height; y += 3f)
            {
                float t = (y - groundY) / (_height - groundY);
                byte g = (byte)(12 * (1f - t));
                ds.FillRectangle(0, y, _width, 3f, Color.FromArgb(255, (byte)(8 + g), (byte)(3 + g / 2), (byte)(2 + g / 3)));
            }

            // Ember glow at bottom
            float beatPulse = _smoothBeat;
            float glowR = _width * 0.4f * (1f + beatPulse * 0.3f);
            float glowH = _height * 0.12f * (1f + beatPulse * 0.4f);
            byte glowA = (byte)Math.Min(255, (int)(15 + beatPulse * 35));
            var glowGeo = CanvasGeometry.CreateEllipse(ds, _width * 0.5f, _height * 0.9f, glowR, glowH);
            ds.FillGeometry(glowGeo, Color.FromArgb(glowA, 180, 40, 10));

            _particles.Draw(ds);

            // Hot core overlay — bright center on strong beats
            if (beatPulse > 0.4f)
            {
                float coreR = _width * 0.05f * beatPulse;
                byte coreA = (byte)Math.Min(180, (int)(beatPulse * 100));
                var coreGeo = CanvasGeometry.CreateEllipse(ds, _width * 0.5f, _height * 0.82f, coreR, coreR * 0.6f);
                ds.FillGeometry(coreGeo, Color.FromArgb(coreA, 255, 200, 100));
            }

            // Waveform line at bottom with ghost trail — ascending loop effect
            float waveY = _height * 0.72f;
            float waveH = _height * 0.10f;
            var strokeStyle = new CanvasStrokeStyle { StartCap = CanvasCapStyle.Round, EndCap = CanvasCapStyle.Round };

            for (int g = 0; g < GhostFrames; g++)
            {
                int gi = (_ghostIndex + g) % GhostFrames;
                float[] wave = _ghostWaveforms[gi];
                int count = _ghostCounts[gi];
                if (wave == null || count <= 1) continue;

                float age = (float)(GhostFrames - g) / GhostFrames;
                float yOffset = _ghostYOffsets[gi];
                float waveYOffset = waveY - yOffset;
                byte waveAlpha = (byte)Math.Clamp((int)(120 * (1f - age * 0.7f)), 0, 255);
                if (waveAlpha < 3) continue;

                float waveHue = (0.07f + age * 0.03f) % 1.0f;
                Color waveColor = HslToRgb(waveHue, 0.9f, 0.5f + age * 0.2f);

                float prevX = 0f;
                float prevY = waveYOffset + wave[0] * waveH;
                int step = Math.Max(1, count / 200);
                for (int i = step; i < count; i += step)
                {
                    float x = (float)i / count * _width;
                    float y = waveYOffset + wave[i] * waveH;
                    ds.DrawLine(prevX, prevY, x, y, Color.FromArgb(waveAlpha, waveColor.R, waveColor.G, waveColor.B), 3f + _smoothBass * 1.5f, strokeStyle);
                    prevX = x; prevY = y;
                }
            }
        }

        public void Resize(float width, float height) { _width = width; _height = height; }
        public void Dispose() { _device = null; }

        public void ConfigurePipeline(PostProcessPipeline pipeline)
        {
            pipeline.FeedbackOpacity = 0.20f;
            pipeline.FeedbackZoom = 1.0003f;
            pipeline.BloomAmount = 0.04f;
            pipeline.BloomBlur = 2f;
            pipeline.BloomThreshold = 0.5f;
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
