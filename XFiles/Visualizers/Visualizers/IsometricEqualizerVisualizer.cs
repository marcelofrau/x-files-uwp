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

        private const int GridX = 8;
        private const int GridZ = 6;
        private float[] _smoothLevels = new float[GridX * GridZ];
        private float _smoothBass, _smoothBeat, _smoothAvg;
        private const float AudioSmooth = 0.25f;

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
                    int bandIdx = (cellIdx * AudioData.BandCount) / totalCells;
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
            float cx = _width * 0.5f;
            float cy = _height * 0.55f;
            float beatPulse = _smoothBeat;
            float hue = (_time * 0.03f) % 1.0f;

            // Outer glow — large, soft, beat-driven radius
            float outerR = _width * 0.35f * (1f + beatPulse * 0.6f);
            float outerH = _height * 0.2f * (1f + beatPulse * 0.5f);
            byte outerA = (byte)Math.Min(255, (int)(4 + beatPulse * 20));
            Color outerC = HslToRgb(hue, 0.5f, 0.2f);
            var outerGeo = CanvasGeometry.CreateEllipse(ds, cx, cy, outerR, outerH);
            ds.FillGeometry(outerGeo, Color.FromArgb(outerA, outerC.R, outerC.G, outerC.B));

            // Inner glow — tighter, brighter on beat
            float innerR = _width * 0.18f * (1f + beatPulse * 0.8f);
            float innerH = _height * 0.1f * (1f + beatPulse * 0.7f);
            byte innerA = (byte)Math.Min(255, (int)(3 + beatPulse * 28));
            Color innerC = HslToRgb((hue + 0.05f) % 1.0f, 0.6f, 0.3f);
            var innerGeo = CanvasGeometry.CreateEllipse(ds, cx, cy, innerR, innerH);
            ds.FillGeometry(innerGeo, Color.FromArgb(innerA, innerC.R, innerC.G, innerC.B));

            // Core flash — small, bright white on strong beats
            if (beatPulse > 0.3f)
            {
                float coreR = _width * 0.06f * beatPulse;
                float coreH = _height * 0.04f * beatPulse;
                byte coreA = (byte)Math.Min(200, (int)(beatPulse * 120));
                var coreGeo = CanvasGeometry.CreateEllipse(ds, cx, cy, coreR, coreH);
                ds.FillGeometry(coreGeo, Color.FromArgb(coreA, 255, 255, 255));
            }
        }

        private void DrawGrid(CanvasDrawingSession ds)
        {
            float cx = _width * 0.5f;
            float groundY = _height * 0.68f;

            float tileW = _width * 0.065f;
            float tileH = tileW * 0.5f;
            float halfW = tileW * 0.5f * 1.22f;
            float halfH = tileH * 0.5f * 1.22f;

            float maxHeight = _height * 0.35f;

            for (int gz = 0; gz < GridZ; gz++)
            {
                for (int gx = 0; gx < GridX; gx++)
                {
                    int cellIdx = gz * GridX + gx;
                    float level = _smoothLevels[cellIdx];
                    float blockH = Math.Max(4f, level * maxHeight);

                    float isoX = (gx - (GridX - 1) * 0.5f) - (gz - (GridZ - 1) * 0.5f);
                    float isoY = (gx - (GridX - 1) * 0.5f) + (gz - (GridZ - 1) * 0.5f);

                    float sx = cx + isoX * halfW;
                    float sy = groundY + isoY * halfH;

                    float depthFactor = (float)(gx + gz) / (GridX + GridZ - 2);
                    float fog = Math.Max(0.4f, 1f - (1f - depthFactor) * 0.3f);

                    float gxNorm = (float)gx / (GridX - 1);
                    float colHue = (0.0f + gxNorm * 0.75f) % 1.0f;
                    float beatWobble = (float)Math.Sin(_time * 2.5f + gx * 1.1f) * 0.04f;
                    float hue = (colHue + beatWobble) % 1.0f;
                    float satBoost = 0.75f + _smoothBeat * 0.25f;
                    Color blockColor = HslToRgb(
                        hue,
                        satBoost,
                        (0.32f + level * 0.40f) * fog
                    );

                    DrawVoxelBlock(ds, sx, sy, tileW, tileH, blockH, blockColor);
                }
            }
        }

        private static void DrawVoxelBlock(CanvasDrawingSession ds, float sx, float sy,
            float w, float h, float blockH, Color color)
        {
            float hw = w * 0.5f;
            float hh = h * 0.5f;

            float topY = sy - blockH;

            var topGeo = CanvasGeometry.CreatePolygon(ds, new[]
            {
                new System.Numerics.Vector2(sx, topY - hh),
                new System.Numerics.Vector2(sx + hw, topY),
                new System.Numerics.Vector2(sx, topY + hh),
                new System.Numerics.Vector2(sx - hw, topY)
            });

            Color topColor = Color.FromArgb(255,
                (byte)Math.Min(255, (int)(color.R * 1.3f)),
                (byte)Math.Min(255, (int)(color.G * 1.3f)),
                (byte)Math.Min(255, (int)(color.B * 1.3f)));
            ds.FillGeometry(topGeo, topColor);

            var leftGeo = CanvasGeometry.CreatePolygon(ds, new[]
            {
                new System.Numerics.Vector2(sx - hw, topY),
                new System.Numerics.Vector2(sx, topY + hh),
                new System.Numerics.Vector2(sx, sy + hh),
                new System.Numerics.Vector2(sx - hw, sy)
            });

            Color leftColor = Color.FromArgb(255,
                (byte)(color.R * 0.70f),
                (byte)(color.G * 0.70f),
                (byte)(color.B * 0.70f));
            ds.FillGeometry(leftGeo, leftColor);

            var rightGeo = CanvasGeometry.CreatePolygon(ds, new[]
            {
                new System.Numerics.Vector2(sx, topY + hh),
                new System.Numerics.Vector2(sx + hw, topY),
                new System.Numerics.Vector2(sx + hw, sy),
                new System.Numerics.Vector2(sx, sy + hh)
            });

            Color rightColor = Color.FromArgb(255,
                (byte)(color.R * 0.45f),
                (byte)(color.G * 0.45f),
                (byte)(color.B * 0.45f));
            ds.FillGeometry(rightGeo, rightColor);

            ds.DrawGeometry(topGeo, Color.FromArgb(200, 255, 255, 255), 1.0f);
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
            pipeline.FeedbackOpacity = 0f;
            pipeline.FeedbackZoom = 1.0f;
            pipeline.FeedbackDecay = 0f;
            pipeline.BloomEnabled = false;
            pipeline.BloomAmount = 0f;
            pipeline.BloomBlur = 0f;
            pipeline.BloomThreshold = 0.65f;
        }
    }
}
