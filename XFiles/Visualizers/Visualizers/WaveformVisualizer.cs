using System;
using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Geometry;
using Windows.Foundation;
using Windows.UI;

namespace XFiles.Visualizers.Visualizers
{
    // Melhorias em rela��o � vers�o anterior:
    // 1. Sample caching: GetSample() era chamado ~9x por �ndice por frame. Agora �
    //    resolvido uma vez por frame num array fixo (FixedSampleCount), reamostrado
    //    do buffer bruto � desacopla a resolu��o visual do tamanho do buffer de �udio.
    // 2. Trail temporal: guarda os �ltimos N frames reamostrados e desenha como
    //    camadas ascendentes com alpha/hue decrescente. Isso d� profundidade real
    //    (hist�rico do som), em vez de duplicatas est�ticas da mesma amostra.
    // 3. Preenchimento em gradiente sob a onda principal via CanvasLinearGradientBrush.
    // 4. CanvasStrokeStyle est�tico (evita aloca��o por chamada).
    // 5. Amplitude base e idle state aumentados � o estado parado agora tem uma
    //    respira��o percept�vel em vez de ficar quase reto.
    public sealed class WaveformVisualizer : IAudioVisualizer
    {
        public string Name => "Waveform";
        public string Id => "waveform";

        private static readonly CanvasStrokeStyle RoundCap =
            new CanvasStrokeStyle { StartCap = CanvasCapStyle.Round, EndCap = CanvasCapStyle.Round };

        private const int FixedSampleCount = 96;
        private const int TrailFrames = 5;

        private CanvasDevice _device;
        private float _width, _height, _time;

        private float[] _smoothWave;
        private int _smoothCount;
        private float _smoothBass, _smoothMid, _smoothBeat;
        private float[] _smoothBands;
        private const float SmoothFactor = 0.35f;
        private float _activity;

        // Buffer fixo reamostrado, usado para desenho (independe do tamanho do FFT/waveform).
        private readonly float[] _sampleCache = new float[FixedSampleCount];

        // Ring buffer de frames anteriores para o trail.
        private readonly float[][] _trail = CreateTrailBuffer();
        private int _trailHead;

        private static float[][] CreateTrailBuffer()
        {
            var buf = new float[TrailFrames][];
            for (int i = 0; i < TrailFrames; i++) buf[i] = new float[FixedSampleCount];
            return buf;
        }

        public void Initialize(CanvasDevice device) { _device = device; }

        public void Update(AudioData data, TimeSpan elapsed)
        {
            _time = data.Time;
            _smoothBeat += (data.Beat - _smoothBeat) * 0.25f;

            int count = Math.Min(data.WaveformCount, data.Waveform.Length);
            if (_smoothWave == null || _smoothWave.Length != data.Waveform.Length)
                _smoothWave = new float[data.Waveform.Length];
            for (int i = 0; i < count; i++)
                _smoothWave[i] += (data.Waveform[i] - _smoothWave[i]) * SmoothFactor;
            _smoothCount = count;

            float peak = 0f;
            for (int i = 0; i < count; i++) peak = Math.Max(peak, Math.Abs(_smoothWave[i]));
            _activity += (peak - _activity) * 0.15f;

            int half = Math.Max(1, data.BandLevels.Length / 2);
            float bass = 0, mid = 0;
            for (int i = 0; i < half; i++) bass += data.BandLevels[i];
            for (int i = half; i < data.BandLevels.Length; i++) mid += data.BandLevels[i];
            _smoothBass += (bass / half - _smoothBass) * 0.2f;
            int midCount = Math.Max(1, data.BandLevels.Length - half);
            _smoothMid += (mid / midCount - _smoothMid) * 0.2f;

            if (_smoothBands == null || _smoothBands.Length != data.BandLevels.Length)
                _smoothBands = new float[data.BandLevels.Length];
            for (int i = 0; i < data.BandLevels.Length; i++)
                _smoothBands[i] += (data.BandLevels[i] - _smoothBands[i]) * 0.3f;

            BuildFrameSamples();
            PushTrail();
        }

        // Resolve GetSample() uma vez por �ndice fixo e cacheia � usado por todos os
        // m�todos de desenho no lugar de recalcular.
        private void BuildFrameSamples()
        {
            for (int i = 0; i < FixedSampleCount; i++)
                _sampleCache[i] = GetSample(i);
        }

