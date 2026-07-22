using System;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Windows.Foundation;
using Windows.UI;

namespace XFiles.Visualizers.Visualizers
{
    public sealed class InfernoCoreVisualizer : IAudioVisualizer
    {
        public string Name => "Inferno Core";
        public string Id => "inferno-core";

        private CanvasDevice _device;
        private float _width, _height, _time;

        private float _smoothBass, _smoothMid, _smoothTreble, _smoothBeat, _smoothAvg;
        private const float AudioSmooth = 0.25f;
        private float _cubeAngleX, _cubeAngleY, _cubeAngleZ;
        private float _flashIntensity;

        private const int PlasmaGrid = 8;
        private float[,,] _plasmaNoise;

        public void Initialize(CanvasDevice device) { _device = device; InitPlasmaNoise(); }

        public void Update(AudioData data, TimeSpan elapsed)
        {
            _time = data.Time;
            float dt = (float)elapsed.TotalSeconds;

            float bass = 0, mid = 0, treble = 0, avg = 0;
            for (int i = 0; i < 6; i++) bass += data.BandLevels[i]; bass /= 6f;
            for (int i = 10; i < 16; i++) mid += data.BandLevels[i]; mid /= 6f;
            for (int i = 20; i < 26; i++) treble += data.BandLevels[i]; treble /= 6f;
            for (int i = 0; i < AudioData.BandCount; i++) avg += data.BandLevels[i]; avg /= AudioData.BandCount;

            _smoothBass += (bass - _smoothBass) * AudioSmooth;
            _smoothMid += (mid - _smoothMid) * AudioSmooth;
            _smoothTreble += (treble - _smoothTreble) * AudioSmooth;
            _smoothBeat += (data.Beat - _smoothBeat) * 0.4f;
            _smoothAvg += (avg - _smoothAvg) * AudioSmooth;

            float rotSpeed = 0.4f + _smoothMid * 1.5f;
            _cubeAngleX += rotSpeed * dt * (1f + _smoothBeat * 0.5f);
            _cubeAngleY += rotSpeed * 0.7f * dt * (1f + _smoothBeat * 0.3f);
            _cubeAngleZ += rotSpeed * 0.3f * dt;

            if (data.Beat > 0.7f) _flashIntensity = 1.0f;
            _flashIntensity *= 0.88f;
        }

        public void Draw(CanvasDrawingSession ds)
        {
            if (_device == null || _width == 0 || _height == 0) return;
            ds.Clear(Color.FromArgb(255, 8, 2, 2));

            DrawPlasmaBackground(ds);
            DrawCube(ds);
            DrawBeatFlash(ds);
            DrawRadialEnergy(ds);
        }

        public void Resize(float width, float height) { _width = width; _height = height; }
        public void Dispose() { _device = null; }

        private void InitPlasmaNoise()
        {
            _plasmaNoise = new float[PlasmaGrid, PlasmaGrid, PlasmaGrid];
            var rng = new Random(42);
            for (int x = 0; x < PlasmaGrid; x++)
                for (int y = 0; y < PlasmaGrid; y++)
                    for (int z = 0; z < PlasmaGrid; z++)
                        _plasmaNoise[x, y, z] = (float)rng.NextDouble();
        }

        private void DrawPlasmaBackground(CanvasDrawingSession ds)
        {
            int cols = (int)Math.Ceiling(_width / 10f);
            int rows = (int)Math.Ceiling(_height / 10f);
            float invW = 1f / _width, invH = 1f / _height;
            float plasmaSpeed = 0.3f + _smoothBass * 0.5f;
            float trebleMod = _smoothTreble * 0.15f;

            for (int gy = 0; gy < rows; gy++)
            {
                float v = gy * 10f * invH;
                for (int gx = 0; gx < cols; gx++)
                {
                    float u = gx * 10f * invW;
                    float wave1 = (float)Math.Sin(u * 4f + _time * plasmaSpeed);
                    float wave2 = (float)Math.Cos(v * 3f + _time * plasmaSpeed * 0.7f);
                    float wave3 = (float)Math.Sin((u + v) * 2.5f + _time * plasmaSpeed * 1.3f);
                    float plasma = (wave1 + wave2 + wave3) / 3f;

                    float hue = plasma * 0.3f + trebleMod + _time * 0.04f;
                    hue -= (float)Math.Floor(hue);
                    float fireHue = hue * 0.15f;
                    float sat = 0.8f + plasma * 0.2f;
                    float brightness = (0.15f + plasma * 0.15f + _smoothBass * 0.1f);

                    float dx = u - 0.5f, dy = v - 0.5f;
                    float vignette = 1f - (float)Math.Sqrt(dx * dx + dy * dy) * 0.8f;
                    if (vignette < 0f) vignette = 0f;

                    brightness *= vignette;
                    ds.FillRectangle(gx * 10f, gy * 10f, 10f, 10f, HslToRgb(fireHue, sat, brightness));
                }
            }
        }

