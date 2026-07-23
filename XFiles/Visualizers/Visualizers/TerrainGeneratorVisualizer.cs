using System;
using Microsoft.Graphics.Canvas;
using Windows.UI;

namespace XFiles.Visualizers.Visualizers
{
    public sealed class TerrainGeneratorVisualizer : IAudioVisualizer
    {
        public string Name => "Voxel Terrain Generator";
        public string Id => "terrain-generator";

        private CanvasDevice _device;
        private float _width, _height, _time;

        private const int MapSize = 64;
        private readonly float[,] _heightMap = new float[MapSize, MapSize];

        // Câmera posicionada no alto e apontando para baixo
        private float _cameraX = MapSize / 2f;
        private float _cameraY = 0f;
        private float _cameraZ = 35f;

        private readonly float[] _smoothBands = new float[AudioData.BandCount];
        private float _smoothBass, _smoothBeat;
        private const float AudioSmooth = 0.20f;

        public void Initialize(CanvasDevice device) { _device = device; }

        public void Update(AudioData data, TimeSpan elapsed)
        {
            if (data.BandLevels == null || data.BandLevels.Length == 0) return;

            _time = data.Time;

            float bass = 0;
            if (data.BandLevels.Length >= 6)
            {
                for (int i = 0; i < 6; i++) bass += data.BandLevels[i];
                bass /= 6f;
            }
            _smoothBass += (bass - _smoothBass) * AudioSmooth;
            _smoothBeat += (data.Beat - _smoothBeat) * 0.35f;

            for (int i = 0; i < Math.Min(AudioData.BandCount, data.BandLevels.Length); i++)
            {
                _smoothBands[i] += (data.BandLevels[i] - _smoothBands[i]) * AudioSmooth;
            }

            float speed = 9.0f + _smoothBass * 7.0f;
            _cameraY += speed * (float)elapsed.TotalSeconds;
            if (_cameraY >= MapSize) _cameraY -= MapSize;

            GenerateVoxelHeights();
        }

        private void GenerateVoxelHeights()
        {
            for (int y = 0; y < MapSize; y++)
            {
                for (int x = 0; x < MapSize; x++)
                {
                    int bandIdx = (int)((float)x / MapSize * AudioData.BandCount) % AudioData.BandCount;

                    float wave1 = MathF.Sin(_time * 2.5f + x * 0.25f + y * 0.15f) * 4f;
                    float wave2 = MathF.Cos(_time * 3.5f + y * 0.25f) * 3f * _smoothBass;

                    // Elevação reativa equilibrada
                    float audioHeight = _smoothBands[bandIdx] * 28f;
                    float beatSpike = (_smoothBeat > 0.4f) ? MathF.Sin(x * 0.5f) * 8f * _smoothBeat : 0f;

                    float h = 4f + audioHeight + wave1 + wave2 + beatSpike;
                    _heightMap[x, y] = Math.Clamp(h, 0f, 75f);
                }
            }
        }

        public void Draw(CanvasDrawingSession ds)
        {
            if (_device == null || _width == 0 || _height == 0) return;

            // Fundo escuro do espaço
            ds.Clear(Color.FromArgb(255, 3, 1, 10));

            RenderVoxelTerrain(ds);
        }

        private void RenderVoxelTerrain(CanvasDrawingSession ds)
        {
            // Ponto de fuga no meio da tela para dar inclinação 3D
            float horizon = _height * 0.50f;
            float focalLength = _height * 0.55f;
            int stepX = 4;
            int screenWidthInt = (int)_width;

            // Limite superior onde o terreno para de desenhar (evita tela toda azul)
            float maxTerrainScreenY = _height * 0.35f;

            float[] yBuffer = new float[screenWidthInt + stepX];
            for (int i = 0; i < yBuffer.Length; i++) yBuffer[i] = _height;

            float distanceStep = 0.6f;
            float maxDistance = 35f;

            for (float distance = 1.2f; distance < maxDistance; distance += distanceStep)
            {
                float invDistance = 1.0f / distance;
                float mapY = (_cameraY + distance) % MapSize;
                float fog = Math.Clamp(1.0f - (distance / maxDistance), 0.05f, 1.0f);

                for (int sx = 0; sx < screenWidthInt; sx += stepX)
                {
                    float normX = ((float)sx / _width) - 0.5f;
                    float mapX = (_cameraX + normX * distance * 1.6f) % MapSize;
                    if (mapX < 0) mapX += MapSize;

                    int ix = (int)mapX;
                    int iy = (int)mapY;

                    float heightValue = _heightMap[ix, iy];

                    // Projeção 3D com trava no limite Y
                    float projectedY = horizon + (_cameraZ - heightValue) * invDistance * focalLength;

                    if (projectedY < yBuffer[sx])
                    {
                        // Corta tudo que tenta subir além de maxTerrainScreenY
                        float topY = Math.Max(projectedY, maxTerrainScreenY);
                        float bottomY = yBuffer[sx];

                        if (topY < bottomY)
                        {
                            // Cor reativa mudando tom com altura e beat
                            float hue = (0.52f + (heightValue / 75f) * 0.32f + _smoothBeat * 0.1f) % 1.0f;
                            Color voxelColor = HslToRgb(hue, 0.85f, (0.30f + _smoothBeat * 0.25f) * fog);

                            ds.FillRectangle(sx, topY, stepX, bottomY - topY, voxelColor);

                            // Borda neon no topo do voxel
                            Color topColor = Color.FromArgb((byte)(220 * fog), 0, 255, 230);
                            ds.FillRectangle(sx, topY, stepX, 1.5f, topColor);
                        }

                        for (int k = 0; k < stepX; k++)
                        {
                            if (sx + k < yBuffer.Length) yBuffer[sx + k] = topY;
                        }
                    }
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
            pipeline.BloomAmount = 0.35f;
            pipeline.BloomBlur = 2.0f;
            pipeline.BloomThreshold = 0.25f;
        }
    }
}