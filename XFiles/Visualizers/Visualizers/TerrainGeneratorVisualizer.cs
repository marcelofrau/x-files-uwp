using System;
using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Windows.Foundation;
using Windows.UI;

namespace XFiles.Visualizers.Visualizers
{
    public sealed class TerrainGeneratorVisualizer : IAudioVisualizer
    {
        public string Name => "Terrain Generator";
        public string Id => "terrain-generator";

        private CanvasDevice _device;
        private float _width, _height, _time;

        private const int Rows = 32;
        private const int Cols = 48;
        private readonly float[,] _heightMap = new float[Rows, Cols];
        private float _scrollOffset;
        private float _scrollAccum;
        private float _smoothScroll;

        private readonly float[] _smoothBands = new float[AudioData.BandCount];
        private float _smoothBass, _smoothBeat;
        private const float AudioSmooth = 0.25f;

        private readonly Vector2[,] _screenGrid = new Vector2[Rows, Cols];
        private readonly Vector2[] _quadBuffer = new Vector2[4];

        public void Initialize(CanvasDevice device) { _device = device; }

        public void Update(AudioData data, TimeSpan elapsed)
        {
            _time = data.Time;

            float bass = 0;
            for (int i = 0; i < 6; i++) bass += data.BandLevels[i]; bass /= 6f;
            _smoothBass += (bass - _smoothBass) * AudioSmooth;
            _smoothBeat += (data.Beat - _smoothBeat) * 0.4f;

            for (int i = 0; i < AudioData.BandCount; i++)
                _smoothBands[i] += (data.BandLevels[i] - _smoothBands[i]) * AudioSmooth;

            float scrollSpeed = 5.5f + _smoothBass * 4.0f;
            _scrollAccum += scrollSpeed * (float)elapsed.TotalSeconds;
            if (_scrollAccum >= 1f)
            {
                for (int r = Rows - 1; r > 0; r--)
                    for (int c = 0; c < Cols; c++)
                        _heightMap[r, c] = _heightMap[r - 1, c];

                for (int c = 0; c < Cols; c++)
                {
                    int bandIdx = (int)((float)c / Cols * AudioData.BandCount) % AudioData.BandCount;
                    float wave1 = (float)Math.Sin(_time * 3f + c * 0.25f) * 0.12f;
                    float wave2 = (float)Math.Cos(_time * 5.5f + c * 0.18f) * 0.08f * _smoothBass;
                    float spike = (_smoothBeat > 0.5f) ? (float)Math.Sin(c * 0.7f + _time * 12f) * 0.18f * _smoothBeat : 0f;
                    float val = _smoothBands[bandIdx] * 1.1f + wave1 + wave2 + spike;
                    _heightMap[0, c] = Math.Clamp(val, 0f, 1f);
                }
                _scrollAccum -= 1f;
                if (_scrollAccum > 1f) _scrollAccum = 1f;
            }
            _smoothScroll += (_scrollAccum - _smoothScroll) * 0.45f;
            _scrollOffset = _smoothScroll;
        }

        public void Draw(CanvasDrawingSession ds)
        {
            if (_device == null || _width == 0 || _height == 0) return;

            ds.Clear(Color.FromArgb(255, 3, 2, 8));

            DrawSun(ds);
            ProjectTerrainGrid();
            DrawTerrainMesh(ds);

            float horizonY = _height * 0.38f;
            Color horizColor = Color.FromArgb((byte)(180 + _smoothBeat * 75), 255, 0, 128);
            ds.DrawLine(0, horizonY, _width, horizonY, horizColor, 2f);
        }

        public void Resize(float width, float height) { _width = width; _height = height; }
        public void Dispose() { _device = null; }

