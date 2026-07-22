namespace XFiles.Visualizers
{
    /// <summary>
    /// Display modes for the fullscreen audio overlay.
    /// Select button cycles through these in order.
    /// </summary>
    public enum AudioFullscreenMode
    {
        /// <summary>Album art + VU meter + metadata (default).</summary>
        Default,

        /// <summary>Radial spectrum: 26 frequency bars in a circle.</summary>
        RadialSpectrum,

        /// <summary>Waveform: time-domain oscilloscope with symmetry.</summary>
        Waveform,

        /// <summary>Plasma: color waves reactive to audio.</summary>
        Plasma
    }
}
