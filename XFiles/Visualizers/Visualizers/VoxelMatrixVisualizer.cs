using System;
using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Windows.Foundation;
using Windows.UI;

namespace XFiles.Visualizers.Visualizers
{
    public sealed class VoxelMatrixVisualizer : IAudioVisualizer
    {
        public string Name => "Voxel Matrix";
        public string Id => "voxel-matrix";

        private CanvasDevice _device;
        private float _width, _height, _time;

        private const int GridX = 8;
        private const int GridZ = 8;
        private readonly float[] _smoothLevels = new float[GridX * GridZ];
        private float _smoothBass, _smoothBeat, _smoothAvg;
        private const float AudioSmooth = 0.25f;
        private float _cameraAngle;

        private struct VoxelCell
        {
            public float Depth;
            public float RotX;
            public float RotZ;
            public int CellIndex;
        }

        private readonly VoxelCell[] _renderCells = new VoxelCell[GridX * GridZ];
        private readonly Vector2[] _polyBuffer = new Vector2[4];

        public void Initialize(CanvasDevice device) { _device = device; }

        public void Update(AudioData data, TimeSpan elapsed)
        {
            _time = data.Time;

            float bass = 0;
            for (int i = 0; i < 6; i++) bass += data.BandLevels[i]; bass /= 6f;
            float avg = 0;
            for (int i = 0; i < AudioData.BandCount; i++) avg += data.BandLevels[i]; avg /= AudioData.BandCount;

            _smoothBass += (bass - _smoothBass) * AudioSmooth;
            _smoothBeat += (data.Beat - _smoothBeat) * 0.4f;
            _smoothAvg += (avg - _smoothAvg) * AudioSmooth;

            _cameraAngle += (0.15f + _smoothAvg * 0.2f) * (float)elapsed.TotalSeconds;

            int totalCells = GridX * GridZ;
            for (int gz = 0; gz < GridZ; gz++)
            {
                for (int gx = 0; gx < GridX; gx++)
                {
                    int cellIdx = gz * GridX + gx;
                    int bandIdx = ((totalCells - 1 - cellIdx) * AudioData.BandCount) / totalCells;
                    bandIdx = Math.Min(bandIdx, AudioData.BandCount - 1);
                    _smoothLevels[cellIdx] += (data.BandLevels[bandIdx] - _smoothLevels[cellIdx]) * AudioSmooth;
                }
            }
        }

        public void Draw(CanvasDrawingSession ds)
        {
            if (_device == null || _width == 0 || _height == 0) return;

            ds.Clear(Color.FromArgb(255, 4, 2, 10));

            DrawSkyGlow(ds);
            DrawCity(ds);
        }

        public void Resize(float width, float height) { _width = width; _height = height; }
        public void Dispose() { _device = null; }

        private void DrawSkyGlow(CanvasDrawingSession ds)
        {
            float cx = _width * 0.5f;
            float cy = _height * 0.35f;
            float rx = _width * 0.5f;
            float ry = _height * 0.25f;

            Color c = HslToRgb((_time * 0.02f) % 1.0f, 0.8f, 0.25f + _smoothBeat * 0.15f);
            byte alpha = (byte)(30 + _smoothBeat * 40);

            ds.FillCircle(cx, cy, ry, Color.FromArgb(alpha, c.R, c.G, c.B));
            ds.FillCircle(cx, cy, ry * 0.4f, Color.FromArgb((byte)(alpha * 1.5f), 255, 255, 255));
        }

