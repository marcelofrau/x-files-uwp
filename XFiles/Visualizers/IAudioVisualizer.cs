using System;
using Microsoft.Graphics.Canvas;

namespace XFiles.Visualizers
{
    /// <summary>
    /// Lifecycle interface for audio visualizers.
    /// Each visualizer owns its own Win2D effects and shader state.
    /// Uses draw-direct pattern (void Draw) to avoid cross-thread disposal races
    /// with CanvasRenderTarget used as effect sources.
    /// </summary>
    public interface IAudioVisualizer : IDisposable
    {
        /// <summary>Display name shown in OSD (e.g. "Radial Spectrum").</summary>
        string Name { get; }

        /// <summary>Unique identifier (e.g. "radial-spectrum").</summary>
        string Id { get; }

        /// <summary>Called once when the visualizer is activated. Create device resources here.</summary>
        void Initialize(CanvasDevice device);

        /// <summary>Called each frame with fresh audio data. Update shader parameters here.</summary>
        void Update(AudioData data, TimeSpan elapsed);

        /// <summary>Draw the visualizer directly to the provided drawing session.
        /// All effects (blur, blend) must be created and drawn within this call.
        /// Do NOT return ICanvasImage — the caller's session owns the GPU context.</summary>
        void Draw(CanvasDrawingSession ds);

        /// <summary>Called when the canvas is resized. Update resolution-dependent parameters.</summary>
        void Resize(float width, float height);

        /// <summary>Configure post-processing pipeline before each draw.
        /// Override to enable/customize effects (slide, rotation, bloom, scanlines, etc.).
        /// Default implementation does nothing (pipeline uses its own defaults).</summary>
        void ConfigurePipeline(PostProcessPipeline pipeline);
    }
}
