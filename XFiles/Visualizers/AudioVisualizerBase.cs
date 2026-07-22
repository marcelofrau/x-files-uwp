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
    /// Thread safety: Activate/Deactivate may be called from UI thread while
    /// Draw/Update fire on the composition thread. A lock protects _visualizer access.
    /// Post-processing: wraps visualizer output with MilkDrop-style effects
    /// (feedback trails, bloom/glare, vignette depth).
    /// </summary>
    public sealed class AudioVisualizerBase : UserControl
    {
        private readonly CanvasAnimatedControl _canvas;
        private readonly object _lock = new object();
        private IAudioVisualizer _visualizer;
        private Audio.AudioLevelService _service;
        private PostProcessPipeline _pipeline;
        private float _elapsed;
        private bool _initialized;
        private float _cachedWidth;
        private float _cachedHeight;

        // Cached audio data for pipeline (read from Update, used in Draw)
        private float _bassLevel;
        private float _beatLevel;

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
            lock (_lock)
            {
                _visualizer = visualizer;
                _initialized = false;
            }
        }

        /// <summary>
        /// Deactivate and dispose the current visualizer.
        /// </summary>
        public void Deactivate()
        {
            IAudioVisualizer old = null;
            lock (_lock)
            {
                old = _visualizer;
                _visualizer = null;
                _initialized = false;
            }
            old?.Dispose();
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
            IAudioVisualizer vis;
            lock (_lock)
            {
                vis = _visualizer;
            }
            if (vis == null) return;

            if (!_initialized)
            {
                vis.Initialize(args.DrawingSession.Device);

                // Initialize pipeline
                _pipeline = new PostProcessPipeline();
                _pipeline.Initialize(args.DrawingSession.Device);

                if (_cachedWidth > 0 && _cachedHeight > 0)
                {
                    vis.Resize(_cachedWidth, _cachedHeight);
                    _pipeline.Resize(_cachedWidth, _cachedHeight);
                }
                _initialized = true;
            }

            var ds = args.DrawingSession;

            // Use post-processing pipeline for MilkDrop-style effects
            if (_pipeline != null)
            {
                _pipeline.Draw(ds, (sceneDs) => vis.Draw(sceneDs), _bassLevel, _beatLevel);
            }
            else
            {
                vis.Draw(ds);
            }
        }

        private void OnCanvasUpdate(ICanvasAnimatedControl sender, CanvasAnimatedUpdateEventArgs args)
        {
            _elapsed += (float)args.Timing.ElapsedTime.TotalSeconds;

            IAudioVisualizer vis;
            lock (_lock)
            {
                vis = _visualizer;
            }
            if (vis == null || !_initialized) return;

            if (_service != null && _service.IsAnalyzing)
            {
                var data = AudioData.FromService(_service, _elapsed);
                vis.Update(data, args.Timing.ElapsedTime);

                // Cache bass/beat for pipeline
                float bass = 0;
                for (int i = 0; i < 6; i++) bass += data.BandLevels[i];
                _bassLevel = Math.Min(1f, bass / 6f);
                _beatLevel = data.Beat;
            }
        }

        private void OnCanvasSizeChanged(object sender, SizeChangedEventArgs e)
        {
            _cachedWidth = (float)e.NewSize.Width;
            _cachedHeight = (float)e.NewSize.Height;
            IAudioVisualizer vis;
            lock (_lock)
            {
                vis = _visualizer;
            }
            if (vis != null && _initialized)
            {
                vis.Resize(_cachedWidth, _cachedHeight);
            }
            if (_pipeline != null && _initialized)
            {
                _pipeline.Resize(_cachedWidth, _cachedHeight);
            }
        }
    }
}