        private void DrawCity(CanvasDrawingSession ds)
        {
            float cx = _width * 0.5f;
            float groundY = _height * 0.65f;
            float tileW = _width * 0.055f;
            float tileH = tileW * 0.5f;
            float maxHeight = _height * 0.30f;

            float cosCam = (float)Math.Cos(_cameraAngle);
            float sinCam = (float)Math.Sin(_cameraAngle);

            int cellCounter = 0;
            for (int gz = 0; gz < GridZ; gz++)
            {
                for (int gx = 0; gx < GridX; gx++)
                {
                    float relX = gx - (GridX - 1) * 0.5f;
                    float relZ = gz - (GridZ - 1) * 0.5f;

                    float rotX = relX * cosCam - relZ * sinCam;
                    float rotZ = relX * sinCam + relZ * cosCam;

                    _renderCells[cellCounter] = new VoxelCell
                    {
                        Depth = rotZ,
                        RotX = rotX,
                        RotZ = rotZ,
                        CellIndex = gz * GridX + gx
                    };
                    cellCounter++;
                }
            }

            Array.Sort(_renderCells, (a, b) => a.Depth.CompareTo(b.Depth));

            for (int i = 0; i < _renderCells.Length; i++)
            {
                var cell = _renderCells[i];
                int cellIdx = cell.CellIndex;

                float level = _smoothLevels[cellIdx];
                float blockH = Math.Max(3f, level * maxHeight);

                float depthOffset = cell.RotZ + GridZ * 0.5f;
                float depthScale = 1f / (1f + depthOffset * 0.06f);

                float sx = cx + cell.RotX * tileW * depthScale;
                float sy = groundY + cell.RotZ * tileH * 0.45f * depthScale;
                float drawW = tileW * 0.85f * depthScale;
                float drawH = tileH * 0.45f * depthScale;
                float drawBlockH = blockH * depthScale;

                float fog = Math.Clamp(1f - (depthOffset * 0.05f), 0.2f, 1f);
                float bandT = (float)cellIdx / (_renderCells.Length - 1);

                Color blockColor = HslToRgb(
                    (0.55f - bandT * 0.35f + _time * 0.03f) % 1.0f,
                    0.85f,
                    (0.3f + level * 0.4f) * fog
                );

                DrawVoxelBlock(ds, sx, sy, drawW, drawH, drawBlockH, blockColor, level);
            }
        }

        private void DrawVoxelBlock(CanvasDrawingSession ds, float sx, float sy, float w, float h, float blockH, Color color, float level)
        {
            float hw = w * 0.5f;
            float hh = h * 0.5f;

            _polyBuffer[0] = new Vector2(sx, sy - blockH);
            _polyBuffer[1] = new Vector2(sx + hw, sy - blockH + hh);
            _polyBuffer[2] = new Vector2(sx, sy - blockH + h);
            _polyBuffer[3] = new Vector2(sx - hw, sy - blockH + hh);

            Color topColor = Color.FromArgb(255,
                (byte)Math.Min(255, color.R * 1.5f),
                (byte)Math.Min(255, color.G * 1.5f),
                (byte)Math.Min(255, color.B * 1.5f));

            using (var geo = CanvasGeometry.CreatePolygon(ds, _polyBuffer))
            {
                ds.FillGeometry(geo, topColor);
            }

            _polyBuffer[0] = new Vector2(sx, sy - blockH + h);
            _polyBuffer[1] = new Vector2(sx - hw, sy - blockH + hh);
            _polyBuffer[2] = new Vector2(sx - hw, sy + hh);
            _polyBuffer[3] = new Vector2(sx, sy + h);

            Color leftColor = Color.FromArgb(255, (byte)(color.R * 0.6f), (byte)(color.G * 0.6f), (byte)(color.B * 0.6f));

            using (var geo = CanvasGeometry.CreatePolygon(ds, _polyBuffer))
            {
                ds.FillGeometry(geo, leftColor);
            }

            _polyBuffer[0] = new Vector2(sx, sy - blockH + h);
            _polyBuffer[1] = new Vector2(sx + hw, sy - blockH + hh);
            _polyBuffer[2] = new Vector2(sx + hw, sy + hh);
            _polyBuffer[3] = new Vector2(sx, sy + h);

            Color rightColor = Color.FromArgb(255, (byte)(color.R * 0.35f), (byte)(color.G * 0.35f), (byte)(color.B * 0.35f));

            using (var geo = CanvasGeometry.CreatePolygon(ds, _polyBuffer))
            {
                ds.FillGeometry(geo, rightColor);
            }

            if (level > 0.3f)
            {
                Color wireColor = Color.FromArgb((byte)(level * 200), 255, 255, 255);
                ds.DrawLine(sx, sy - blockH, sx + hw, sy - blockH + hh, wireColor, 1.2f);
                ds.DrawLine(sx + hw, sy - blockH + hh, sx, sy - blockH + h, wireColor, 1.2f);
                ds.DrawLine(sx, sy - blockH + h, sx - hw, sy - blockH + hh, wireColor, 1.2f);
                ds.DrawLine(sx - hw, sy - blockH + hh, sx, sy - blockH, wireColor, 1.2f);
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
            pipeline.FeedbackOpacity = 0.30f;
            pipeline.FeedbackZoom = 1.003f;
            pipeline.FeedbackDecay = 0.02f;
            pipeline.BloomEnabled = true;
            pipeline.BloomAmount = 0.06f;
            pipeline.BloomBlur = 0.8f;
            pipeline.BloomThreshold = 0.5f;
            pipeline.VignetteEnabled = true;
            pipeline.VignetteAmount = 0.25f;
        }
    }
}
