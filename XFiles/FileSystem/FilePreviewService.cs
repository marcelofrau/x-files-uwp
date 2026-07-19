using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Windows.ApplicationModel.Core;
using Windows.Storage.Streams;
using Windows.UI.Core;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using XFiles.FileSystem;

namespace XFiles.FileSystem
{
    public enum FilePreviewType
    {
        None,
        Text,
        Image,
        Unsupported,
        Error
    }

    public class FilePreviewResult
    {
        public FilePreviewType Type { get; set; }
        public string TextContent { get; set; }
        public ImageSource ImageSource { get; set; }
        public string ErrorMessage { get; set; }
        public string FileType { get; set; }
        public long FileSizeBytes { get; set; }
        public bool IsTruncated { get; set; }
        public int PixelWidth { get; set; }
        public int PixelHeight { get; set; }
    }

    public static class FilePreviewService
    {
        private const long MaxTextBytes = 256 * 1024; // 256 KB

        #region P/Invoke

        private const uint GENERIC_READ = 0x80000000;
        private const uint OPEN_EXISTING = 3;
        private const uint FILE_ATTRIBUTE_NORMAL = 0x80;
        private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

        [DllImport("api-ms-win-core-file-fromapp-l1-1-0.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateFileFromAppW(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadFile(
            IntPtr hFile,
            byte[] lpBuffer,
            uint nNumberOfBytesToRead,
            out uint lpNumberOfBytesRead,
            IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetFileSizeEx(IntPtr hFile, out long lpFileSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        #endregion

        private static readonly HashSet<string> TextExtensions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                // Plain text
                ".txt", ".log", ".md", ".markdown", ".rst", ".csv", ".tsv",
                // Config / data
                ".ini", ".cfg", ".conf", ".config", ".toml", ".yaml", ".yml",
                ".json", ".jsonc", ".json5", ".jsonl", ".xml", ".plist",
                ".env", ".properties", ".props", ".targets",
                // Web
                ".html", ".htm", ".xhtml", ".css", ".scss", ".less", ".sass",
                ".js", ".mjs", ".cjs", ".ts", ".jsx", ".tsx",
                ".vue", ".svelte", ".astro",
                // C/C++
                ".c", ".cpp", ".cc", ".cxx", ".h", ".hpp", ".hxx", ".hh",
                ".inl", ".inc",
                // C# / .NET
                ".cs", ".vb", ".fs", ".fsx", ".fsi", ".csx",
                ".csproj", ".vbproj", ".fsproj", ".sln",
                ".xaml", ".axaml", ".resx", ".resw",
                ".storyboard", ".strings",
                // Java / JVM
                ".java", ".kt", ".kts", ".groovy", ".gradle", ".gradle.kts",
                // Python
                ".py", ".pyw", ".pyi", ".pyx",
                // Ruby
                ".rb", ".erb", ".rake",
                // Go
                ".go",
                // Rust
                ".rs",
                // Shell / scripting
                ".sh", ".bash", ".zsh", ".fish", ".ksh",
                ".bat", ".cmd", ".ps1", ".psm1", ".psd1",
                // Other languages
                ".lua", ".pl", ".pm", ".swift", ".dart", ".r", ".R",
                ".sql", ".graphql", ".gql", ".proto",
                // Build / infra
                ".dockerfile", ".dockerignore", ".makefile", ".cmake",
                ".mk", ".mak",
                ".rc", ".rc2",
                ".gitignore", ".gitattributes", ".gitmodules",
                ".editorconfig", ".prettierrc", ".eslintrc",
                ".babelrc", ".stylelintrc",
                // Misc
                ".webp",
                ".out", ".err",
                ".inf", ".dif",
                ".wxs", ".wxi", ".wixproj",
                ".nuspec", ".nuget",
                ".feed", ".opml",
                ".pod", ".srt", ".vtt", ".sub",
                ".lrc", ".ly",
                ".bib", ".cls", ".sty", ".tex", ".latex",
            };

        private static readonly HashSet<string> ImageExtensions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".png", ".jpg", ".jpeg", ".gif", ".bmp",
                ".tiff", ".tif", ".webp", ".ico"
            };

        public static bool IsTextFile(string extension)
        {
            return !string.IsNullOrEmpty(extension) && TextExtensions.Contains(extension);
        }

        public static bool IsImageFile(string extension)
        {
            return !string.IsNullOrEmpty(extension) && ImageExtensions.Contains(extension);
        }

        public static bool IsSvgFile(string extension)
        {
            return string.Equals(extension, ".svg", StringComparison.OrdinalIgnoreCase);
        }

        public static async Task<FilePreviewResult> GetPreviewAsync(string filePath)
        {
            var result = new FilePreviewResult { FileType = "" };

            try
            {
                if (string.IsNullOrEmpty(filePath))
                {
                    result.Type = FilePreviewType.Error;
                    result.ErrorMessage = "No file path specified";
                    return result;
                }

                string ext = Path.GetExtension(filePath);
                result.FileType = GetFileTypeLabel(ext);

                // Read file size via Win32 to verify file exists and get size.
                long fileSize = 0;
                if (!GetFileSizeWin32(filePath, out fileSize))
                {
                    result.Type = FilePreviewType.Error;
                    result.ErrorMessage = "File not found or inaccessible";
                    return result;
                }

                result.FileSizeBytes = fileSize;

                if (IsImageFile(ext))
                {
                    await LoadImagePreview(filePath, result);
                }
                else if (IsSvgFile(ext))
                {
                    await LoadSvgPreview(filePath, result);
                }
                else if (IsTextFile(ext))
                {
                    await LoadTextPreview(filePath, result);
                }
                else
                {
                    result.Type = FilePreviewType.Unsupported;
                }
            }
            catch (Exception ex)
            {
                result.Type = FilePreviewType.Error;
                result.ErrorMessage = $"Cannot load preview: {ex.Message}";
                Log.Warning("FilePreviewService: error previewing '{Path}': {Error}", filePath, ex.Message);
            }

            return result;
        }

        public static async Task<FilePreviewResult> GetPreviewFromArchiveAsync(
            ArchiveBrowser archiveBrowser, string archivePath, string internalPath)
        {
            var result = new FilePreviewResult { FileType = "" };

            try
            {
                string ext = Path.GetExtension(internalPath);
                result.FileType = GetFileTypeLabel(ext);
                result.FileSizeBytes = 0;

                using (var stream = archiveBrowser.OpenEntryStream(archivePath, internalPath))
                {
                    if (stream == null)
                    {
                        result.Type = FilePreviewType.Error;
                        result.ErrorMessage = "Failed to open entry in archive";
                        return result;
                    }

                    if (IsImageFile(ext))
                    {
                        await LoadImagePreviewFromStream(stream, result);
                    }
                    else if (IsSvgFile(ext))
                    {
                        await LoadSvgPreviewFromStream(stream, result);
                    }
                    else if (IsTextFile(ext))
                    {
                        await LoadTextPreviewFromStream(stream, result);
                    }
                    else
                    {
                        result.Type = FilePreviewType.Unsupported;
                    }
                }
            }
            catch (Exception ex)
            {
                result.Type = FilePreviewType.Error;
                result.ErrorMessage = $"Cannot load preview: {ex.Message}";
                Log.Warning("FilePreviewService: error previewing archive entry '{Archive}|{Internal}': {Error}",
                    archivePath, internalPath, ex.Message);
            }

            return result;
        }

        private static async Task LoadTextPreviewFromStream(Stream stream, FilePreviewResult result)
        {
            result.Type = FilePreviewType.Text;

            byte[] buffer = new byte[MaxTextBytes];
            int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);

            result.IsTruncated = bytesRead == MaxTextBytes;

            if (bytesRead < buffer.Length)
            {
                byte[] trimmed = new byte[bytesRead];
                Array.Copy(buffer, trimmed, bytesRead);
                buffer = trimmed;
            }

            result.TextContent = Encoding.UTF8.GetString(buffer);
        }

