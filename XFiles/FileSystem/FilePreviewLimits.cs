namespace XFiles.FileSystem
{
    /// <summary>
    /// Size limits that gate inline previews. Pure (no UWP types) so it links into the
    /// desktop test project for unit testing.
    /// </summary>
    public static class FilePreviewLimits
    {
        /// <summary>Inline image/SVG preview size cap (~50 MB, mirrors the PDF cap).</summary>
        public const long MaxImageBytes = 50 * 1024 * 1024;

        /// <summary>
        /// Threshold above which a remote archive's contents are NOT auto-listed in the
        /// hover preview. Hover-listing a large zip over a network stream opens the whole
        /// file (and, for FTP, reopens it per seek), freezing the shared session. Zips at
        /// or below this size keep the auto-list preview.
        /// </summary>
        public const long MaxNetworkArchivePreviewBytes = 128 * 1024 * 1024; // 128 MB

        public static bool ShouldDeferNetworkArchivePreview(long sizeBytes) =>
            sizeBytes > MaxNetworkArchivePreviewBytes;
    }
}
