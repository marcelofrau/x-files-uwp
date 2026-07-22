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

        private const int GridX = 6;
        private const int GridZ = 5;
        private float[] _smoothLevels = new float[GridX * GridZ];
        private float _smoothBass, _smoothBeat, _smoothAvg;
        private const float AudioSmooth = 0.25f;
        private float _cameraAngle = -0.5f;

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
            ds.Clear(Color.FromArgb(255, 5, 3, 10));

            DrawSkyGlow(ds);
            DrawGrid(ds);
        }

        public void Resize(float width, float height) { _width = width; _height = height; }
        public void Dispose() { _device = null; }

        private void DrawSkyGlow(CanvasDrawingSession ds)
        {
            float glowAlpha = (byte)Math.Min(255, (int)(12 + _smoothBeat * 25));
            float hue = (_time * 0.03f) % 1.0f;
            Color c = HslToRgb(hue, 0.5f, 0.25f);
            var geo = CanvasGeometry.CreateEllipse(ds, _width * 0.5f, _height * 0.3f, _width * 0.5f, _height * 0.25f);
            ds.FillGeometry(geo, Color.FromArgb((byte)glowAlpha, c.R, c.G, c.B));
        }

        private void DrawGrid(CanvasDrawingSession ds)
        {
            float cx = _width * 0.5f;
            float groundY = _height * 0.65f;
            float tileW = _width * 0.055f;
            float tileH = tileW * 0.5f;
            float maxHeight = _height * 0.38f;
            float gap = tileW * 0.15f;
            float cosCam = (float)Math.Cos(_cameraAngle);
            float sinCam = (float)Math.Sin(_cameraAngle);

            for (int gz = GridZ - 1; gz >= 0; gz--)
            {
                for (int gx = 0; gx < GridX; gx++)
                {
                    int cellIdx = gz * GridX + gx;
                    float level = _smoothLevels[cellIdx];
                    float blockH = level * maxHeight;
                    if (blockH < 2f) blockH = 2f;

                    float relX = (gx - GridX * 0.5f) * (1f + gap / tileW);
                    float relZ = (gz - GridZ * 0.5f) * (1f + gap / tileW);
                    float rotX = relX * cosCam - relZ * sinCam;
                    float rotZ = relX * sinCam + relZ * cosCam;
                    float depth = rotZ + GridZ * 0.5f;
                    float depthScale = 1f / (1f + depth * 0.07f);

                    float sx = cx + rotX * tileW * depthScale;
                    float sy = groundY + rotZ * tileH * 0.4f * depthScale;
                    float drawW = tileW * 0.75f * depthScale;
                    float drawH = tileH * 0.4f * depthScale;
                    float drawBlockH = blockH * depthScale;

                    float fog = Math.Max(0.2f, 1f - depth * 0.035f);
                    float bandT = (float)cellIdx / (GridX * GridZ - 1);
                    Color blockColor = HslToRgb(
                        (0.65f - bandT * 0.35f + _time * 0.015f) % 1.0f,
                        0.6f + _smoothBeat * 0.2f,
                        (0.25f + level * 0.4f) * fog
                    );

                    DrawVoxelBlock(ds, sx, sy, drawW, drawH, drawBlockH, blockColor);
                }
            }
        }

        private static void DrawVoxelBlock(CanvasDrawingSession ds, float sx, float sy,
            float w, float h, float blockH, Color color)
        {
            float hw = w * 0.5f;
            float hh = h * 0.5f;

            var topGeo = CanvasGeometry.CreatePolygon(ds, new[]
            {
                new System.Numerics.Vector2(sx, sy - blockH),
                new System.Numerics.Vector2(sx + hw, sy - blockH + hh),
                new System.Numerics.Vector2(sx, sy - blockH + h),
                new System.Numerics.Vector2(sx - hw, sy - blockH + hh)
            });
            Color topColor = Color.FromArgb(255,
                (byte)Math.Min(255, (int)(color.R * 1.4f)),
                (byte)Math.Min(255, (int)(color.G * 1.4f)),
                (byte)Math.Min(255, (int)(color.B * 1.4f)));
            ds.FillGeometry(topGeo, topColor);

            var leftGeo = CanvasGeometry.CreatePolygon(ds, new[]
            {
                new System.Numerics.Vector2(sx, sy - blockH + h),
                new System.Numerics.Vector2(sx - hw, sy - blockH + hh),
                new System.Numerics.Vector2(sx - hw, sy + hh),
                new System.Numerics.Vector2(sx, sy + h)
            });
            Color leftColor = Color.FromArgb(255,
                (byte)(color.R * 0.55f),
                (byte)(color.G * 0.55f),
                (byte)(color.B * 0.55f));
            ds.FillGeometry(leftGeo, leftColor);

            var rightGeo = CanvasGeometry.CreatePolygon(ds, new[]
            {
                new System.Numerics.Vector2(sx, sy - blockH + h),
                new System.Numerics.Vector2(sx + hw, sy - blockH + hh),
                new System.Numerics.Vector2(sx + hw, sy + hh),
                new System.Numerics.Vector2(sx, sy + h)
            });
            Color rightColor = Color.FromArgb(255,
                (byte)(color.R * 0.35f),
                (byte)(color.G * 0.35f),
                (byte)(color.B * 0.35f));
            ds.FillGeometry(rightGeo, rightColor);
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
            pipeline.Rotation = 0f;
            pipeline.SlideX = 0f;
            pipeline.SlideY = 0f;
            pipeline.FeedbackOpacity = 0.50f;
            pipeline.FeedbackZoom = 1.0005f;
            pipeline.FeedbackDecay = 0f;
            pipeline.BloomAmount = 0.2f;
            pipeline.BloomBlur = 5f;
            pipeline.BloomThreshold = 0.3f;
        }
    }
}
