using System;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Windows.Foundation;
using Windows.UI;

namespace XFiles.Visualizers.Visualizers
{
    public sealed class RetroOscilloscopeVisualizer : IAudioVisualizer
    {
        public string Name => "Retro Oscilloscope";
        public string Id => "retro-oscilloscope";

        private CanvasDevice _device;
        private float _width, _height, _time;

        private float[] _currentWaveform = new float[2048];
        private int _currentWaveformCount;

        private float _smoothBass, _smoothMid, _smoothBeat;
        private const float AudioSmooth = 0.25f;

        private const int GhostFrames = 5;
        private readonly float[][] _ghostHistory = new float[GhostFrames][];
        private readonly int[] _ghostCounts = new int[GhostFrames];
        private int _ghostIndex;

        public void Initialize(CanvasDevice device)
        {
            _device = device;
            for (int i = 0; i < GhostFrames; i++)
            {
                _ghostHistory[i] = new float[2048];
                _ghostCounts[i] = 0;
            }
        }

        public void Update(AudioData data, TimeSpan elapsed)
        {
            _time = data.Time;

            float bass = 0, mid = 0;
            for (int i = 0; i < 6; i++) bass += data.BandLevels[i]; bass /= 6f;
            for (int i = 10; i < 16; i++) mid += data.BandLevels[i]; mid /= 6f;

            _smoothBass += (bass - _smoothBass) * AudioSmooth;
            _smoothMid += (mid - _smoothMid) * AudioSmooth;
            _smoothBeat += (data.Beat - _smoothBeat) * 0.4f;

            // 1. C�PIA SEGURA DOS DADOS DA ONDA (EVITA BUG DE REFER�NCIA)
            if (data.Waveform != null && data.WaveformCount > 0)
            {
                _currentWaveformCount = Math.Min(data.WaveformCount, 2048);
                Array.Copy(data.Waveform, _currentWaveform, _currentWaveformCount);

                // Copia para o ring buffer de fantasmas
                _ghostCounts[_ghostIndex] = _currentWaveformCount;
                Array.Copy(_currentWaveform, _ghostHistory[_ghostIndex], _currentWaveformCount);
                _ghostIndex = (_ghostIndex + 1) % GhostFrames;
            }
        }

        public void Draw(CanvasDrawingSession ds)
        {
            if (_device == null || _width == 0 || _height == 0) return;

            // Fundo escuro levemente esverdeado (Tubo CRT desligado)
            ds.Clear(Color.FromArgb(255, 1, 6, 2));

            DrawGrid(ds);
            DrawGhostTraces(ds);
            DrawWaveform(ds);
            DrawLissajous(ds);
            DrawPhosphorGlow(ds);
            DrawVignette(ds);
        }

        public void Resize(float width, float height) { _width = width; _height = height; }
        public void Dispose() { _device = null; }

        private void DrawGrid(CanvasDrawingSession ds)
        {
            Color gridColor = Color.FromArgb(25, 40, 220, 40);
            Color subGridColor = Color.FromArgb(12, 40, 220, 40);

            // Grade reticular principal 10x8 de oscilosc�pio
            float stepX = _width / 10f;
            for (int i = 1; i < 10; i++)
            {
                float x = i * stepX;
                ds.DrawLine(x, 0, x, _height, gridColor, 1f);
            }

            float stepY = _height / 8f;
            for (int i = 1; i < 8; i++)
            {
                float y = i * stepY;
                ds.DrawLine(0, y, _width, y, gridColor, 1f);
            }

            // Eixos centrais com marca��es de calibra��o
            float cx = _width * 0.5f;
            float cy = _height * 0.5f;
            Color centerColor = Color.FromArgb(60, 50, 255, 50);

            ds.DrawLine(cx, 0, cx, _height, centerColor, 1.5f);
            ds.DrawLine(0, cy, _width, cy, centerColor, 1.5f);

            // Ticks de precis�o no centro
            for (float x = 0; x < _width; x += stepX * 0.2f)
                ds.DrawLine(x, cy - 3f, x, cy + 3f, subGridColor, 1f);

            for (float y = 0; y < _height; y += stepY * 0.2f)
                ds.DrawLine(cx - 3f, y, cx + 3f, y, subGridColor, 1f);
        }

        private void DrawGhostTraces(CanvasDrawingSession ds)
        {
            Color phosphorGreen = Color.FromArgb(255, 30, 200, 30);
            float cy = _height * 0.38f;
            float amplitude = _height * 0.32f;

            for (int g = 0; g < GhostFrames; g++)
            {
                int gi = (_ghostIndex + g) % GhostFrames;
                float[] wave = _ghostHistory[gi];
                int count = _ghostCounts[gi];
                if (wave == null || count <= 1) continue;

                float age = (float)(GhostFrames - g) / GhostFrames;
                byte alpha = (byte)Math.Clamp(40 * (1f - age), 0, 255);
                if (alpha < 4) continue;

                using (var builder = new CanvasPathBuilder(ds))
                {
                    int step = Math.Max(1, count / 400);
                    builder.BeginFigure(0, cy + wave[0] * amplitude);

                    for (int i = step; i < count; i += step)
                    {
                        float x = (float)i / count * _width;
                        float y = cy + wave[i] * amplitude;
                        builder.AddLine(x, y);
                    }

                    builder.EndFigure(CanvasFigureLoop.Open);

                    using (var geo = CanvasGeometry.CreatePath(builder))
                    {
                        ds.DrawGeometry(geo, Color.FromArgb(alpha, phosphorGreen.R, phosphorGreen.G, phosphorGreen.B), 1.2f);
                    }
                }
            }
        }

