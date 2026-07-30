using System;
using Microsoft.Graphics.Canvas;
using Windows.UI;

namespace XFiles.Visualizers.Visualizers
{
    public sealed class WaveformTunnelVisualizer : IAudioVisualizer
    {
        public string Name => "Infinite Waveform Tunnel";
        public string Id => "waveform-tunnel";

        private CanvasDevice _device;
        private float _width, _height, _time;

        // --- Configurações do Túnel ---
        private const int RingCount = 32;          // Quantidade de anéis em profundidade
        private const int PointsPerRing = 90;       // Resolução de cada anel de waveform
        private const float TunnelMaxZ = 1200f;    // Fundo do túnel
        private float _tunnelSpeed = 450f;         // Velocidade padrão de voo

        // --- Sistema de Partículas em Vórtice ---
        private struct TunnelParticle
        {
            public float X, Y, Z;
            public float Angle;
            public float Radius;
            public float Speed;
            public float BaseHue;
            public float Size;
        }

        private const int ParticleCount = 220;
        private readonly TunnelParticle[] _particles = new TunnelParticle[ParticleCount];
        private readonly Random _rand = new Random();

        // --- Áudio e Suavização ---
        private readonly float[] _smoothBands = new float[AudioData.BandCount];
        private float _smoothBass, _smoothBeat, _smoothTreble;
        private const float AudioSmooth = 0.18f;

        public void Initialize(CanvasDevice device)
        {
            _device = device;
            InitParticles();
        }

        private void InitParticles()
        {
            for (int i = 0; i < ParticleCount; i++)
            {
                float angle = (float)(_rand.NextDouble() * Math.PI * 2);
                float radius = 80f + (float)_rand.NextDouble() * 320f;

                _particles[i] = new TunnelParticle
                {
                    Angle = angle,
                    Radius = radius,
                    X = MathF.Cos(angle) * radius,
                    Y = MathF.Sin(angle) * radius,
                    Z = (float)_rand.NextDouble() * TunnelMaxZ,
                    Speed = 300f + (float)_rand.NextDouble() * 500f,
                    BaseHue = (float)_rand.NextDouble(),
                    Size = 2.0f + (float)_rand.NextDouble() * 3.5f
                };
            }
        }

        public void Update(AudioData data, TimeSpan elapsed)
        {
            if (data.BandLevels == null || data.BandLevels.Length == 0) return;

            _time = data.Time;
            float dt = (float)elapsed.TotalSeconds;

            // Extração e tratamento de frequências
            float bass = 0, treble = 0;
            int bassBands = Math.Min(4, data.BandLevels.Length);
            for (int i = 0; i < bassBands; i++) bass += data.BandLevels[i];
            bass /= bassBands;

            int trebleStart = Math.Max(0, data.BandLevels.Length - 6);
            for (int i = trebleStart; i < data.BandLevels.Length; i++) treble += data.BandLevels[i];
            treble /= Math.Max(1, data.BandLevels.Length - trebleStart);

            _smoothBass += (bass - _smoothBass) * AudioSmooth;
            _smoothTreble += (treble - _smoothTreble) * AudioSmooth;
            _smoothBeat += (data.Beat - _smoothBeat) * 0.35f;

            for (int i = 0; i < Math.Min(AudioData.BandCount, data.BandLevels.Length); i++)
            {
                _smoothBands[i] += (data.BandLevels[i] - _smoothBands[i]) * AudioSmooth;
            }

            // A velocidade do túnel dispara na batida
            float currentSpeed = _tunnelSpeed + _smoothBass * 650f + _smoothBeat * 400f;

            // Atualiza Partículas
            for (int i = 0; i < ParticleCount; i++)
            {
                _particles[i].Z -= (_particles[i].Speed + currentSpeed * 0.5f) * dt;

                // Rotação em espiral das partículas no túnel
                _particles[i].Angle += (0.4f + _smoothBass * 0.8f) * dt;
                _particles[i].X = MathF.Cos(_particles[i].Angle) * _particles[i].Radius;
                _particles[i].Y = MathF.Sin(_particles[i].Angle) * _particles[i].Radius;

                // Reset das partículas quando ultrapassam a tela
                if (_particles[i].Z <= 10f)
                {
                    _particles[i].Z += TunnelMaxZ;
                    _particles[i].Angle = (float)(_rand.NextDouble() * Math.PI * 2);
                    _particles[i].Radius = 70f + (float)_rand.NextDouble() * 340f;
                }
            }
        }

