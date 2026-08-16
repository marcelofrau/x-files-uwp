using System;
using System.Buffers;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.UI;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Windows.Foundation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace XFiles.Visualizers
{
    public sealed class AudioVisualizerBase : UserControl
    {
        public static readonly CanvasStrokeStyle RoundCapStroke = new CanvasStrokeStyle
        {
            StartCap = CanvasCapStyle.Round,
            EndCap = CanvasCapStyle.Round
        };

        public static readonly CanvasStrokeStyle SquareCapStroke = new CanvasStrokeStyle
        {
            StartCap = CanvasCapStyle.Square,
            EndCap = CanvasCapStyle.Square
        };

        private readonly CanvasAnimatedControl _canvas;
        private readonly object _lock = new object();
        private IAudioVisualizer _visualizer;
        private Audio.AudioLevelService _service;
        private PostProcessPipeline _pipeline;
        private readonly Action<CanvasDrawingSession> _drawSceneAction;
        private float _elapsed;
        private bool _initialized;
        private float _cachedWidth;
        private float _cachedHeight;

        private float _bassLevel;
        private float _beatLevel;
        private int _gcLogCounter;
        private long _lastAllocBytes;

        private const long VizNoGcRegionSize = 128 * 1024 * 1024L;
        private bool _vizGcRegionActive;

        private readonly float[] _bandBuffer = new float[AudioData.BandCount];
        private readonly float[] _peakBuffer = new float[AudioData.BandCount];
        private readonly float[] _magBuffer = new float[AudioData.FftBinCount];
        private readonly float[] _waveBuffer = new float[Audio.AudioLevelService.FftSize];

        public AudioVisualizerBase()
        {
            _canvas = new CanvasAnimatedControl
            {
                ClearColor = Windows.UI.Colors.Black
            };
            _drawSceneAction = OnDrawScene;

            _canvas.Draw += OnCanvasDraw;
            _canvas.Update += OnCanvasUpdate;
            _canvas.SizeChanged += OnCanvasSizeChanged;

            Content = _canvas;
        }

        private void OnDrawScene(CanvasDrawingSession sceneDs)
        {
            IAudioVisualizer vis;
            lock (_lock)
            {
                vis = _visualizer;
            }
            if (vis != null)
            {
                vis.Draw(sceneDs);
            }
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
        /// Disposal is deferred to avoid race with in-flight draw calls.
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
            if (old != null)
            {
                Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    old.Dispose();
                });
            }
        }

        private void OnCanvasDraw(ICanvasAnimatedControl sender, CanvasAnimatedDrawEventArgs args)
        {
            IAudioVisualizer vis;
            lock (_lock)
            {
                vis = _visualizer;
            }

            // End NoGCRegion on render thread if visualizer was deactivated
            if (vis == null && _vizGcRegionActive)
            {
                _vizGcRegionActive = false;
                try { GC.EndNoGCRegion(); } catch { }
                Log.Verb("VIS[TID={Tid}]: NoGCRegion ended on render thread", Environment.CurrentManagedThreadId);
            }
            if (vis == null) return;

            try
            {
                if (!_initialized)
                {
                    vis.Initialize(args.DrawingSession.Device);

                    _pipeline?.Dispose();

                    _pipeline = new PostProcessPipeline();
                    _pipeline.Initialize(args.DrawingSession.Device);

                    if (_cachedWidth > 0 && _cachedHeight > 0)
                    {
                        vis.Resize(_cachedWidth, _cachedHeight);
                        _pipeline.Resize(_cachedWidth, _cachedHeight);
                    }

                    // Start per-thread NoGCRegion for render thread (.NET Core 2.x)
                    if (!_vizGcRegionActive)
                    {
                        int tid = Environment.CurrentManagedThreadId;
                        try
                        {
                            if (GC.TryStartNoGCRegion(VizNoGcRegionSize))
                            {
                                _vizGcRegionActive = true;
                                Log.Verb("VIS[TID={Tid}]: NoGCRegion started {Size}MB", tid, VizNoGcRegionSize / (1024 * 1024));
                            }
                            else
                                Log.Warn("VIS[TID={Tid}]: NoGCRegion TryStart returned false", tid);
                        }
                        catch (Exception ex)
                        {
                            Log.Warn("VIS[TID={Tid}]: NoGCRegion TryStart threw {Ex}", tid, ex.GetType().Name);
                        }
                    }

                    _initialized = true;
                }

                _gcLogCounter++;
                if (_gcLogCounter % 60 == 0)
                {
                    long now = GC.GetAllocatedBytesForCurrentThread();
                    long delta = now - _lastAllocBytes;
                    _lastAllocBytes = now;
                    int tid = Environment.CurrentManagedThreadId;
                    Log.Verb("VIS-ALLOC[TID={Tid}]: allocRate={Rate}KB/s perFrame={Frame}B totalThread={Thread}KB heap={Heap}KB",
                        tid, delta / 1024, delta / 60, now / 1024, GC.GetTotalMemory(false) / 1024);
                    _gcLogCounter = 0;
                }

                var ds = args.DrawingSession;

                if (_pipeline != null)
                {
                    vis.ConfigurePipeline(_pipeline);
                    _pipeline.Draw(ds, _drawSceneAction, _bassLevel, _beatLevel);
                }
                else
                {
                    vis.Draw(ds);
                }
            }
            catch (Exception ex)
            {
                if (_vizGcRegionActive)
                {
                    _vizGcRegionActive = false;
                    int tid = Environment.CurrentManagedThreadId;
                    try { GC.EndNoGCRegion(); } catch { }
                    Log.Warn("VIS: NoGCRegion ended after draw error TID={Tid}", tid);
                }
                Log.Err("AudioVisualizerBase.OnCanvasDraw", ex);
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

            // End NoGCRegion on render thread if deactivated
            if (vis == null && _vizGcRegionActive)
            {
                _vizGcRegionActive = false;
                try { GC.EndNoGCRegion(); } catch { }
                Log.Verb("VIS[TID={Tid}]: NoGCRegion ended on render thread (update)",
                    Environment.CurrentManagedThreadId);
            }
            if (vis == null || !_initialized) return;

            try
            {
                if (_service != null && _service.IsAnalyzing)
                {
                    var data = AudioData.FromService(_service, _elapsed, _bandBuffer, _peakBuffer, _magBuffer, _waveBuffer);
                    vis.Update(data, args.Timing.ElapsedTime);

                    // Cache bass/beat for pipeline
                    float bass = 0;
                    for (int i = 0; i < 6; i++) bass += data.BandLevels[i];
                    _bassLevel = Math.Min(1f, bass / 6f);
                    _beatLevel = data.Beat;
                }
            }
            catch (Exception ex)
            {
                Log.Err("AudioVisualizerBase.OnCanvasUpdate", ex);
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
