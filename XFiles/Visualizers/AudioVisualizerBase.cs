using System;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Windows.Foundation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace XFiles.Visualizers
{
    /// <summary>
    /// Hosts a CanvasAnimatedControl and drives the IAudioVisualizer lifecycle.
    /// Uses composition (not inheritance) because CanvasAnimatedControl is sealed.
    /// </summary>
    public sealed class AudioVisualizerBase : UserControl
    {
        private readonly CanvasAnimatedControl _canvas;
        private IAudioVisualizer _visualizer;
        private Audio.AudioLevelService _service;
        private float _elapsed;
        private bool _initialized;
        private float _cachedWidth;
        private float _cachedHeight;

        public AudioVisualizerBase()
        {
            _canvas = new CanvasAnimatedControl
            {
                ClearColor = Windows.UI.Colors.Black
            };

            _canvas.Draw += OnCanvasDraw;
            _canvas.Update += OnCanvasUpdate;
            _canvas.SizeChanged += OnCanvasSizeChanged;

            Content = _canvas;
        }

        /// <summary>
        /// Attach an audio service to feed data to the visualizer.
        /// </summary>
        public void AttachService(Audio.AudioLevelService service)
        {
            _service = service;
        }

        /// <summary>
        /// Detach the audio service (e.g. on stop or track change).
        /// </summary>
        public void DetachService()
        {
            _service = null;
        }

        /// <summary>
        /// Activate this visualizer with the given implementation.
        /// Actual device init is deferred to the first Draw call.
        /// </summary>
        public void Activate(IAudioVisualizer visualizer)
        {
            Deactivate();
            _visualizer = visualizer;
            _initialized = false;
        }

        /// <summary>
        /// Deactivate and dispose the current visualizer.
        /// </summary>
        public void Deactivate()
        {
            if (_visualizer != null)
            {
                _visualizer.Dispose();
                _visualizer = null;
                _initialized = false;
            }
        }

        /// <summary>
        /// Invalidate the canvas to trigger a redraw.
        /// </summary>
        public void Invalidate()
        {
            _canvas?.Invalidate();
        }

        private void OnCanvasDraw(ICanvasAnimatedControl sender, CanvasAnimatedDrawEventArgs args)
        {
            if (_visualizer == null) return;

            if (!_initialized)
            {
                _visualizer.Initialize(args.DrawingSession.Device);
                if (_cachedWidth > 0 && _cachedHeight > 0)
                {
                    _visualizer.Resize(_cachedWidth, _cachedHeight);
                }
                _initialized = true;
            }

            var ds = args.DrawingSession;
            var image = _visualizer.GetImage();
            if (image != null)
            {
                ds.DrawImage(image);
            }
        }

        private void OnCanvasUpdate(ICanvasAnimatedControl sender, CanvasAnimatedUpdateEventArgs args)
        {
            _elapsed += (float)args.Timing.ElapsedTime.TotalSeconds;

            if (_visualizer == null || !_initialized) return;

            if (_service != null && _service.IsAnalyzing)
            {
                var data = AudioData.FromService(_service, _elapsed);
                _visualizer.Update(data, args.Timing.ElapsedTime);
            }
        }

        private void OnCanvasSizeChanged(object sender, SizeChangedEventArgs e)
        {
            _cachedWidth = (float)e.NewSize.Width;
            _cachedHeight = (float)e.NewSize.Height;
            if (_visualizer != null && _initialized)
            {
                _visualizer.Resize(_cachedWidth, _cachedHeight);
            }
        }
    }
}
