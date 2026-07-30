using System;
using Microsoft.Graphics.Canvas;
using Windows.UI;

namespace XFiles.Visualizers.Visualizers
{
    public sealed class ThreeDOVisualizer : IAudioVisualizer
    {
        public string Name => "3DO Interactive Music Player";
        public string Id => "threedo-visualizer";

        private CanvasDevice _device;
        private float _width, _height, _time;

        // Estrutura para os cubos e formas flutuantes em 3D
        private struct Cube3D
        {
            public float X, Y, Z;
            public float Size;
            public int BandIndex;
            public float BaseHue;
        }

        private const int CubeCount = 64;
        private readonly Cube3D[] _cubes = new Cube3D[CubeCount];
        private readonly Random _rand = new Random();

        private readonly float[] _smoothBands = new float[AudioData.BandCount];
        private float _smoothBass, _smoothBeat;
        private const float AudioSmooth = 0.20f;

        public void Initialize(CanvasDevice device)
        {
            _device = device;
            InitCubes();
        }

        private void InitCubes()
        {
            // Organiza os cubos formando um túnel/campo 3D ao redor do centro
            for (int i = 0; i < CubeCount; i++)
            {
                float angle = (float)(_rand.NextDouble() * Math.PI * 2);
                float radius = 120f + (float)_rand.NextDouble() * 250f;

                _cubes[i] = new Cube3D
                {
                    X = MathF.Cos(angle) * radius,
                    Y = MathF.Sin(angle) * radius,
                    Z = (float)_rand.NextDouble() * 1000f,
                    Size = 14f + (float)_rand.NextDouble() * 10f,
                    BandIndex = i % AudioData.BandCount,
                    BaseHue = (float)i / CubeCount
                };
            }
        }

        public void Update(AudioData data, TimeSpan elapsed)
        {
            if (data.BandLevels == null || data.BandLevels.Length == 0) return;

            _time = data.Time;

            float bass = 0;
            int bassBands = Math.Min(4, data.BandLevels.Length);
            for (int i = 0; i < bassBands; i++) bass += data.BandLevels[i];
            bass /= bassBands;

            _smoothBass += (bass - _smoothBass) * AudioSmooth;
            _smoothBeat += (data.Beat - _smoothBeat) * 0.35f;

            for (int i = 0; i < Math.Min(AudioData.BandCount, data.BandLevels.Length); i++)
            {
                _smoothBands[i] += (data.BandLevels[i] - _smoothBands[i]) * AudioSmooth;
            }

            // Velocidade de voo pelo túnel impulsionada pelo grave da música
            float speed = 250f + _smoothBass * 400f;
            float dt = (float)elapsed.TotalSeconds;

            for (int i = 0; i < CubeCount; i++)
            {
                _cubes[i].Z -= speed * dt;

                // Quando o cubo passa da câmera, ele renasce no fundo do túnel
                if (_cubes[i].Z <= 10f)
                {
                    _cubes[i].Z += 1000f;
                    float angle = (float)(_rand.NextDouble() * Math.PI * 2);
                    float radius = 100f + (float)_rand.NextDouble() * 260f;
                    _cubes[i].X = MathF.Cos(angle) * radius;
                    _cubes[i].Y = MathF.Sin(angle) * radius;
                }
            }
        }

        public void Draw(CanvasDrawingSession ds)
        {
            if (_device == null || _width == 0 || _height == 0) return;

            float cx = _width * 0.5f;
            float cy = _height * 0.5f;

            // Fundo Roxo/Azul escuro característico dos menus do 3DO
            ds.Clear(Color.FromArgb(255, 10, 4, 28));

            // 1. DESENHA O VÓRTICE/GRADE DE FUNDO (Gira continuamente)
            DrawBackgroundGrid(ds, cx, cy);

            // 2. ROTAÇÃO GLOBAL E PROJEÇÃO PERSPECTIVA (Câmera 3D Estilo 32-bit)
            float rotAngle = _time * 0.4f + _smoothBass * 0.2f;
            float cosR = MathF.Cos(rotAngle);
            float sinR = MathF.Sin(rotAngle);

            float fov = 380f; // Distância focal para perspectiva acentuada dos anos 90

            for (int i = 0; i < CubeCount; i++)
            {
                var cube = _cubes[i];

                // Rotação em Z
                float rx = cube.X * cosR - cube.Y * sinR;
                float ry = cube.X * sinR + cube.Y * cosR;

                // Projeção 3D -> 2D
                float invZ = 1.0f / cube.Z;
                float screenX = cx + (rx * fov * invZ);
                float screenY = cy + (ry * fov * invZ);

                // O tamanho do cubo reage à frequência de áudio atribuída a ele
                float audioScale = 1.0f + _smoothBands[cube.BandIndex] * 1.8f;
                float projectedSize = (cube.Size * audioScale * fov * invZ);

                // Efeito Fog (objetos distantes ficam mais escuros)
                float fog = Math.Clamp(1.0f - (cube.Z / 1000f), 0.05f, 1.0f);

                if (screenX > -projectedSize && screenX < _width + projectedSize &&
                    screenY > -projectedSize && screenY < _height + projectedSize)
                {
                    // Tonalidade vibrante em 16-bit reativa ao Beat
                    float hue = (cube.BaseHue + _time * 0.05f + _smoothBeat * 0.1f) % 1.0f;
                    Color fillColor = HslToRgb(hue, 0.9f, 0.45f * fog);
                    Color wireColor = HslToRgb((hue + 0.2f) % 1.0f, 1.0f, 0.85f * fog);

                    // Desenha o bloco estilo 32-bit
                    float drawX = screenX - projectedSize * 0.5f;
                    float drawY = screenY - projectedSize * 0.5f;

                    // Corpo preenchido
                    ds.FillRectangle(drawX, drawY, projectedSize, projectedSize, fillColor);

                    // Borda Neon/Wireframe em destaque (muito comum nos gráficos do 3DO)
                    ds.DrawRectangle(drawX, drawY, projectedSize, projectedSize, wireColor, 1.5f);
                }
            }
        }

        private void DrawBackgroundGrid(CanvasDrawingSession ds, float cx, float cy)
        {
            // Anéis concêntricos que simulam um túnel 3D ao fundo
            int ringCount = 8;
            float maxRadius = Math.Max(_width, _height) * 0.65f;

            for (int r = 1; r <= ringCount; r++)
            {
                float baseR = (r / (float)ringCount) * maxRadius;
                float pulseR = baseR + MathF.Sin(_time * 3f + r) * 12f * _smoothBass;

                float hue = (0.70f + (r / (float)ringCount) * 0.25f) % 1.0f;
                Color ringColor = Color.FromArgb((byte)(80 * (1f - r / (float)ringCount)), 
                                                 HslToRgb(hue, 0.8f, 0.5f).R, 
                                                 HslToRgb(hue, 0.8f, 0.5f).G, 
                                                 HslToRgb(hue, 0.8f, 0.5f).B);

                ds.DrawCircle(cx, cy, pulseR, ringColor, 1.5f);
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
            // Glow sutil para dar o toque de tela CRT/TV de tubo da época
            pipeline.BloomAmount = 0.40f;
            pipeline.BloomBlur = 2.5f;
            pipeline.BloomThreshold = 0.20f;
        }
    }
}
