using System;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Windows.Foundation;
using Windows.UI;

namespace XFiles.Visualizers.Visualizers
{
    public sealed class PlasmaVisualizer : IAudioVisualizer
    {
        public string Name => "Plasma";
        public string Id => "plasma";

        private CanvasDevice _device;
        private float _width, _height, _time;

        private float _smoothBass, _smoothMid, _smoothTreble, _smoothBeat, _smoothAvgEnergy;
        private const float SmoothFactor = 0.25f;
        private const int GridStep = 6;

        private static byte[] _shaderBytecode;
        private static bool _shaderLoaded;
        private static bool _shaderAttempted;

        public void Initialize(CanvasDevice device) { _device = device; }

        public void Update(AudioData data, TimeSpan elapsed)
        {
            _time = data.Time;
            float bass = 0, mid = 0, treble = 0;
            for (int i = 0; i < 6; i++) bass += data.BandLevels[i]; bass /= 6f;
            for (int i = 10; i < 16; i++) mid += data.BandLevels[i]; mid /= 6f;
            for (int i = 20; i < 26; i++) treble += data.BandLevels[i]; treble /= 6f;
            _smoothBass += (bass - _smoothBass) * SmoothFactor;
            _smoothMid += (mid - _smoothMid) * SmoothFactor;
            _smoothTreble += (treble - _smoothTreble) * SmoothFactor;
            _smoothBeat += (data.Beat - _smoothBeat) * 0.3f;
            float avg = (bass + mid + treble) / 3f;
            _smoothAvgEnergy += (avg - _smoothAvgEnergy) * SmoothFactor;
        }

        public void Draw(CanvasDrawingSession ds)
        {
            if (_device == null || _width == 0 || _height == 0) return;

            if (!TryDrawWithShader(ds))
                DrawCpuFallback(ds);
        }

        public void Resize(float width, float height) { _width = width; _height = height; }
        public void Dispose() { _device = null; }

        private bool TryDrawWithShader(CanvasDrawingSession ds)
        {
            if (!_shaderAttempted)
            {
                _shaderAttempted = true;
                _shaderBytecode = LoadShaderBytecode();
                _shaderLoaded = _shaderBytecode != null && _shaderBytecode.Length > 0;
            }
            if (!_shaderLoaded) return false;
            try
            {
                var shader = new PixelShaderEffect(_shaderBytecode);
                var props = shader.Properties;
                props["uResolution"] = new System.Numerics.Vector2(_width, _height);
                props["uTime"] = _time;
                props["uBeat"] = _smoothBeat;
                float[] levels = BuildBandLevels();
                for (int i = 0; i < AudioData.BandCount; i++)
                    props[$"uBandLevels[{i}]"] = levels[i];
                for (int i = 0; i < AudioData.BandCount; i++)
                    props[$"uBandPeaks[{i}]"] = 0f;
                ds.Clear(Colors.Black);
                ds.DrawImage(shader);
                return true;
            }
            catch { _shaderLoaded = false; return false; }
        }

        private float[] BuildBandLevels()
        {
            var levels = new float[AudioData.BandCount];
            for (int i = 0; i < AudioData.BandCount; i++)
            {
                if (i < 6) levels[i] = _smoothBass;
                else if (i < 10) levels[i] = (_smoothBass + _smoothMid) * 0.5f;
                else if (i < 16) levels[i] = _smoothMid;
                else if (i < 20) levels[i] = (_smoothMid + _smoothTreble) * 0.5f;
                else levels[i] = _smoothTreble;
            }
            return levels;
        }

        private static byte[] LoadShaderBytecode()
        {
            try
            {
                var task = Windows.Storage.StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///Assets/Shaders/Plasma.cso"));
                var file = task.AsTask().GetAwaiter().GetResult();
                var buffer = Windows.Storage.FileIO.ReadBufferAsync(file).AsTask().GetAwaiter().GetResult();
                byte[] data;
                Windows.Security.Cryptography.CryptographicBuffer.CopyToByteArray(buffer, out data);
                return data;
            }
            catch { return null; }
        }

        private void DrawCpuFallback(CanvasDrawingSession ds)
        {
            ds.Clear(Colors.Black);
            int cols = (int)Math.Ceiling(_width / GridStep);
            int rows = (int)Math.Ceiling(_height / GridStep);
            float invW = 1f / _width, invH = 1f / _height;
            float freq1 = 3f + _smoothBass * 8f;
            float freq2 = 4f + _smoothMid * 6f;
            float freq3 = 5f + _smoothTreble * 10f;
            float t = _time;

            for (int gy = 0; gy < rows; gy++)
            {
                float v = (gy * GridStep) * invH;
                float yWave2Arg = v * freq2 + t * 0.7f;
                for (int gx = 0; gx < cols; gx++)
                {
                    float u = (gx * GridStep) * invW;
                    float wave1 = (float)Math.Sin(u * freq1 + t);
                    float wave2 = (float)Math.Cos(yWave2Arg);
                    float wave3 = (float)Math.Sin((u + v) * freq3 + t * 1.3);
                    float plasma = (wave1 + wave2 + wave3) / 3f;
                    float hue = plasma * 0.5f + t * 0.05f;
                    hue -= (float)Math.Floor(hue);
                    float sat = 0.7f + 0.3f * _smoothBeat;
                    float brightness = 0.5f + 0.5f * _smoothAvgEnergy;
                    float dx = u - 0.5f, dy = v - 0.5f;
                    float vignette = 1f - (float)Math.Sqrt(dx * dx + dy * dy) * 1.2f;
                    if (vignette < 0f) vignette = 0f;
                    ds.FillRectangle(gx * GridStep, gy * GridStep, GridStep, GridStep, HslToRgb(hue, sat, brightness * vignette));
                }
            }
        }

        private static Color HslToRgb(float h, float s, float l)
        {
            h -= (float)Math.Floor(h);
            float hue = h * 360f;
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
            pipeline.BloomEnabled = false;
            pipeline.FeedbackOpacity = 0.35f;
            pipeline.FeedbackZoom = 1.0f;
        }
    }
}
