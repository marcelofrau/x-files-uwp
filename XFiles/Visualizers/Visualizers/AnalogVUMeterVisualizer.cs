using System;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;
using Windows.Foundation;
using Windows.UI;

namespace XFiles.Visualizers.Visualizers
{
    public sealed class AnalogVUMeterVisualizer : IAudioVisualizer
    {
        public string Name => "Analog VU Meter";
        public string Id => "analog-vu-meter";

        private CanvasDevice _device;
        private float _width, _height, _time;

        private float _needleBass, _needleMid, _needleTreble;
        private float _velBass, _velMid, _velTreble;
        private float _smoothBass, _smoothMid, _smoothTreble, _smoothBeat;
        private const float AudioSmooth = 0.35f;
        private const float SpringK = 220f;
        private const float Damping = 12f;
        private const float InputGain = 0.85f;

        // --- Objetos reutiliz�veis (Zero GC Allocations no Draw) ---
        private CanvasTextFormat _labelFormat;
        private CanvasTextFormat _scaleTextFormat;
        private CanvasStrokeStyle _roundCapStroke;

        public void Initialize(CanvasDevice device)
        {
            _device = device;

            _labelFormat = new CanvasTextFormat
            {
                FontSize = 16f,
                HorizontalAlignment = CanvasHorizontalAlignment.Center,
                VerticalAlignment = CanvasVerticalAlignment.Center,
                FontWeight = Windows.UI.Text.FontWeights.Bold
            };

            _scaleTextFormat = new CanvasTextFormat
            {
                FontSize = 10f,
                HorizontalAlignment = CanvasHorizontalAlignment.Center,
                VerticalAlignment = CanvasVerticalAlignment.Center
            };

            _roundCapStroke = new CanvasStrokeStyle
            {
                StartCap = CanvasCapStyle.Round,
                EndCap = CanvasCapStyle.Round
            };
        }

        public void Update(AudioData data, TimeSpan elapsed)
        {
            _time = data.Time;
            float dt = Math.Min((float)elapsed.TotalSeconds, 0.05f);

            float bass = 0, mid = 0, treble = 0;
            for (int i = 0; i < 6; i++) bass += data.BandLevels[i]; bass /= 6f;
            for (int i = 10; i < 16; i++) mid += data.BandLevels[i]; mid /= 6f;
            for (int i = 20; i < 26; i++) treble += data.BandLevels[i]; treble /= 6f;

            _smoothBass += (bass - _smoothBass) * AudioSmooth;
            _smoothMid += (mid - _smoothMid) * AudioSmooth;
            _smoothTreble += (treble - _smoothTreble) * AudioSmooth;
            _smoothBeat += (data.Beat - _smoothBeat) * 0.4f;

            SimulateNeedle(ref _needleBass, ref _velBass, Math.Min(1.1f, _smoothBass * InputGain), dt);
            SimulateNeedle(ref _needleMid, ref _velMid, Math.Min(1.1f, _smoothMid * InputGain), dt);
            SimulateNeedle(ref _needleTreble, ref _velTreble, Math.Min(1.1f, _smoothTreble * InputGain), dt);
        }

        private static void SimulateNeedle(ref float pos, ref float vel, float target, float dt)
        {
            float force = SpringK * (target - pos) - Damping * vel;
            vel += force * dt;
            pos += vel * dt;

            // Batentes f�sicos do VU meter
            if (pos < -0.05f) { pos = -0.05f; vel = Math.Max(0, vel * -0.2f); }
            if (pos > 1.08f) { pos = 1.08f; vel = Math.Min(0, vel * -0.2f); }
        }

        public void Draw(CanvasDrawingSession ds)
        {
            if (_device == null || _width == 0 || _height == 0) return;

            // Gabinete de madeira / chassi escuro
            ds.Clear(Color.FromArgb(255, 14, 11, 9));

            float meterW = _width * 0.29f;
            float meterH = meterW * 0.72f;
            float spacing = _width * 0.025f;
            float totalW = meterW * 3 + spacing * 2;
            float startX = (_width - totalW) * 0.5f;
            float meterY = (_height - meterH) * 0.48f;

            // Atualiza tamanho das fontes proporcionalmente
            _labelFormat.FontSize = meterW * 0.075f;
            _scaleTextFormat.FontSize = meterW * 0.045f;

            DrawOneMeter(ds, startX, meterY, meterW, meterH, _needleBass, "LOW / BASS");
            DrawOneMeter(ds, startX + meterW + spacing, meterY, meterW, meterH, _needleMid, "MID");
            DrawOneMeter(ds, startX + (meterW + spacing) * 2, meterY, meterW, meterH, _needleTreble, "HIGH / TREBLE");

            DrawIncandescentGlow(ds);
        }

