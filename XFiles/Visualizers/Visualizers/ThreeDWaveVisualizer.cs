using System;
using Microsoft.Graphics.Canvas;
using Windows.UI;

namespace XFiles.Visualizers.Visualizers
{
    public sealed class ThreeDWaveVisualizer : IAudioVisualizer
    {
        public string Name => "3D Wave";
        public string Id => "threed-wave";

        private CanvasDevice _device;
        private float _width, _height, _time;

        // Resolução da grade 3D (colunas x profundidade)
        private const int GridCols = 36;
        private const int GridRows = 32;
        private readonly float[,] _heights = new float[GridCols, GridRows];

        private readonly float[] _smoothBands = new float[AudioData.BandCount];
        private float _smoothBass;
        private const float AudioSmooth = 0.20f;

        public void Initialize(CanvasDevice device)
        {
            _device = device;
        }

        public void Update(AudioData data, TimeSpan elapsed)
        {
            if (data.BandLevels == null || data.BandLevels.Length == 0) return;

            _time = data.Time;

            // Tratamento das frequências
            float bass = 0;
            int bassBands = Math.Min(4, data.BandLevels.Length);
            for (int i = 0; i < bassBands; i++) bass += data.BandLevels[i];
            bass /= bassBands;

            _smoothBass += (bass - _smoothBass) * AudioSmooth;

            for (int i = 0; i < Math.Min(AudioData.BandCount, data.BandLevels.Length); i++)
            {
                _smoothBands[i] += (data.BandLevels[i] - _smoothBands[i]) * AudioSmooth;
            }

            // 1. PROPAGAÇÃO DAS ONDAS E DEFORMAÇÃO DO CHÃO
            for (int r = 0; r < GridRows; r++)
            {
                // Associa cada linha de profundidade a uma banda do áudio
                float bandPower = _smoothBands[r % AudioData.BandCount];

                for (int c = 0; c < GridCols; c++)
                {
                    // Distância do ponto ao centro da grade
                    float dx = c - GridCols / 2f;
                    float dz = r - GridRows / 2f;
                    float dist = MathF.Sqrt(dx * dx + dz * dz);

                    // Função senoidal combinada com a intensidade da música
                    float wave = MathF.Sin(dist * 0.35f - _time * 4.5f) * 22f;
                    float audioDeform = bandPower * 55f * MathF.Cos((c - GridCols / 2f) * 0.3f);

                    _heights[c, r] = wave + audioDeform;
                }
            }
        }

        public void Draw(CanvasDrawingSession ds)
        {
            if (_device == null || _width == 0 || _height == 0) return;

            float cx = _width * 0.5f;
            float cy = _height * 0.45f; // Linha do horizonte (ponto de fuga)

            // Fundo escuro azul/púrpura clássico dos anos 90
            ds.Clear(Color.FromArgb(255, 6, 2, 18));

            float fov = 400f;
            float cameraHeight = 140f + MathF.Sin(_time * 0.8f) * 15f; // Câmera balança levemente
            float spacingX = 38f;
            float spacingZ = 35f;

            // Guardaremos as coordenadas 2D projetadas na tela
            var proj = new (float x, float y, float z)[GridCols, GridRows];

            // 2. MATRIZ DE PROJEÇÃO PERSPECTIVA (Chão Mode 7)
            for (int r = 0; r < GridRows; r++)
            {
                // Z cresce em direção ao horizonte
                float z = 120f + r * spacingZ;
                float invZ = 1.0f / z;

                for (int c = 0; c < GridCols; c++)
                {
                    float worldX = (c - GridCols / 2f) * spacingX;

                    // Altura do chão = Altura da Câmera - Deformação da onda
                    float worldY = cameraHeight - _heights[c, r];

                    // Projeção em perspectiva 3D -> 2D
                    float screenX = cx + (worldX * fov * invZ);
                    float screenY = cy + (worldY * fov * invZ);

                    proj[c, r] = (screenX, screenY, z);
                }
            }

            // 3. DESENHO DA MALHA/GRADE RETRO
            for (int r = GridRows - 1; r >= 0; r--)
            {
                // Névoa / Fade de distância no horizonte
                float fog = Math.Clamp(1.0f - (r / (float)GridRows), 0.05f, 1.0f);
                float lineThickness = 1.6f * fog;

                // Cor gradiente da grade (Ciano/Rosa neon com fade de profundidade)
                byte red = (byte)(Math.Clamp((1.0f - fog) * 255 + _smoothBass * 50, 0, 255));
                byte green = (byte)(Math.Clamp(fog * 220, 0, 255));
                byte blue = (byte)(Math.Clamp(fog * 255, 0, 255));
                Color gridColor = Color.FromArgb((byte)(255 * fog), red, green, blue);

                // Linhas Horizontais (Largura da grade)
                for (int c = 0; c < GridCols - 1; c++)
                {
                    var p1 = proj[c, r];
                    var p2 = proj[c + 1, r];

                    ds.DrawLine(p1.x, p1.y, p2.x, p2.y, gridColor, lineThickness);
                }

                // Linhas Longitudinais (Profundidade em direção ao horizonte)
                if (r < GridRows - 1)
                {
                    for (int c = 0; c < GridCols; c++)
                    {
                        var p1 = proj[c, r];
                        var p2 = proj[c, r + 1];

                        ds.DrawLine(p1.x, p1.y, p2.x, p2.y, gridColor, lineThickness * 0.8f);
                    }
                }
            }

            // 4. VU METER BAR NO HORIZONTE
            DrawHorizonVuMeter(ds, cx, cy);
        }

        private void DrawHorizonVuMeter(CanvasDrawingSession ds, float cx, float cy)
        {
            int vuCount = 24;
            float vuWidth = _width * 0.55f;
            float startX = cx - vuWidth * 0.5f;
            float barW = vuWidth / vuCount;
            float barH = 4f;

            for (int i = 0; i < vuCount; i++)
            {
                int bandIdx = (i * AudioData.BandCount) / vuCount;
                bandIdx = Math.Min(bandIdx, AudioData.BandCount - 1);
                float level = _smoothBands[bandIdx];
                float height = Math.Max(1f, level * 35f);

                float x = startX + i * barW;
                float hue = (0.55f + (float)i / vuCount * 0.25f + _time * 0.02f) % 1f;
                var col = HslToRgb(hue, 0.8f, 0.4f + level * 0.4f);

                ds.FillRectangle(x, cy - height, barW - 1, height,
                    Color.FromArgb(180, col.R, col.G, col.B));

                if (level > 0.1f)
                {
                    ds.DrawRectangle(x, cy - height, barW - 1, height,
                        Color.FromArgb((byte)(level * 180), 255, 255, 255), 0.5f);
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
            // Bloom suave para criar o brilho dos monitores CRT da época
            pipeline.BloomAmount = 0.55f;
            pipeline.BloomBlur = 3.2f;
            pipeline.BloomThreshold = 0.15f;
        }
    }
}