        private void DrawCube(CanvasDrawingSession ds)
        {
            float cx = _width * 0.5f, cy = _height * 0.5f;
            float baseSize = Math.Min(_width, _height) * 0.14f;
            float scale = 1f + _smoothBass * 0.6f + _flashIntensity * 0.3f;
            float size = baseSize * scale;

            float cosX = (float)Math.Cos(_cubeAngleX), sinX = (float)Math.Sin(_cubeAngleX);
            float cosY = (float)Math.Cos(_cubeAngleY), sinY = (float)Math.Sin(_cubeAngleY);
            float cosZ = (float)Math.Cos(_cubeAngleZ), sinZ = (float)Math.Sin(_cubeAngleZ);

            float[,] verts = new float[8, 3];
            float[,] projected = new float[8, 2];
            float[] depth = new float[8];
            float half = size * 0.5f;

            float[,] corners = new float[8, 3] {
                {-1,-1,-1}, {1,-1,-1}, {1,1,-1}, {-1,1,-1},
                {-1,-1,1}, {1,-1,1}, {1,1,1}, {-1,1,1}
            };

            for (int i = 0; i < 8; i++)
            {
                float x = corners[i, 0] * half;
                float y = corners[i, 1] * half;
                float z = corners[i, 2] * half;

                float y1 = y * cosX - z * sinX;
                float z1 = y * sinX + z * cosX;
                float x2 = x * cosY + z1 * sinY;
                float z2 = -x * sinY + z1 * cosY;
                float x3 = x2 * cosZ - y1 * sinZ;
                float y3 = x2 * sinZ + y1 * cosZ;

                float fov = 300f / (300f + z2);
                projected[i, 0] = cx + x3 * fov;
                projected[i, 1] = cy + y3 * fov;
                depth[i] = z2;
            }

            int[,] edges = new int[12, 2] {
                {0,1},{1,2},{2,3},{3,0},
                {4,5},{5,6},{6,7},{7,4},
                {0,4},{1,5},{2,6},{3,7}
            };

            float cubeHue = (_time * 0.05f) % 1.0f;
            Color edgeColor = HslToRgb(cubeHue, 0.9f, 0.5f + _smoothBeat * 0.3f);

            for (int e = 0; e < 12; e++)
            {
                int a = edges[e, 0], b = edges[e, 1];
                float avgDepth = (depth[a] + depth[b]) * 0.5f;
                float depthFade = Math.Max(0.15f, 1f - avgDepth * 0.003f);

                byte er = (byte)Math.Min(255, (int)(edgeColor.R * depthFade));
                byte eg = (byte)Math.Min(255, (int)(edgeColor.G * depthFade));
                byte eb = (byte)Math.Min(255, (int)(edgeColor.B * depthFade));

                ds.DrawLine(projected[a, 0], projected[a, 1], projected[b, 0], projected[b, 1],
                    Color.FromArgb(255, er, eg, eb), 2f + _smoothBeat * 1.5f);

                float glowA = 20 + (int)(_smoothBeat * 20);
                ds.DrawLine(projected[a, 0], projected[a, 1], projected[b, 0], projected[b, 1],
                    Color.FromArgb((byte)glowA, er, eg, eb), 6f);
            }

            for (int i = 0; i < 8; i++)
            {
                ds.FillGeometry(CanvasGeometry.CreateCircle(ds, projected[i, 0], projected[i, 1], 2.5f + _smoothBeat * 1.5f),
                    Color.FromArgb(200, 255, 200, 100));
            }
        }

        private void DrawBeatFlash(CanvasDrawingSession ds)
        {
            if (_flashIntensity < 0.05f) return;
            float cx = _width * 0.5f, cy = _height * 0.5f;
            float flashR = Math.Max(_width, _height) * 0.6f * _flashIntensity;
            byte a = (byte)Math.Min(255, (int)(60 * _flashIntensity));
            Color flashColor = Color.FromArgb(a, 255, 180, 60);
            var geo = CanvasGeometry.CreateEllipse(ds, cx, cy, flashR, flashR);
            ds.FillGeometry(geo, flashColor);
        }

        private void DrawRadialEnergy(CanvasDrawingSession ds)
        {
            float cx = _width * 0.5f, cy = _height * 0.5f;
            float maxR = Math.Min(_width, _height) * 0.45f;
            int lineCount = 16;
            var strokeStyle = new CanvasStrokeStyle { StartCap = CanvasCapStyle.Round, EndCap = CanvasCapStyle.Round };

            for (int i = 0; i < lineCount; i++)
            {
                float angle = (float)i / lineCount * 2f * (float)Math.PI + _time * 0.1f;
                float intensity = _smoothBeat * 0.6f + _flashIntensity * 0.4f;
                if (intensity < 0.05f) continue;

                float innerR = Math.Min(_width, _height) * 0.1f * (1f + _smoothBass * 0.3f);
                float outerR = innerR + maxR * 0.3f * intensity;
                float hue = (float)i / lineCount;
                Color c = HslToRgb(hue, 0.7f, 0.5f);
                byte a = (byte)Math.Min(255, (int)(intensity * 80));

                ds.DrawLine(
                    cx + (float)Math.Cos(angle) * innerR, cy + (float)Math.Sin(angle) * innerR,
                    cx + (float)Math.Cos(angle) * outerR, cy + (float)Math.Sin(angle) * outerR,
                    Color.FromArgb(a, c.R, c.G, c.B), 1.5f, strokeStyle);
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
        }
    }
}