        private static async Task LoadImagePreviewFromStream(Stream stream, FilePreviewResult result)
        {
            result.Type = FilePreviewType.Image;

            byte[] imageBytes;
            using (var ms = new MemoryStream())
            {
                await stream.CopyToAsync(ms);
                imageBytes = ms.ToArray();
            }

            result.FileSizeBytes = imageBytes.Length;

            var dispatcher = CoreApplication.MainView.CoreWindow?.Dispatcher;
            if (dispatcher == null)
            {
                result.Type = FilePreviewType.Error;
                result.ErrorMessage = "Cannot access UI dispatcher for image preview";
                return;
            }

            var tcs = new TaskCompletionSource<bool>();

            await dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () =>
            {
                try
                {
                    var bitmap = new BitmapImage();
                    using (var memStream = new InMemoryRandomAccessStream())
                    {
                        using (var writer = new DataWriter(memStream.GetOutputStreamAt(0)))
                        {
                            writer.WriteBytes(imageBytes);
                            await writer.StoreAsync();
                            await writer.FlushAsync();
                        }
                        memStream.Seek(0);
                        await bitmap.SetSourceAsync(memStream);
                    }
                    result.ImageSource = bitmap;
                    result.PixelWidth = bitmap.PixelWidth;
                    result.PixelHeight = bitmap.PixelHeight;
                    tcs.SetResult(true);
                }
                catch (Exception ex)
                {
                    result.Type = FilePreviewType.Error;
                    result.ErrorMessage = $"Cannot load image: {ex.Message}";
                    tcs.SetResult(false);
                }
            });