        public void Draw(CanvasDrawingSession ds)
        {
            if (_device == null || _width == 0 || _height == 0) return;

            float cx = _width * 0.5f;
            float cy = _height * 0.5f;

            // Fundo abissal do túnel
            ds.Clear(Color.FromArgb(255, 3, 1, 12));

            float fov = 420f;
            float tunnelBaseRadius = Math.Min(_width, _height) * 0.28f;

            // 1. DESENHO DAS PARTÍCULAS EM VÓRTICE (DE TRÁS PARA FRENTE)
            DrawTunnelParticles(ds, cx, cy, fov);

            // 2. DESENHO DOS ANÉIS DE WAVEFORM DO TÚNEL
            for (int r = RingCount - 1; r >= 0; r--)
            {
                // Cálculo de profundidade continuada para efeito Infinito
                float zOffset = (_time * (_tunnelSpeed + _smoothBass * 300f)) % (TunnelMaxZ / RingCount);
                float z = (r * (TunnelMaxZ / RingCount)) - zOffset;

                if (z <= 15f) continue;

                float invZ = 1.0f / z;
                float fog = Math.Clamp(1.0f - (z / TunnelMaxZ), 0.0f, 1.0f);
                float ringRadius = tunnelBaseRadius * (1.0f + _smoothBass * 0.25f);

                // Rotaciona os anéis levemente para dar torção ao túnel
                float ringRotation = _time * 0.3f + r * 0.08f;

                // Tonalidade dinâmica viajando pelo túnel
                float hue = (_time * 0.06f + (r / (float)RingCount) * 0.5f + _smoothBeat * 0.15f) % 1.0f;
                Color ringColor = HslToRgb(hue, 0.95f, (0.35f + _smoothBeat * 0.25f) * fog);
                Color glowColor = Color.FromArgb((byte)(160 * fog), ringColor.R, ringColor.G, ringColor.B);

                float lineThickness = Math.Max(1.0f, (3.5f * fov * invZ) * fog);

                // Desenha a forma de onda do anel fechado
                for (int i = 0; i < PointsPerRing; i++)
                {
                    float angle1 = ((float)i / PointsPerRing) * MathF.PI * 2f + ringRotation;
                    float angle2 = ((float)(i + 1) / PointsPerRing) * MathF.PI * 2f + ringRotation;

                    int bandIdx = (int)((float)i / PointsPerRing * AudioData.BandCount) % AudioData.BandCount;
                    int nextBandIdx = (int)((float)(i + 1) / PointsPerRing * AudioData.BandCount) % AudioData.BandCount;

                    // Deformação da waveform no anel
                    float waveDeform1 = MathF.Sin(angle1 * 8f + _time * 4f) * 12f * _smoothTreble;
                    float waveDeform2 = MathF.Sin(angle2 * 8f + _time * 4f) * 12f * _smoothTreble;

                    float audioAmp1 = _smoothBands[bandIdx] * 70f;
                    float audioAmp2 = _smoothBands[nextBandIdx] * 70f;

                    float r1 = ringRadius + waveDeform1 + audioAmp1;
                    float r2 = ringRadius + waveDeform2 + audioAmp2;

                    // Posições 3D -> 2D
                    float x1 = cx + (MathF.Cos(angle1) * r1 * fov * invZ);
                    float y1 = cy + (MathF.Sin(angle1) * r1 * fov * invZ);

                    float x2 = cx + (MathF.Cos(angle2) * r2 * fov * invZ);
                    float y2 = cy + (MathF.Sin(angle2) * r2 * fov * invZ);

                    ds.DrawLine(x1, y1, x2, y2, ringColor, lineThickness);
                }

                // Linhas conectoras longitudinais (Grade de profundidade do túnel)
                if (r < RingCount - 1)
                {
                    float nextZ = z + (TunnelMaxZ / RingCount);
                    float nextInvZ = 1.0f / nextZ;

                    for (int step = 0; step < PointsPerRing; step += 10)
                    {
                        float angle = ((float)step / PointsPerRing) * MathF.PI * 2f + ringRotation;
                        int bandIdx = (int)((float)step / PointsPerRing * AudioData.BandCount) % AudioData.BandCount;

                        float rDist = ringRadius + _smoothBands[bandIdx] * 70f;

                        float px1 = cx + (MathF.Cos(angle) * rDist * fov * invZ);
                        float py1 = cy + (MathF.Sin(angle) * rDist * fov * invZ);

                        float px2 = cx + (MathF.Cos(angle) * rDist * fov * nextInvZ);
                        float py2 = cy + (MathF.Sin(angle) * rDist * fov * nextInvZ);

                        ds.DrawLine(px1, py1, px2, py2, glowColor, Math.Max(0.8f, lineThickness * 0.5f));
                    }
                }
            }
        }

        private void DrawTunnelParticles(CanvasDrawingSession ds, float cx, float cy, float fov)
        {
            for (int i = 0; i < ParticleCount; i++)
            {
                var p = _particles[i];
                if (p.Z <= 10f) continue;

                float invZ = 1.0f / p.Z;
                float fog = Math.Clamp(1.0f - (p.Z / TunnelMaxZ), 0.0f, 1.0f);

                float sx = cx + (p.X * fov * invZ);
                float sy = cy + (p.Y * fov * invZ);

                float pSize = Math.Max(1.0f, (p.Size * fov * invZ) * (1.0f + _smoothBass));

                if (sx > 0 && sx < _width && sy > 0 && sy < _height)
                {
                    float hue = (p.BaseHue + _time * 0.1f + _smoothBeat * 0.2f) % 1.0f;
                    Color pColor = HslToRgb(hue, 1.0f, 0.6f * fog);

                    ds.FillCircle(sx, sy, pSize, pColor);
                }
            }
        }

        public void Resize(float width, float height) { _width = width; _height = height; }
        public void Dispose() { _device = null; }

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
            // Glow e brilho para realçar as partículas e waveforms neon
            pipeline.BloomAmount = 0.65f;
            pipeline.BloomBlur = 3.5f;
            pipeline.BloomThreshold = 0.12f;
        }
    }
}