        private void DrawSun(CanvasDrawingSession ds)
        {
            float cx = _width * 0.5f;
            float cy = _height * 0.38f;
            float sunRadius = Math.Min(_width, _height) * 0.22f;

            var clipGeo = CanvasGeometry.CreateRectangle(ds, 0, 0, _width, cy + 2f);
            using (ds.CreateLayer(1f, clipGeo))
            {
                Color sunCore = HslToRgb((_time * 0.02f) % 1.0f, 0.9f, 0.5f);
                ds.FillCircle(cx, cy - sunRadius * 0.3f, sunRadius, Color.FromArgb(40, sunCore.R, sunCore.G, sunCore.B));
                ds.FillCircle(cx, cy - sunRadius * 0.3f, sunRadius * 0.7f, Color.FromArgb(200, 255, 200, 0));
                ds.FillCircle(cx, cy - sunRadius * 0.3f, sunRadius * 0.4f, Colors.White);
            }
        }

        private void ProjectTerrainGrid()
        {
            float cx = _width * 0.5f;
            float horizonY = _height * 0.38f;
            float nearY = _height * 1.05f;
            float terrainHeightRange = nearY - horizonY;

            float maxDepth = Rows;

            for (int r = 0; r < Rows; r++)
            {
                float depth = (r - _scrollOffset);
                float depthNorm = Math.Clamp(depth / maxDepth, 0.01f, 1f);

                float perspY = horizonY + terrainHeightRange * (float)Math.Pow(depthNorm, 1.8f);
                float perspScale = (float)Math.Pow(depthNorm, 1.2f);
                float rowWidth = _width * 1.3f * perspScale;

                for (int c = 0; c < Cols; c++)
                {
                    float colNorm = (float)c / (Cols - 1) - 0.5f;
                    float xPos = cx + colNorm * rowWidth;

                    float h = _heightMap[r, c];
                    float hFactor = h * _height * 0.30f * perspScale;
                    float yPos = perspY - hFactor;

                    _screenGrid[r, c] = new Vector2(xPos, yPos);
                }
            }
        }

        private void DrawTerrainMesh(CanvasDrawingSession ds)
        {
            for (int r = Rows - 2; r >= 0; r--)
            {
                float depthNorm = (float)r / Rows;
                float fog = Math.Clamp(1f - depthNorm * 0.7f, 0.1f, 1f);

                float hue = 0.55f + depthNorm * 0.25f;
                Color gridColor = HslToRgb(hue % 1.0f, 0.85f, (0.2f + _smoothBeat * 0.15f) * fog);
                byte alpha = (byte)(180 * fog);
                Color strokeColor = Color.FromArgb(alpha, gridColor.R, gridColor.G, gridColor.B);

                for (int c = 0; c < Cols - 1; c++)
                {
                    _quadBuffer[0] = _screenGrid[r, c];
                    _quadBuffer[1] = _screenGrid[r, c + 1];
                    _quadBuffer[2] = _screenGrid[r + 1, c + 1];
                    _quadBuffer[3] = _screenGrid[r + 1, c];

                    byte fillAlpha = (byte)(40 * fog);
                    using (var geo = CanvasGeometry.CreatePolygon(ds, _quadBuffer))
                    {
                        ds.FillGeometry(geo, Color.FromArgb(fillAlpha, gridColor.R, gridColor.G, gridColor.B));
                    }
                }

                for (int c = 0; c < Cols - 1; c++)
                {
                    ds.DrawLine(_screenGrid[r, c], _screenGrid[r, c + 1], strokeColor, 1.2f * fog);
                }

                for (int c = 0; c < Cols; c++)
                {
                    ds.DrawLine(_screenGrid[r, c], _screenGrid[r + 1, c], strokeColor, 1.0f * fog);
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

        public void ConfigurePipeline(PostProcessPipeline pipeline)
        {
            pipeline.FeedbackOpacity = 0.80f;
            pipeline.FeedbackZoom = 1.01f;
            pipeline.FeedbackDecay = 0.04f;
            pipeline.BloomAmount = 0.10f;
            pipeline.BloomBlur = 4f;
            pipeline.BloomThreshold = 0.4f;
        }
    }
}
