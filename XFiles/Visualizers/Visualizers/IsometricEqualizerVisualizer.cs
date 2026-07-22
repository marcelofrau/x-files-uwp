using System;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Windows.Foundation;
using Windows.UI;

namespace XFiles.Visualizers.Visualizers
{
    public sealed class IsometricEqualizerVisualizer : IAudioVisualizer
    {
        public string Name => "Isometric Equalizer";
        public string Id => "isometric-equalizer";

        private CanvasDevice _device;
        private float _width, _height, _time;

        private const int GridSize = 8;
        private const float MaxBlockHeight = 30f;
        private float[] _smoothLevels = new float[GridSize * GridSize];
        private float[] _smoothPeaks = new float[GridSize * GridSize];
        private float _smoothBeat;
        private const float SmoothFactor = 0.3f;

        public void Initialize(CanvasDevice device) { _device = device; }

        public void Update(AudioData data, TimeSpan elapsed)
        {
            _time = data.Time;
            _smoothBeat += (data.Beat - _smoothBeat) * 0.3f;

            int totalCells = GridSize * GridSize;
            for (int gy = 0; gy < GridSize; gy++)
            {
                for (int gx = 0; gx < GridSize; gx++)
                {
                    int cellIndex = gy * GridSize + gx;
                    int bandIndex = (cellIndex * AudioData.BandCount) / totalCells;
                    bandIndex = Math.Min(bandIndex, AudioData.BandCount - 1);

                    float target = data.BandLevels[bandIndex];
                    float targetPeak = data.BandPeaks[bandIndex];
                    _smoothLevels[cellIndex] += (target - _smoothLevels[cellIndex]) * SmoothFactor;
                    _smoothPeaks[cellIndex] += (targetPeak - _smoothPeaks[cellIndex]) * SmoothFactor;
                }
            }
        }

        public void Draw(CanvasDrawingSession ds)
        {
            if (_device == null || _width == 0 || _height == 0) return;
            ds.Clear(Color.FromArgb(255, 5, 5, 12));

            float tileW = _width / (GridSize + 4);
            float tileH = tileW * 0.5f;
            float offsetX = _width * 0.5f;
            float offsetY = _height * 0.3f;

            for (int gy = 0; gy < GridSize; gy++)
            {
                for (int gx = 0; gx < GridSize; gx++)
                {
                    int cellIndex = gy * GridSize + gx;
                    float level = _smoothLevels[cellIndex];
                    float peak = _smoothPeaks[cellIndex];
                    float blockHeight = level * MaxBlockHeight;

                    float sx = offsetX + (gx - gy) * tileW * 0.5f;
                    float sy = offsetY + (gx + gy) * tileH * 0.5f;

                    float bandT = (float)cellIndex / (GridSize * GridSize - 1);
                    Color blockColor = HslToRgb(
                        240f - bandT * 180f,
                        0.75f + _smoothBeat * 0.15f,
                        0.35f + level * 0.35f
                    );

                    DrawBlock(ds, sx, sy, tileW, tileH, blockHeight, blockColor);

                    if (peak > 0.1f)
                    {
                        float peakHeight = peak * MaxBlockHeight;
                        Color peakColor = HslToRgb(240f - bandT * 180f, 0.9f, 0.75f);
                        DrawBlockTop(ds, sx, sy - peakHeight + tileH * 0.5f, tileW, tileH, peakColor);
                    }
                }
            }
        }

        public void Resize(float width, float height) { _width = width; _height = height; }
        public void Dispose() { _device = null; }

        private static void DrawBlock(CanvasDrawingSession ds, float sx, float sy, float tileW, float tileH, float height, Color color)
        {
            if (height < 1f) return;
            float hw = tileW * 0.5f;
            float hh = tileH * 0.5f;
            var strokeStyle = new CanvasStrokeStyle();

            var topGeo = CanvasGeometry.CreatePolygon(ds, new[]
            {
                new System.Numerics.Vector2(sx, sy - height),
                new System.Numerics.Vector2(sx + hw, sy - height + hh),
                new System.Numerics.Vector2(sx, sy - height + tileH),
                new System.Numerics.Vector2(sx - hw, sy - height + hh)
            });
            Color topColor = Color.FromArgb(255,
                (byte)Math.Min(255, (int)(color.R * 1.3f)),
                (byte)Math.Min(255, (int)(color.G * 1.3f)),
                (byte)Math.Min(255, (int)(color.B * 1.3f)));
            ds.FillGeometry(topGeo, topColor);
            ds.DrawGeometry(topGeo, Color.FromArgb(40, 255, 255, 255), 0.5f);

            var leftGeo = CanvasGeometry.CreatePolygon(ds, new[]
            {
                new System.Numerics.Vector2(sx, sy - height + tileH),
                new System.Numerics.Vector2(sx - hw, sy - height + hh),
                new System.Numerics.Vector2(sx - hw, sy + hh),
                new System.Numerics.Vector2(sx, sy + tileH)
            });
            Color leftColor = Color.FromArgb(255,
                (byte)(color.R * 0.6f),
                (byte)(color.G * 0.6f),
                (byte)(color.B * 0.6f));
            ds.FillGeometry(leftGeo, leftColor);
            ds.DrawGeometry(leftGeo, Color.FromArgb(30, 0, 0, 0), 0.5f);

            var rightGeo = CanvasGeometry.CreatePolygon(ds, new[]
            {
                new System.Numerics.Vector2(sx, sy - height + tileH),
                new System.Numerics.Vector2(sx + hw, sy - height + hh),
                new System.Numerics.Vector2(sx + hw, sy + hh),
                new System.Numerics.Vector2(sx, sy + tileH)
            });
            Color rightColor = Color.FromArgb(255,
                (byte)(color.R * 0.4f),
                (byte)(color.G * 0.4f),
                (byte)(color.B * 0.4f));
            ds.FillGeometry(rightGeo, rightColor);
            ds.DrawGeometry(rightGeo, Color.FromArgb(20, 0, 0, 0), 0.5f);
        }

        private static void DrawBlockTop(CanvasDrawingSession ds, float sx, float sy, float tileW, float tileH, Color color)
        {
            float hw = tileW * 0.5f;
            float hh = tileH * 0.5f;
            var geo = CanvasGeometry.CreatePolygon(ds, new[]
            {
                new System.Numerics.Vector2(sx, sy),
                new System.Numerics.Vector2(sx + hw, sy + hh),
                new System.Numerics.Vector2(sx, sy + tileH),
                new System.Numerics.Vector2(sx - hw, sy + hh)
            });
            ds.FillGeometry(geo, color);
        }

        private static Color HslToRgb(float h, float s, float l)
        {
            h = h % 360f; if (h < 0f) h += 360f;
            float c = (1f - Math.Abs(2f * l - 1f)) * s;
            float x = c * (1f - Math.Abs((h / 60f) % 2f - 1f));
            float m = l - c / 2f;
            float r, g, b;
            if (h < 60) { r = c; g = x; b = 0; }
            else if (h < 120) { r = x; g = c; b = 0; }
            else if (h < 180) { r = 0; g = c; b = x; }
            else if (h < 240) { r = 0; g = x; b = c; }
            else if (h < 300) { r = x; g = 0; b = c; }
            else { r = c; g = 0; b = x; }
            return Color.FromArgb(255, (byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
        }
    }
}
