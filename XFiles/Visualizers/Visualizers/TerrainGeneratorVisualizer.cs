using System;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Windows.Foundation;
using Windows.UI;

namespace XFiles.Visualizers.Visualizers
{
    /// <summary>
    /// Terrain Generator visualizer: demoscene-style 2D terrain scrolling toward the viewer.
    /// Height = frequency band magnitudes. Scroll speed = time + bass.
    /// Color = depth-based gradient (green near → blue far). Beat = terrain pulse.
    /// Glow via GaussianBlur + Screen. MilkDrop-inspired: wireframe + solid fill,
    /// depth fog, retro vector aesthetic.
    /// </summary>
    public sealed class TerrainGeneratorVisualizer : IAudioVisualizer
    {
        public string Name => "Terrain Generator";
        public string Id => "terrain-generator";

        private CanvasDevice _device;
        private float _width, _height, _time;

        // Terrain rows
        private const int MaxRows = 40;
        private const int RowPoints = 64;
        private float[,] _heightMap;  // [row, point]
        private float _scrollOffset;

        // Audio
        private readonly float[] _smoothBands = new float[AudioData.BandCount];
        private float _smoothBass, _smoothBeat;
        private const float AudioSmooth = 0.25f;

        // Perspective
        private const float HorizonY = 0.35f;  // fraction from top
        private const float NearY = 1.05f;     // fraction from top

        public void Initialize(CanvasDevice device)
        {
            _device = device;
            _heightMap = new float[MaxRows, RowPoints];
            var rng = new Random(42);
            for (int r = 0; r < MaxRows; r++)
                for (int p = 0; p < RowPoints; p++)
                    _heightMap[r, p] = (float)rng.NextDouble();
        }

        public void Update(AudioData data, TimeSpan elapsed)
        {
            _time = data.Time;

            float bass = 0;
            for (int i = 0; i < 6; i++) bass += data.BandLevels[i]; bass /= 6f;
            _smoothBass += (bass - _smoothBass) * AudioSmooth;
            _smoothBeat += (data.Beat - _smoothBeat) * 0.4f;

            for (int i = 0; i < AudioData.BandCount; i++)
                _smoothBands[i] += (data.BandLevels[i] - _smoothBands[i]) * AudioSmooth;

            // Scroll terrain
            _scrollOffset += (0.3f + _smoothBass * 0.8f) * (float)elapsed.TotalSeconds;

            // Update heightmap: shift rows forward, generate new back row
            for (int r = MaxRows - 1; r > 0; r--)
                for (int p = 0; p < RowPoints; p++)
                    _heightMap[r, p] = _heightMap[r - 1, p];

            // New back row from band levels
            for (int p = 0; p < RowPoints; p++)
            {
                int bandIdx = (int)((float)p / RowPoints * AudioData.BandCount) % AudioData.BandCount;
                _heightMap[0, p] = _smoothBands[bandIdx] * 0.7f + (float)Math.Sin(_time * 2f + p * 0.3f) * 0.15f;
            }
        }

        public void Draw(CanvasDrawingSession ds)
        {
            if (_device == null || _width == 0 || _height == 0) return;
            ds.Clear(Color.FromArgb(255, 2, 3, 8));
            DrawTerrain(ds);
        }

        public void Resize(float width, float height) { _width = width; _height = height; }

        public void Dispose() { _device = null; }

        private void DrawTerrain(CanvasDrawingSession ds)
        {
            float horizonY = _height * HorizonY;
            float nearY = _height * NearY;
            float terrainHeight = nearY - horizonY;
            float halfWidth = _width * 0.5f;

            var strokeStyle = new CanvasStrokeStyle { StartCap = CanvasCapStyle.Round, EndCap = CanvasCapStyle.Round };

            for (int r = MaxRows - 1; r >= 0; r--)
            {
                float depthNorm = (float)r / MaxRows;
                float perspY = horizonY + terrainHeight * (float)Math.Pow(depthNorm, 1.5f);

                // Perspective narrowing
                float perspScale = (float)Math.Pow(depthNorm, 0.8f);
                float rowWidth = halfWidth * perspScale;

                // Depth fog
                float fog = 1f - depthNorm * 0.6f;

                // Color: green (near) → blue (far) with band modulation
                float hue = 0.33f + depthNorm * 0.15f;
                float sat = 0.6f + _smoothBeat * 0.2f;
                float lum = 0.15f + (1f - depthNorm) * 0.3f + _smoothBeat * 0.1f;
                lum *= fog;
                Color rowColor = HslToRgb(hue, sat, lum);

                // Fill polygon for solid terrain
                using (var builder = new CanvasPathBuilder(ds))
                {
                    float leftX = halfWidth - rowWidth;
                    float rightX = halfWidth + rowWidth;

                    builder.BeginFigure(leftX, perspY);
                    builder.AddLine(rightX, perspY);

                    if (r > 0)
                    {
                        float prevDepth = (float)(r - 1) / MaxRows;
                        float prevY = horizonY + terrainHeight * (float)Math.Pow(prevDepth, 1.5f);
                        float prevScale = (float)Math.Pow(prevDepth, 0.8f);
                        float prevWidth = halfWidth * prevScale;
                        builder.AddLine(halfWidth + prevWidth, prevY);
                        builder.AddLine(halfWidth - prevWidth, prevY);
                    }
                    else
                    {
                        builder.AddLine(rightX, perspY - 5f);
                        builder.AddLine(leftX, perspY - 5f);
                    }
                    builder.EndFigure(CanvasFigureLoop.Closed);

                    var fillGeo = CanvasGeometry.CreatePath(builder);
                    byte fa = (byte)Math.Min(255, (int)(120 * fog));
                    ds.FillGeometry(fillGeo, Color.FromArgb(fa, rowColor.R, rowColor.G, rowColor.B));
                }

                // Wireframe line
                float lineThickness = 1f + (1f - depthNorm) * 1.5f;
                byte la = (byte)Math.Min(255, (int)(255 * fog));
                ds.DrawLine(halfWidth - rowWidth, perspY, halfWidth + rowWidth, perspY,
                    Color.FromArgb(la, rowColor.R, rowColor.G, rowColor.B), lineThickness, strokeStyle);

                // Height peaks at band positions
                for (int p = 0; p < RowPoints; p++)
                {
                    float h = _heightMap[r, p];
                    if (h < 0.1f) continue;
                    float xPos = halfWidth - rowWidth + (float)p / RowPoints * rowWidth * 2f;
                    float peakY = perspY - h * terrainHeight * 0.3f * perspScale;
                    float peakHue = hue + h * 0.1f;
                    Color peakColor = HslToRgb(peakHue - (float)Math.Floor(peakHue), 0.9f, 0.4f + h * 0.3f);
                    byte pa = (byte)Math.Min(255, (int)(200 * fog * h));
                    ds.DrawLine(xPos, perspY, xPos, peakY, Color.FromArgb(pa, peakColor.R, peakColor.G, peakColor.B),
                        1f + h * 2f, strokeStyle);
                }
            }

            // Horizon glow line
            byte ga = (byte)Math.Min(255, (int)(100 + _smoothBeat * 50));
            ds.DrawLine(0, horizonY, _width, horizonY, Color.FromArgb(ga, 50, 200, 50), 2f);
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