        private void PushTrail()
        {
            _trailHead = (_trailHead + 1) % TrailFrames;
            Array.Copy(_sampleCache, _trail[_trailHead], FixedSampleCount);
        }

        private float GetSample(int fixedIndex)
        {
            float t = (float)fixedIndex / (FixedSampleCount - 1);
            float idleWave = MathF.Sin(_time * 1.2f + fixedIndex * 0.22f) * 0.07f
                           + MathF.Sin(_time * 0.55f + fixedIndex * 0.09f) * 0.03f;
            if (_activity < 0.01f || _smoothCount < 2) return idleWave;

            int rawIndex = Math.Min(_smoothCount - 1, (int)(t * (_smoothCount - 1)));
            float sample = _smoothWave[rawIndex];
            float mix = Math.Min(1f, _activity * 8f);
            return sample * mix + idleWave * (1f - mix);
        }

        public void Draw(CanvasDrawingSession ds)
        {
            if (_device == null || _width == 0 || _height == 0) return;
            ds.Clear(Color.FromArgb(255, 8, 8, 13));
            DrawGradientBackground(ds);
            DrawCenterGuide(ds);
            DrawTrail(ds);
            DrawWaveformFill(ds);
            DrawWaveform(ds);
            DrawBassLine(ds);
            DrawPeakDots(ds);
        }

        public void Resize(float width, float height) { _width = width; _height = height; }
        public void Dispose() { _device = null; }

        private void DrawGradientBackground(CanvasDrawingSession ds)
        {
            float cx = _width * 0.5f, cy = _height * 0.5f;
            float radius = Math.Max(_width, _height) * 0.7f;
            var inner = CanvasGeometry.CreateEllipse(ds, cx, cy, radius * 0.4f, radius * 0.4f);
            ds.FillGeometry(inner, Color.FromArgb(48, 0, 40, 60));
            var outer = CanvasGeometry.CreateEllipse(ds, cx, cy, radius, radius);
            ds.FillGeometry(outer, Color.FromArgb(24, 0, 20, 30));
        }

        private void DrawCenterGuide(CanvasDrawingSession ds)
        {
            float mainY = _height * 0.40f;
            float bassY = _height * 0.75f;
            float waveWidth = _width * 0.85f;
            float startX = (_width - waveWidth) * 0.5f;

            float act = Math.Min(1f, _activity * 4f);
            byte guideA = (byte)(8 * act + _smoothBeat * 8);
            ds.DrawLine(startX, mainY, startX + waveWidth, mainY,
                Color.FromArgb(guideA, 60, 180, 180), 1f);
            byte bassA = (byte)(4 * act);
            ds.DrawLine(startX, bassY, startX + waveWidth, bassY,
                Color.FromArgb(bassA, 60, 120, 120), 1f);
        }

        // Camadas ascendentes de frames anteriores � d� o efeito de "hist�rico" do
        // som subindo, em vez de linhas duplicadas na mesma posi��o.
        private void DrawTrail(CanvasDrawingSession ds)
        {
            float centerY = _height * 0.40f;
            float waveWidth = _width * 0.85f;
            float startX = (_width - waveWidth) * 0.5f;
            float step = waveWidth / (FixedSampleCount - 1);
            float act = Math.Min(1f, _activity * 4f);
            if (act < 0.02f) return;

            for (int age = 1; age < TrailFrames; age++)
            {
                int idx = (_trailHead - age + TrailFrames * 2) % TrailFrames;
                float[] frame = _trail[idx];
                float ageT = (float)age / (TrailFrames - 1);
                float yOffset = -age * (14f + _smoothBeat * 6f);
                byte a = (byte)(act * 90 * (1f - ageT));
                if (a < 3) continue;
                float hue = (0.52f + ageT * 0.25f + _time * 0.015f) % 1f;
                var col = HslToRgb(hue, 0.75f, 0.55f);
                float thickness = 2.2f * (1f - ageT * 0.5f);

                float px = startX, py = centerY + yOffset - frame[0] * _height * 0.26f;
                for (int i = 1; i < FixedSampleCount; i++)
                {
                    float x = startX + i * step;
                    float y = centerY + yOffset - frame[i] * _height * 0.26f;
                    ds.DrawLine(px, py, x, y, Color.FromArgb(a, col.R, col.G, col.B), thickness, RoundCap);
                    px = x; py = y;
                }
            }
        }

