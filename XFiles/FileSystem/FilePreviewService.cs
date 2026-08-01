using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Windows.ApplicationModel.Core;
using Windows.Graphics.Imaging;
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
        Pdf,
        Audio,
        Video,
        Rom,
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
        public int PdfPageCount { get; set; }
        public string RomSystem { get; set; }
        public string RomIconPath { get; set; }

        // Gamelist data (populated when gamelist.xml entry matches)
        public bool HasGamelistData { get; set; }
        public string RomDescription { get; set; }
        public string RomDeveloper { get; set; }
        public string RomPublisher { get; set; }
        public string RomGenre { get; set; }
        public int RomPlayers { get; set; }
        public float RomRating { get; set; }
        public int RomReleaseYear { get; set; }
        public string RomCoverLocalPath { get; set; }
    }

    public static class FilePreviewService
    {
        private const long MaxTextBytes = 256 * 1024; // 256 KB

        private static readonly Dictionary<string, string> RomSystemIcons =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "NES", "retro-nes-128.png" },
                { "SNES", "retro-snes-128.png" },
                { "Game Boy", "retro-game-boy-128.png" },
                { "Game Boy Color", "retro-game-boy-color-128.png" },
                { "GBA", "retro-game-boy-advance-128.png" },
                { "Genesis/Mega Drive", "retro-sega-genesis-128.png" },
                { "Master System", "retro-master-system-128.png" },
                { "Game Gear", "retro-game-gear-128.png" },
                { "PC Engine/TurboGrafx-16", "retro-pc-engine-128.png" },
                { "Atari Jaguar", "retro-gamecube-128.png" },
                { "Atari Lynx", "retro-gamecube-128.png" },
                { "ZX Spectrum", "retro-nintendo-ds-128.png" },
                { "Vectrex", "retro-virtual-boy-128.png" },
                { "Nintendo 64", "retro-nintendo-64-128.png" },
                { "Nintendo DS", "retro-nintendo-ds-128.png" },
                { "Nintendo 3DS", "retro-nintendo-ds-128.png" },
                { "Virtual Boy", "retro-virtual-boy-128.png" },
                { "Neo Geo Pocket", "retro-neogeo-128.png" },
                { "Neo Geo", "retro-neogeo-128.png" },
                { "Dreamcast", "retro-dreamcast-128.png" },
                { "GameCube", "retro-gamecube-128.png" },
                { "Saturn", "retro-sega-saturn-128.png" },
                { "PlayStation", RomDefaultIcon },
                { "WonderSwan", "retro-gamecube-128.png" },
                { "Sega 32X", "retro-sega-genesis-128.png" },
                { "Wii", "retro-gamecube-128.png" },
                { "Wii U", "retro-gamecube-128.png" },
                { "Switch", "retro-gamecube-128.png" },
                { "PSP", RomDefaultIcon },
                { "Sega CD", "retro-sega-genesis-128.png" },
            };

        private const string RomDefaultIcon = "icons8-game-controller-128.png";
        private const string RomIconBasePath = "ms-appx:///Assets/Views/MillerColumnsPage/rom/";

        public static string GetRomIconPathPublic(string system) => GetRomIconPath(system);

        private static string GetRomIconPath(string system)
        {
            if (!string.IsNullOrEmpty(system) &&
                RomSystemIcons.TryGetValue(system, out string icon))
                return RomIconBasePath + icon;
            return RomIconBasePath + RomDefaultIcon;
        }

        #region P/Invoke

        private const uint GENERIC_READ = 0x80000000;
        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint FILE_SHARE_WRITE = 0x00000002;
        private const uint FILE_SHARE_DELETE = 0x00000004;
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
                // ROM/disk metadata
                ".nfo", ".diz", ".sfv",
                ".md5", ".sha1", ".sha256", ".sha512",
                ".asc", ".hash", ".crc",
            };

        private static readonly HashSet<string> ImageExtensions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".png", ".jpg", ".jpeg", ".gif", ".bmp",
                ".tiff", ".tif", ".webp", ".ico", ".svg",
                ".heic", ".heif"
            };

        private static readonly HashSet<string> AudioExtensions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".mp3", ".flac", ".wav", ".ogg", ".m4a",
                ".aac", ".wma", ".opus", ".mid", ".midi"
            };

        private static readonly HashSet<string> VideoExtensions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".mp4", ".avi", ".mkv", ".webm", ".flv",
                ".wmv", ".mov", ".mpg", ".mpeg", ".m4v",
                ".ts", ".vob", ".3gp"
            };

        private static readonly HashSet<string> PdfExtensions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".pdf"
            };

        public static bool IsTextFile(string extension)
        {
            return !string.IsNullOrEmpty(extension) && TextExtensions.Contains(extension);
        }

        public static bool IsImageFile(string extension)
        {
            return !string.IsNullOrEmpty(extension) && ImageExtensions.Contains(extension);
        }

        public static bool IsAudioFile(string extension)
        {
            return !string.IsNullOrEmpty(extension) && AudioExtensions.Contains(extension);
        }

        public static bool IsVideoFile(string extension)
        {
            return !string.IsNullOrEmpty(extension) && VideoExtensions.Contains(extension);
        }

        public static bool IsMediaFile(string extension)
        {
            return IsAudioFile(extension) || IsVideoFile(extension);
        }

        public static bool IsSvgFile(string extension)
        {
            return string.Equals(extension, ".svg", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsPdfFile(string extension)
        {
            return !string.IsNullOrEmpty(extension) && PdfExtensions.Contains(extension);
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
                result.FileType = GetFileTypeLabel(ext, filePath);

                if (IsImageFile(ext) && !IsSvgFile(ext))
                {
                    await LoadImagePreview(filePath, result);
                }
                else if (IsSvgFile(ext))
                {
                    await LoadSvgPreview(filePath, result);
                }
                else if (IsPdfFile(ext))
                {
                    long fileSize = 0;
                    GetFileSizeWin32(filePath, out fileSize);
                    result.FileSizeBytes = fileSize;
                    result.Type = FilePreviewType.Pdf;
                    var page = await PdfPreviewService.LoadPageAsync(filePath, 0);
                    result.PdfPageCount = page.PageCount;
                    if (page.Bitmap != null)
                    {
                        result.ImageSource = page.Bitmap;
                        result.PixelWidth = page.PageWidth;
                        result.PixelHeight = page.PageHeight;
                    }
                }
                else if (IsTextFile(ext))
                {
                    await LoadTextPreview(filePath, result);
                }
                else if (IsVideoFile(ext))
                {
                    long fileSize = 0;
                    GetFileSizeWin32(filePath, out fileSize);
                    result.FileSizeBytes = fileSize;
                    result.Type = FilePreviewType.Video;
                }
                else if (IsAudioFile(ext))
                {
                    long fileSize = 0;
                    GetFileSizeWin32(filePath, out fileSize);
                    result.FileSizeBytes = fileSize;
                    result.Type = FilePreviewType.Audio;
                }
                else if (RomHeaderParser.IsRomFile(ext))
                {
                    await LoadRomPreview(filePath, result);
                }
                else
                {
                    long fileSize = 0;
                    GetFileSizeWin32(filePath, out fileSize);
                    result.FileSizeBytes = fileSize;
                    result.Type = FilePreviewType.Unsupported;
                }
            }
                catch (Exception ex)
                {
                    Log.Warn("FilePreviewService: image decode/bitmap failed", ex);
                    result.Type = FilePreviewType.Error;
                    result.ErrorMessage = $"Cannot create image bitmap: {ex.Message}";
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

                    // Disambiguate .md inside archives: peek first bytes
                    if (string.Equals(ext, ".md", StringComparison.OrdinalIgnoreCase))
                    {
                        byte[] buf = new byte[256];
                        int read = stream.Read(buf, 0, buf.Length);
                        bool hasNull = false;
                        for (int i = 0; i < read; i++)
                        {
                            if (buf[i] == 0) { hasNull = true; break; }
                        }
                        result.FileType = hasNull ? "Genesis ROM" : "Markdown";
                        stream.Position = 0;
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
                    else if (RomHeaderParser.IsRomFile(ext))
                    {
                        await LoadRomPreviewFromStream(stream, result, ext);
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
                Log.Warn("FilePreviewService: error previewing archive entry '{Archive}|{Internal}': {Error}",
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

            // Copy stream to MemoryStream on whatever thread we're on, then decode on background.
            byte[] imageBytes;
            using (var ms = new MemoryStream())
            {
                await stream.CopyToAsync(ms);
                imageBytes = ms.ToArray();
            }

            result.FileSizeBytes = imageBytes.Length;

            // Decode on background thread using BitmapDecoder
            var decoded = await Task.Run(async () =>
            {
                try
                {
                    using (var memStream = new InMemoryRandomAccessStream())
                    {
                        using (var writer = new DataWriter(memStream.GetOutputStreamAt(0)))
                        {
                            writer.WriteBytes(imageBytes);
                            await writer.StoreAsync();
                            await writer.FlushAsync();
                        }
                        memStream.Seek(0);

                        var decoder = await BitmapDecoder.CreateAsync(memStream);
                        var sb = await decoder.GetSoftwareBitmapAsync(
                            BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
                        return (sb, (int)decoder.PixelWidth, (int)decoder.PixelHeight, (string)null);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn("FilePreviewService: image decode/bitmap failed", ex);
                    return ((SoftwareBitmap)null, 0, 0, $"Cannot decode image: {ex.Message}");
                }
            });

            if (decoded.Item1 == null)
            {
                result.Type = FilePreviewType.Error;
                result.ErrorMessage = decoded.Item4 ?? "Failed to decode image from archive";
                return;
            }

            // Create WriteableBitmap on UI thread
            var dispatcher = CoreApplication.MainView.CoreWindow?.Dispatcher;
            if (dispatcher == null)
            {
                result.Type = FilePreviewType.Error;
                result.ErrorMessage = "Cannot access UI dispatcher for image preview";
                return;
            }

                var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var softwareBitmap = decoded.Item1;
            int pw = decoded.Item2;
            int ph = decoded.Item3;

            await dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                try
                {
                    var wb = new WriteableBitmap(pw, ph);
                    softwareBitmap.CopyToBuffer(wb.PixelBuffer);
                    result.ImageSource = wb;
                    result.PixelWidth = pw;
                    result.PixelHeight = ph;
                    tcs.SetResult(true);
                }
                catch (Exception ex)
                {
                    Log.Warn("FilePreviewService: image decode/bitmap failed", ex);
                    result.Type = FilePreviewType.Error;
                    result.ErrorMessage = $"Cannot create image bitmap: {ex.Message}";
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
            IntPtr hFile = CreateFileFromAppW(filePath, GENERIC_READ,
                FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, IntPtr.Zero,
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

            var fileData = await Task.Run(() =>
            {
                long fileSize = 0;
                GetFileSizeWin32(filePath, out fileSize);
                byte[] bytes = ReadFileWin32(filePath, MaxTextBytes);
                return (bytes, fileSize);
            });

            if (fileData.bytes == null)
            {
                result.Type = FilePreviewType.Error;
                result.ErrorMessage = "Failed to read file";
                return;
            }

            result.FileSizeBytes = fileData.fileSize;
            result.IsTruncated = fileData.fileSize > MaxTextBytes;

            result.TextContent = Encoding.UTF8.GetString(fileData.bytes);

            if (result.IsTruncated)
            {
                result.TextContent += $"\n\n... [truncated \u2014 showing {FormatSize(MaxTextBytes)} of {FormatSize(fileData.fileSize)}]";
            }
        }

        private static async Task LoadImagePreview(string filePath, FilePreviewResult result)
        {
            result.Type = FilePreviewType.Image;

            // ALL heavy work on background thread: file I/O + image decode via BitmapDecoder.
            // Only WriteableBitmap creation stays on UI thread (it's a UI element).
            var decoded = await Task.Run(async () =>
            {
                byte[] imageBytes = ReadFileWin32(filePath, 0);
                if (imageBytes == null) return ((SoftwareBitmap)null, 0, 0, "Failed to read image file");

                try
                {
                    using (var stream = new InMemoryRandomAccessStream())
                    {
                        using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
                        {
                            writer.WriteBytes(imageBytes);
                            await writer.StoreAsync();
                            await writer.FlushAsync();
                        }
                        stream.Seek(0);

                        var decoder = await BitmapDecoder.CreateAsync(stream);
                        var sb = await decoder.GetSoftwareBitmapAsync(
                            BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
                        return (sb, (int)decoder.PixelWidth, (int)decoder.PixelHeight, (string)null);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn("FilePreviewService: image decode/bitmap failed", ex);
                    return ((SoftwareBitmap)null, 0, 0, $"Cannot decode image: {ex.Message}");
                }
            });

            if (decoded.Item1 == null)
            {
                result.Type = FilePreviewType.Error;
                result.ErrorMessage = decoded.Item4 ?? "Failed to decode image";
                return;
            }

            // Get file size (cheap Win32 call, fine on UI thread for non-image types,
            // but we're already async here so no harm)
            long fileSize = 0;
            GetFileSizeWin32(filePath, out fileSize);
            result.FileSizeBytes = fileSize;

            // Create WriteableBitmap on UI thread — fast (just pixel buffer alloc + copy)
            var dispatcher = CoreApplication.MainView.CoreWindow?.Dispatcher;
            if (dispatcher == null)
            {
                result.Type = FilePreviewType.Error;
                result.ErrorMessage = "Cannot access UI dispatcher for image preview";
                return;
            }

                var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var softwareBitmap = decoded.Item1;
            int pw = decoded.Item2;
            int ph = decoded.Item3;

            await dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                try
                {
                    var wb = new WriteableBitmap(pw, ph);
                    softwareBitmap.CopyToBuffer(wb.PixelBuffer);
                    result.ImageSource = wb;
                    result.PixelWidth = pw;
                    result.PixelHeight = ph;
                    tcs.SetResult(true);
                }
                catch (Exception ex)
                {
                    Log.Warn("FilePreviewService: image decode/bitmap failed", ex);
                    result.Type = FilePreviewType.Error;
                    result.ErrorMessage = $"Cannot create image bitmap: {ex.Message}";
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

        private static async Task LoadRomPreview(string filePath, FilePreviewResult result)
        {
            result.Type = FilePreviewType.Rom;

            long fileSize = 0;
            byte[] headerBytes = await Task.Run(() =>
            {
                GetFileSizeWin32(filePath, out fileSize);
                return ReadFileWin32(filePath, 512);
            });

            result.FileSizeBytes = fileSize;
            string ext = Path.GetExtension(filePath);

            if (headerBytes != null &&
                RomHeaderParser.TryParseTitle(headerBytes, ext, out string title, out string system))
            {
                result.TextContent = title ?? Path.GetFileNameWithoutExtension(filePath);
                result.RomSystem = system;
                result.FileType = system + " ROM";
                result.RomIconPath = GetRomIconPath(system);
            }
            else
            {
                // Fallback: use filename without extension
                string name = Path.GetFileNameWithoutExtension(filePath);
                result.TextContent = name;
                result.RomSystem = "ROM";
                result.RomIconPath = GetRomIconPath(null);
            }
        }

        private static async Task LoadRomPreviewFromStream(Stream stream, FilePreviewResult result, string ext)
        {
            result.Type = FilePreviewType.Rom;

            byte[] headerBytes = new byte[512];
            int bytesRead = await stream.ReadAsync(headerBytes, 0, headerBytes.Length);

            if (bytesRead < headerBytes.Length)
            {
                byte[] trimmed = new byte[bytesRead];
                Array.Copy(headerBytes, trimmed, bytesRead);
                headerBytes = trimmed;
            }

            if (headerBytes.Length >= 16 &&
                RomHeaderParser.TryParseTitle(headerBytes, ext, out string title, out string system))
            {
                result.TextContent = title ?? result.FileType;
                result.RomSystem = system;
                result.FileType = system + " ROM";
                result.RomIconPath = GetRomIconPath(system);
            }
            else
            {
                result.TextContent = result.FileType;
                result.RomSystem = "ROM";
                result.RomIconPath = GetRomIconPath(null);
            }
        }

        /// <summary>
        /// Read file via Win32 CreateFileFromAppW + ReadFile.
        /// If maxBytes is 0, reads entire file.
        /// </summary>
        private static byte[] ReadFileWin32(string filePath, long maxBytes)
        {
            IntPtr hFile = CreateFileFromAppW(filePath, GENERIC_READ,
                FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, IntPtr.Zero,
                OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, IntPtr.Zero);

            if (hFile == INVALID_HANDLE_VALUE)
            {
                int err = Marshal.GetLastWin32Error();
                Log.Warn("FilePreviewService: CreateFileFromAppW failed for '{Path}' (error {Error})", filePath, err);
                return null;
            }

            try
            {
                long fileSize;
                if (!GetFileSizeEx(hFile, out fileSize))
                {
                    Log.Warn("FilePreviewService: GetFileSizeEx failed for '{Path}'", filePath);
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
                { "nfo", "NFO" }, { "diz", "Disk Info" }, { "sfv", "CRC Checksum" },
                { "md5", "MD5 Checksum" }, { "sha1", "SHA-1 Checksum" },
                { "sha256", "SHA-256 Checksum" }, { "sha512", "SHA-512 Checksum" },
                { "asc", "ASCII" }, { "hash", "Checksum" }, { "crc", "CRC Checksum" },
                { "nes", "NES ROM" }, { "sfc", "SNES ROM" }, { "smc", "SNES ROM" },
                { "gb", "Game Boy ROM" }, { "gbc", "Game Boy Color ROM" }, { "gba", "GBA ROM" },
                { "gen", "Genesis ROM" },
                { "sms", "Master System ROM" }, { "gg", "Game Gear ROM" },
                { "pce", "PC Engine ROM" }, { "tg16", "TurboGrafx-16 ROM" },
                { "a26", "Atari 2600 ROM" }, { "a52", "Atari 5200 ROM" }, { "a78", "Atari 7800 ROM" },
                { "j64", "Atari Jaguar ROM" }, { "jag", "Atari Jaguar ROM" }, { "lnx", "Atari Lynx ROM" },
                { "col", "ColecoVision ROM" }, { "int", "Intellivision ROM" },
                { "sg", "SG-1000 ROM" }, { "msx", "MSX ROM" },
                { "sna", "ZX Spectrum Snapshot" }, { "z80", "ZX Spectrum Snapshot" },
                { "vec", "Vectrex ROM" },
            };

        private static string GetFileTypeLabel(string extension, string filePath = null)
        {
            if (string.IsNullOrEmpty(extension))
                return "Unknown";

            string key = extension.TrimStart('.').ToLowerInvariant();

            // Ambiguous extension: peek at file contents to disambiguate
            if (key == "md" && !string.IsNullOrEmpty(filePath))
            {
                try
                {
                    using (var fs = Win32FileStream.OpenRead(filePath))
                    {
                        byte[] buf = new byte[256];
                        int read = fs.Read(buf, 0, buf.Length);
                        for (int i = 0; i < read; i++)
                        {
                            if (buf[i] == 0)
                                return "Genesis ROM";
                        }
                    }
                }
                catch { }
                return "Markdown";
            }

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