            await tcs.Task;
        }

        private static async Task LoadSvgPreviewFromStream(Stream stream, FilePreviewResult result)
        {
            result.Type = FilePreviewType.Text;

            using (var sr = new StreamReader(stream, Encoding.UTF8))
            {
                result.TextContent = await sr.ReadToEndAsync();
            }

            result.IsTruncated = false;
        }

        private static bool GetFileSizeWin32(string filePath, out long size)
        {
            size = 0;
            IntPtr hFile = CreateFileFromAppW(filePath, GENERIC_READ, 0, IntPtr.Zero,
                OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, IntPtr.Zero);

            if (hFile == INVALID_HANDLE_VALUE)
                return false;

            try
            {
                return GetFileSizeEx(hFile, out size);
            }
            finally
            {
                CloseHandle(hFile);
            }
        }

        private static async Task LoadTextPreview(string filePath, FilePreviewResult result)
        {
            result.Type = FilePreviewType.Text;

            byte[] fileBytes = await Task.Run(() => ReadFileWin32(filePath, MaxTextBytes));

            if (fileBytes == null)
            {
                result.Type = FilePreviewType.Error;
                result.ErrorMessage = "Failed to read file";
                return;
            }

            long totalSize = result.FileSizeBytes;
            result.IsTruncated = totalSize > MaxTextBytes;

            result.TextContent = Encoding.UTF8.GetString(fileBytes);

            if (result.IsTruncated)
            {
                result.TextContent += $"\n\n... [truncated \u2014 showing {FormatSize(MaxTextBytes)} of {FormatSize(totalSize)}]";
            }
        }

        private static async Task LoadImagePreview(string filePath, FilePreviewResult result)
        {
            result.Type = FilePreviewType.Image;

            byte[] imageBytes = await Task.Run(() => ReadFileWin32(filePath, 0));

            if (imageBytes == null)
            {
                result.Type = FilePreviewType.Error;
                result.ErrorMessage = "Failed to read image file";
                return;
            }

            var dispatcher = CoreApplication.MainView.CoreWindow?.Dispatcher;
            if (dispatcher == null)
            {
                result.Type = FilePreviewType.Error;
                result.ErrorMessage = "Cannot access UI dispatcher for image preview";
                return;
            }

            // BitmapImage.SetSourceAsync must run on UI thread.
            // dispatcher.RunAsync is fire-and-forget from background threads,
            // so we use a TaskCompletionSource to wait for it properly.
            var tcs = new TaskCompletionSource<bool>();

            await dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () =>
            {
                try
                {
                    var bitmap = new BitmapImage();
                    using (var memStream = new InMemoryRandomAccessStream())
                    {
                        using (var writer = new DataWriter(memStream.GetOutputStreamAt(0)))
                        {
                            writer.WriteBytes(imageBytes);
                            await writer.StoreAsync();
                            await writer.FlushAsync();
                        }
                        memStream.Seek(0);
                        await bitmap.SetSourceAsync(memStream);
                    }
                    result.ImageSource = bitmap;
                    result.PixelWidth = bitmap.PixelWidth;
                    result.PixelHeight = bitmap.PixelHeight;
                    tcs.SetResult(true);
                }
                catch (Exception ex)
                {
                    result.Type = FilePreviewType.Error;
                    result.ErrorMessage = $"Cannot load image: {ex.Message}";
                    tcs.SetResult(false);
                }
            });