        // Preenchimento em gradiente sob a onda principal, tingido pela energia de graves/m�dios.
        private void DrawWaveformFill(CanvasDrawingSession ds)
        {
            float centerY = _height * 0.40f;
            float waveWidth = _width * 0.85f;
            float startX = (_width - waveWidth) * 0.5f;
            float step = waveWidth / (FixedSampleCount - 1);
            float act = Math.Min(1f, _activity * 4f);
            if (act < 0.02f) return;

            float hue = (0.5f + _smoothBass * 0.25f) % 1f;
            var top = HslToRgb(hue, 0.85f, 0.55f);
            var bottom = HslToRgb((hue + 0.5f) % 1f, 0.7f, 0.4f);

            var builder = new CanvasPathBuilder(ds);
            builder.BeginFigure(startX, centerY - _sampleCache[0] * _height * 0.30f);
            for (int i = 1; i < FixedSampleCount; i++)
            {
                float x = startX + i * step;
                float y = centerY - _sampleCache[i] * _height * 0.30f;
                builder.AddLine(x, y);
            }
            builder.AddLine(startX + waveWidth, centerY + 60f);
            builder.AddLine(startX, centerY + 60f);
            builder.EndFigure(CanvasFigureLoop.Closed);
            var geometry = CanvasGeometry.CreatePath(builder);
            builder.Dispose();

            byte alphaTop = (byte)(70 * act);
            byte alphaBottom = 0;
            var brush = new CanvasLinearGradientBrush(
                ds, Color.FromArgb(alphaTop, top.R, top.G, top.B),
                Color.FromArgb(alphaBottom, bottom.R, bottom.G, bottom.B))
            {
                StartPoint = new Vector2(0, centerY - _height * 0.30f),
                EndPoint = new Vector2(0, centerY + 60f)
            };
            ds.FillGeometry(geometry, brush);
            brush.Dispose();
        }

        private void DrawWaveform(CanvasDrawingSession ds)
        {
            float centerY = _height * 0.40f;
            float waveWidth = _width * 0.85f;
            float startX = (_width - waveWidth) * 0.5f;

            float act = Math.Min(1f, _activity * 4f);
            byte glowA = (byte)(70 * act);
            byte midA = (byte)(90 * act);
            DrawWaveformLine(ds, startX, waveWidth, centerY, 9f, Color.FromArgb(glowA, 0, 200, 255), 1.12f, true);
            DrawWaveformLine(ds, startX, waveWidth, centerY, 4.5f, Color.FromArgb(midA, 0, 255, 255), 1.0f, false);
            DrawWaveformLine(ds, startX, waveWidth, centerY, 2.2f, Color.FromArgb((byte)(255 * act), 200, 255, 255), 1.0f, false);
            DrawMirrorLine(ds, startX, waveWidth, centerY, 1.8f, Color.FromArgb((byte)(150 * act), 255, 90, 220), 0.55f);
        }

        private void DrawWaveformLine(CanvasDrawingSession ds, float startX, float waveWidth,
            float centerY, float thickness, Color baseColor, float amplitudeMul, bool wide)
        {
            float step = waveWidth / (FixedSampleCount - 1);
            for (int i = 0; i < FixedSampleCount - 1; i++)
            {
                float x1 = startX + i * step, x2 = startX + (i + 1) * step;
                float s0 = _sampleCache[i], s1 = _sampleCache[i + 1];
                float y1 = centerY - s0 * _height * 0.30f * amplitudeMul;
                float y2 = centerY - s1 * _height * 0.30f * amplitudeMul;
                float t = (float)i / (FixedSampleCount - 1);
                float amplitude = Math.Abs(s0);
                float brightness = 0.85f + 0.15f * _smoothBeat;
                byte r = (byte)(Lerp(80, 255, t) * brightness);
                byte g = (byte)(Lerp(255, 160, t) * brightness);
                byte b = (byte)(255 * brightness);
                float ampBoost = wide ? 0.5f + 0.5f * amplitude : 0.6f + 0.4f * amplitude;
                byte a = (byte)Math.Min(255, (int)(baseColor.A * ampBoost));
                ds.DrawLine(x1, y1, x2, y2, Color.FromArgb(a, r, g, b), thickness, RoundCap);
            }
        }