        private void DrawOneMeter(CanvasDrawingSession ds, float x, float y, float w, float h, float needlePos, string label)
        {
            float cornerR = w * 0.04f;

            // 1. Moldura Externa (Bezel)
            ds.FillRoundedRectangle(x, y, w, h, cornerR, cornerR, Color.FromArgb(255, 28, 24, 20));
            ds.DrawRoundedRectangle(x, y, w, h, cornerR, cornerR, Color.FromArgb(255, 55, 48, 40), 2f);

            // 2. Dial Backplate (Fundo estilo Papel Vintage Vintage Cream/Amber)
            float inset = w * 0.05f;
            float faceX = x + inset, faceY = y + inset;
            float faceW = w - inset * 2, faceH = h - inset * 2;

            // Cor do mostrador com backlight simulado
            Color faceColor = Color.FromArgb(255, 245, 228, 170);
            ds.FillRoundedRectangle(faceX, faceY, faceW, faceH, cornerR, cornerR, faceColor);

            // 3. Geometria do Pivô da Agulha (Base Centralizada na parte inferior)
            float pivotX = x + w * 0.5f;
            float pivotY = y + h * 0.78f;
            float needleLen = h * 0.62f;

            // Varredura de ângulo realista: 0.0 = -126° | 1.0 = -54° (centro em -90° = straight up)
            float minAngle = -126f;
            float maxAngle = -54f;
            float currentAngle = minAngle + Math.Clamp(needlePos, 0f, 1.1f) * (maxAngle - minAngle);
            float rad = currentAngle * MathF.PI / 180f;

            float tipX = pivotX + MathF.Cos(rad) * needleLen;
            float tipY = pivotY + MathF.Sin(rad) * needleLen;

            // 4. Arco da Escala VU
            DrawScaleArc(ds, pivotX, pivotY, needleLen * 0.82f);

            // 5. Alerta Red Zone (Peak Flash no fundo)
            if (needlePos > 0.85f)
            {
                float factor = (needlePos - 0.85f) / 0.25f;
                byte alpha = (byte)Math.Clamp((int)(factor * 35 * (0.6f + _smoothBeat * 0.4f)), 0, 255);
                ds.FillRoundedRectangle(faceX, faceY, faceW, faceH, cornerR, cornerR, Color.FromArgb(alpha, 255, 30, 10));
            }

            // 6. Desenho da Agulha
            Color needleBase = Color.FromArgb(255, 25, 25, 25);
            ds.DrawLine(pivotX, pivotY, tipX, tipY, needleBase, 3f, _roundCapStroke);

            // Ponta vermelha (último terço da agulha)
            float redStartT = 0.6f;
            float redStartX = pivotX + (tipX - pivotX) * redStartT;
            float redStartY = pivotY + (tipY - pivotY) * redStartT;
            ds.DrawLine(redStartX, redStartY, tipX, tipY, Color.FromArgb(255, 220, 30, 15), 2f, _roundCapStroke);

            // 7. Capa do Pivô (Pino Preto Analógico)
            float pivotR = w * 0.045f;
            using (var geo = CanvasGeometry.CreateCircle(ds, pivotX, y + h * 0.78f, pivotR))
            {
                ds.FillGeometry(geo, Color.FromArgb(255, 35, 32, 30));
                ds.DrawGeometry(geo, Color.FromArgb(255, 70, 65, 60), 1f);
            }

            // 8. R�tulo da Banda
            float labelY = y + h * 0.60f;
            ds.DrawText(label, pivotX, labelY, Color.FromArgb(220, 60, 50, 40), _labelFormat);
        }

        private void DrawScaleArc(CanvasDrawingSession ds, float cx, float cy, float radius)
        {
            int tickCount = 18;
            float minAngle = -132f;
            float maxAngle = -48f;

            for (int i = 0; i <= tickCount; i++)
            {
                float t = (float)i / tickCount;
                float angleDeg = minAngle + t * (maxAngle - minAngle);
                float angleRad = angleDeg * MathF.PI / 180f;

                bool isRedZone = t > 0.72f;
                float innerR = radius * (i % 3 == 0 ? 0.86f : 0.90f);
                float outerR = radius;

                float x1 = cx + MathF.Cos(angleRad) * innerR;
                float y1 = cy + MathF.Sin(angleRad) * innerR;
                float x2 = cx + MathF.Cos(angleRad) * outerR;
                float y2 = cy + MathF.Sin(angleRad) * outerR;

                Color tickColor = isRedZone
                    ? Color.FromArgb(220, 210, 30, 20)
                    : Color.FromArgb(180, 40, 35, 30);

                float strokeW = (i % 3 == 0) ? 2.0f : 1.0f;
                ds.DrawLine(x1, y1, x2, y2, tickColor, strokeW, _roundCapStroke);
            }
        }

        private void DrawIncandescentGlow(CanvasDrawingSession ds)
        {
            // Ilumina��o quente por l�mpadas incandescente sobre o chassi
            float glow = 0.4f + _smoothBeat * 0.3f;
            byte a = (byte)Math.Clamp((int)(12 * glow), 0, 255);
            Color warmGlow = Color.FromArgb(a, 255, 180, 80);

            using (var geo = CanvasGeometry.CreateEllipse(ds, _width * 0.5f, _height * 0.5f, _width * 0.48f, _height * 0.42f))
            {
                ds.FillGeometry(geo, warmGlow);
            }
        }

        public void Resize(float width, float height) { _width = width; _height = height; }

        public void Dispose()
        {
            _labelFormat?.Dispose();
            _scaleTextFormat?.Dispose();
            _roundCapStroke?.Dispose();
            _device = null;
        }

        public void ConfigurePipeline(PostProcessPipeline pipeline)
        {
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