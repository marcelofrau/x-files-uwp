using System;
using Microsoft.Graphics.Canvas;

namespace XFiles.Visualizers
{
    /// <summary>
    /// Lifecycle interface for audio visualizers.
    /// Each visualizer owns its own Win2D effects and shader state.
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

        /// <summary>Return the current Win2D image to render. Use ICanvasImage for flexibility.</summary>
        ICanvasImage GetImage();

        /// <summary>Called when the canvas is resized. Update resolution-dependent parameters.</summary>
        void Resize(float width, float height);
    }
}