        private void DrawMirrorLine(CanvasDrawingSession ds, float startX, float waveWidth,
            float centerY, float thickness, Color baseColor, float amplitudeMul)
        {
            float step = waveWidth / (FixedSampleCount - 1);
            for (int i = 0; i < FixedSampleCount - 1; i++)
            {
                float x1 = startX + i * step, x2 = startX + (i + 1) * step;
                float s0 = _sampleCache[i], s1 = _sampleCache[i + 1];
                float y1 = centerY + s0 * _height * 0.30f * amplitudeMul;
                float y2 = centerY + s1 * _height * 0.30f * amplitudeMul;
                float amplitude = Math.Abs(s0);
                float brightness = 0.85f + 0.15f * _smoothBeat;
                byte r = (byte)(Lerp(80, 255, 1 - (float)i / (FixedSampleCount - 1)) * brightness);
                byte g = (byte)(Lerp(160, 80, (float)i / (FixedSampleCount - 1)) * brightness);
                byte b = (byte)(220 * brightness);
                byte a = (byte)Math.Min(255, (int)(baseColor.A * (0.35f + 0.35f * amplitude)));
                ds.DrawLine(x1, y1, x2, y2, Color.FromArgb(a, r, g, b), thickness, RoundCap);
            }
        }

        private void DrawBassLine(CanvasDrawingSession ds)
        {
            float bassY = _height * 0.75f;
            float waveWidth = _width * 0.85f;
            float startX = (_width - waveWidth) * 0.5f;
            float step = waveWidth / (FixedSampleCount - 1);
            float amp = (_smoothBass * 0.6f + _smoothBeat * 0.2f) * Math.Min(1f, _activity * 5f);
            float baseThick = 2f + amp * 4f;

            for (int i = 0; i < FixedSampleCount - 1; i++)
            {
                float s0 = _sampleCache[i], s1 = _sampleCache[i + 1];
                float x1 = startX + i * step, x2 = startX + (i + 1) * step;
                float y1 = bassY - Math.Abs(s0) * _height * 0.10f;
                float y2 = bassY - Math.Abs(s1) * _height * 0.10f;
                float t = (float)i / (FixedSampleCount - 1);
                float hue = (0.55f + t * 0.15f + _time * 0.02f) % 1f;
                var col = HslToRgb(hue, 0.8f, 0.5f + amp * 0.3f);
                byte a = (byte)(120 * Math.Min(1f, _activity * 4f) + (byte)(amp * 100));
                float thick = baseThick * (0.8f + 0.4f * Math.Abs(s0));
                ds.DrawLine(x1, y1, x2, y2, Color.FromArgb(a, col.R, col.G, col.B), thick, RoundCap);
            }
        }

        private void DrawPeakDots(CanvasDrawingSession ds)
        {
            float centerY = _height * 0.40f;
            float waveWidth = _width * 0.85f;
            float startX = (_width - waveWidth) * 0.5f;
            float step = waveWidth / (FixedSampleCount - 1);
            float beatPulse = 1f + _smoothBeat * 0.5f;

            float act = Math.Min(1f, _activity * 3f);
            for (int i = 0; i < FixedSampleCount; i += 3)
            {
                float s = _sampleCache[i];
                float amp = Math.Abs(s) * act;
                if (amp < 0.04f) continue;
                float x = startX + i * step;
                float y = centerY - s * _height * 0.30f;
                float size = 2f + amp * 9f * beatPulse;
                float t = (float)i / (FixedSampleCount - 1);
                float hue = (t * 0.3f + _time * 0.05f) % 1f;
                var col = HslToRgb(hue, 0.9f, 0.6f);
                ds.FillCircle(x, y, size, Color.FromArgb((byte)(170 * amp), col.R, col.G, col.B));
            }
        }

        private static float Lerp(float a, float b, float t) => a + (b - a) * t;

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
            pipeline.FeedbackOpacity = 0.42f;
            pipeline.FeedbackZoom = 1.0016f;
            pipeline.BloomAmount = 0.17f;
            pipeline.BloomBlur = 5.5f;
            pipeline.BloomThreshold = 0.12f;
            pipeline.VignetteEnabled = true;
        }
    }
}