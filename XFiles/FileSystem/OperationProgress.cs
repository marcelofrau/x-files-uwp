namespace XFiles.FileSystem
{
    /// <summary>
    /// Progress payload for file copy/move/delete operations.
    /// Extracted as a standalone partial so the test project can link it
    /// without pulling the full FileOperations.cs (SharpCompress + P/Invoke deps).
    /// </summary>
    public static partial class FileOperations
    {
        public class OperationProgress
        {
            public string FileName { get; set; }
            public double PercentComplete { get; set; }
            public long BytesCopied { get; set; }
            public long TotalBytes { get; set; }
            public int FileIndex { get; set; }
            public int FileTotal { get; set; }
        }
    }
}
