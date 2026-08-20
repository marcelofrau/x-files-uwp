namespace XFiles.FileSystem
{
    /// <summary>
    /// User decision when a copy/move hits an existing destination file.
    /// Mirrors Windows Explorer: replace, keep both (rename), resume partial, or abort.
    /// </summary>
    public enum ConflictDecision
    {
        /// <summary>Overwrite the existing destination file.</summary>
        ReplaceAll,

        /// <summary>Keep both files — auto-rename the incoming one ("name (N)").</summary>
        RenameAll,

        /// <summary>Resume a partially-copied file from where it left off.</summary>
        Resume,

        /// <summary>Abort the whole operation.</summary>
        Cancel
    }
}