        private void DrawWaveform(CanvasDrawingSession ds)
        {
            if (_currentWaveform == null || _currentWaveformCount <= 1) return;

            float cy = _height * 0.38f;
            float amplitude = _height * 0.32f;

            Color phosphorGreen = Color.FromArgb(255, 180, 255, 180); // N�cleo brilhante
            Color glowGreen = Color.FromArgb(120, 30, 240, 30);      // Halos de f�sforo

            using (var builder = new CanvasPathBuilder(ds))
            {
                int step = Math.Max(1, _currentWaveformCount / 600);
                builder.BeginFigure(0, cy + _currentWaveform[0] * amplitude);

                for (int i = step; i < _currentWaveformCount; i += step)
                {
                    float x = (float)i / _currentWaveformCount * _width;
                    float y = cy + _currentWaveform[i] * amplitude;
                    builder.AddLine(x, y);
                }

                builder.EndFigure(CanvasFigureLoop.Open);

                using (var geo = CanvasGeometry.CreatePath(builder))
                {
                    // Passada de Brilho Externo (Glow)
                    ds.DrawGeometry(geo, glowGreen, 4.5f);
                    // Passada do Raio Principal
                    ds.DrawGeometry(geo, phosphorGreen, 1.8f);
                }
            }
        }

        private void DrawLissajous(CanvasDrawingSession ds)
        {
            if (_currentWaveform == null || _currentWaveformCount <= 1) return;

            float cx = _width * 0.5f;
            float cy = _height * 0.5f;
            float radius = Math.Min(_width, _height) * 0.14f * (0.6f + _smoothBass * 0.6f);

            Color lissaColor = Color.FromArgb((byte)(40 + _smoothBeat * 50), 50, 255, 50);

            using (var builder = new CanvasPathBuilder(ds))
            {
                int pointCount = 180;
                float freqMul = 1f + _smoothMid * 2.5f;
                float phase = _time * 0.8f;

                for (int i = 0; i <= pointCount; i++)
                {
                    float t = (float)i / pointCount * (float)(Math.PI * 2);
                    float lx = cx + MathF.Sin(t * freqMul + phase) * radius;
                    float ly = cy + MathF.Cos(t * 2f + phase * 0.6f) * radius;

                    if (i == 0) builder.BeginFigure(lx, ly);
                    else builder.AddLine(lx, ly);
                }

                builder.EndFigure(CanvasFigureLoop.Open);

                using (var geo = CanvasGeometry.CreatePath(builder))
                {
                    ds.DrawGeometry(geo, lissaColor, 1.2f);
                }
            }
        }

        private void DrawPhosphorGlow(CanvasDrawingSession ds)
        {
            float intensity = 0.3f + _smoothBeat * 0.4f;
            byte a = (byte)Math.Clamp(15 * intensity, 0, 255);
            Color glow = Color.FromArgb(a, 40, 220, 40);

            // Usando com instru��o usando (using) para descarte de mem�ria correto
            using (var geo = CanvasGeometry.CreateEllipse(ds, _width * 0.5f, _height * 0.5f, _width * 0.45f, _height * 0.40f))
            {
                ds.FillGeometry(geo, glow);
            }
        }

        private void DrawVignette(CanvasDrawingSession ds)
        {
            // Vinheta de borda escura simulo vidro curvo de monitor CRT
            float w = _width, h = _height;
            float borderX = w * 0.08f;
            float borderY = h * 0.08f;

            Color vignetteColor = Color.FromArgb(140, 0, 0, 0);

            ds.FillRectangle(0, 0, borderX, h, vignetteColor);
            ds.FillRectangle(w - borderX, 0, borderX, h, vignetteColor);
            ds.FillRectangle(0, 0, w, borderY, vignetteColor);
            ds.FillRectangle(0, h - borderY, w, borderY, vignetteColor);
        }

        public void ConfigurePipeline(PostProcessPipeline pipeline)
        {
            // Efeito F�sforo de Reten��o CRT (Afterglow)
            pipeline.FeedbackOpacity = 0.60f;    // Mant�m o feixe de el�trons esmaecendo suavemente
            pipeline.FeedbackZoom = 1.001f;     // Lev�ssima expans�o do brilho
            pipeline.FeedbackDecay = 0.04f;
            pipeline.BloomAmount = 0.12f;       // Brilho intenso de f�sforo verde
            pipeline.BloomBlur = 5f;
            pipeline.BloomThreshold = 0.35f;
        }
    }
}