            await tcs.Task;
        }

        private static async Task LoadSvgPreview(string filePath, FilePreviewResult result)
        {
            result.Type = FilePreviewType.Text;

            byte[] svgBytes = await Task.Run(() => ReadFileWin32(filePath, 0));
            if (svgBytes == null)
            {
                result.Type = FilePreviewType.Error;
                result.ErrorMessage = "Failed to read SVG file";
                return;
            }

            result.TextContent = System.Text.Encoding.UTF8.GetString(svgBytes);
            result.IsTruncated = false;
        }

        /// <summary>
        /// Read file via Win32 CreateFileFromAppW + ReadFile.
        /// If maxBytes is 0, reads entire file.
        /// </summary>
        private static byte[] ReadFileWin32(string filePath, long maxBytes)
        {
            IntPtr hFile = CreateFileFromAppW(filePath, GENERIC_READ, 0, IntPtr.Zero,
                OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, IntPtr.Zero);

            if (hFile == INVALID_HANDLE_VALUE)
            {
                int err = Marshal.GetLastWin32Error();
                Log.Warning("FilePreviewService: CreateFileFromAppW failed for '{Path}' (error {Error})", filePath, err);
                return null;
            }

            try
            {
                long fileSize;
                if (!GetFileSizeEx(hFile, out fileSize))
                {
                    Log.Warning("FilePreviewService: GetFileSizeEx failed for '{Path}'", filePath);
                    return null;
                }

                long bytesToRead = (maxBytes > 0 && fileSize > maxBytes) ? maxBytes : fileSize;
                if (bytesToRead <= 0) return new byte[0];

                byte[] buffer = new byte[bytesToRead];
                uint totalRead = 0;

                while (totalRead < bytesToRead)
                {
                    uint bytesRead;
                    uint chunk = (uint)Math.Min(bytesToRead - totalRead, int.MaxValue);

                    if (!ReadFile(hFile, buffer, chunk, out bytesRead, IntPtr.Zero) || bytesRead == 0)
                        break;

                    totalRead += bytesRead;
                }

                if (totalRead < buffer.Length)
                {
                    byte[] trimmed = new byte[totalRead];
                    Array.Copy(buffer, trimmed, totalRead);
                    return trimmed;
                }

                return buffer;
            }
            finally
            {
                CloseHandle(hFile);
            }
        }

            private static readonly Dictionary<string, string> FileTypeLabels =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "txt", "Text" },
                { "md", "Markdown" }, { "markdown", "Markdown" },
                { "json", "JSON" }, { "jsonc", "JSON" }, { "json5", "JSON" },
                { "xml", "XML" },
                { "csv", "CSV" },
                { "html", "HTML" }, { "htm", "HTML" }, { "xhtml", "HTML" },
                { "css", "CSS" },
                { "js", "JavaScript" }, { "mjs", "JavaScript" }, { "cjs", "JavaScript" },
                { "ts", "TypeScript" }, { "tsx", "TypeScript" }, { "jsx", "TypeScript" },
                { "cs", "C#" },
                { "vb", "VB.NET" },
                { "fs", "F#" }, { "fsx", "F#" }, { "fsi", "F#" },
                { "py", "Python" }, { "pyw", "Python" },
                { "rb", "Ruby" },
                { "java", "Java" },
                { "kt", "Kotlin" }, { "kts", "Kotlin" },
                { "go", "Go" },
                { "rs", "Rust" },
                { "c", "C" }, { "h", "C" },
                { "cpp", "C++" }, { "cc", "C++" }, { "cxx", "C++" }, { "hpp", "C++" },
                { "sh", "Shell" }, { "bash", "Shell" },
                { "ps1", "PowerShell" },
                { "sql", "SQL" },
                { "lua", "Lua" },
                { "pl", "Perl" }, { "pm", "Perl" },
                { "swift", "Swift" },
                { "dart", "Dart" },
                { "r", "R" },
                { "yaml", "YAML" }, { "yml", "YAML" },
                { "toml", "TOML" },
                { "ini", "Config" }, { "cfg", "Config" }, { "conf", "Config" },
                { "log", "Log" },
                { "sln", "Solution" },
                { "csproj", "Project" }, { "vbproj", "Project" }, { "fsproj", "Project" },
                { "xaml", "XAML" }, { "axaml", "XAML" },
                { "dockerfile", "Dockerfile" },
                { "gitignore", "Git" }, { "gitattributes", "Git" },
                { "tex", "LaTeX" }, { "latex", "LaTeX" }, { "bib", "LaTeX" },
                { "srt", "Subtitles" }, { "vtt", "Subtitles" }, { "sub", "Subtitles" },
                { "svg", "SVG" },
                { "zip", "ZIP Archive" }, { "7z", "7-Zip Archive" }, { "rar", "RAR Archive" },
                { "tar", "Tar Archive" }, { "gz", "Gzip Archive" }, { "bz2", "Bzip2 Archive" },
            };

        private static string GetFileTypeLabel(string extension)
        {
            if (string.IsNullOrEmpty(extension))
                return "Unknown";

            string key = extension.TrimStart('.').ToLowerInvariant();
            string label;
            if (FileTypeLabels.TryGetValue(key, out label))
                return label;

            return extension.TrimStart('.').ToUpperInvariant();
        }

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
            return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        }
    }
}
