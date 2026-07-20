using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI.Xaml.Media;
using XFiles.FileSystem;

namespace XFiles.Navigation
{
    /// <summary>
    /// Manages 3-column Miller navigation state. Pure logic, no UI dependency.
    /// Columns: Parent (left), Current (center, has focus), Preview (right, read-only).
    /// </summary>
    public class ColumnNavigator
    {
        private readonly Stack<ColumnState> _history = new Stack<ColumnState>();
        private ColumnState _current;
        private ColumnState _preview;
        private CancellationTokenSource _loadCts;

        private CancellationTokenSource _previewCts;
        private int _previewGeneration;
        private readonly ArchiveBrowser _archiveBrowser = new ArchiveBrowser();

        public ColumnState Parent => _history.Count > 0 ? _history.Peek() : null;
        public ColumnState Current => _current;
        public ColumnState Preview => _preview;

        public event Action ColumnsChanged;
        public event Action PreviewChanged;
        public event Action<bool> LoadingChanged;
        public event Action<string> Error;

        public ColumnNavigator()
        {
            _current = new ColumnState { Path = null, Label = "(Drives)" };
        }

        /// <summary>
        /// Load root directory (drive list).
        /// </summary>
        public async Task LoadRootAsync()
        {
            _history.Clear();
            _current = new ColumnState { Path = null, Label = "(Drives)" };
            _preview = null;

            await _current.LoadAsync(null);
            ColumnsChanged?.Invoke();
        }

        /// <summary>
        /// Drill into the currently selected item (A/Right button).
        /// If item is directory -> push current to history, load new dir.
        /// If item is archive -> push current to history, open archive as virtual folder.
        /// If item is file -> no-op (files handled by preview).
        /// </summary>
        public async Task DrillInAsync()
        {
            ++_previewGeneration;
            var selected = _current.GetSelectedEntry();
            if (selected == null) return;

            // Handle archives
            if (selected.IsArchive)
            {
                await DrillIntoArchiveAsync(selected);
                return;
            }

            // Handle directories (including directories inside archives)
            if (!selected.IsDirectory) return;

            // If we're already in an archive, drill into archive subdirectory
            if (_current.IsArchive)
            {
                await DrillIntoArchiveSubdirectoryAsync(selected);
                return;
            }

            // Normal filesystem directory
            string path = selected.Name == ".."
                ? _current.ParentPath
                : selected.FullPath;

            if (string.IsNullOrEmpty(path))
            {
                // Going back to root
                await LoadRootAsync();
                return;
            }

            // Cancel any in-flight scan
            _loadCts?.Cancel();
            _loadCts = new CancellationTokenSource();
            var token = _loadCts.Token;

            // Push current state to history
            _history.Push(new ColumnState
            {
                Path = _current.Path,
                Label = _current.Label,
                SelectedIndex = _current.SelectedIndex,
                Entries = _current.Entries
            });

            // Current becomes the new directory
            _current = new ColumnState { Path = path, Label = selected.Name };

            LoadingChanged?.Invoke(true);
            try
            {
                await _current.LoadAsync(path, token);
            }
            catch (OperationCanceledException)
            {
                Log.Information("DrillInAsync: scan cancelled for {Path}", path);
                return;
            }
            finally
            {
                LoadingChanged?.Invoke(false);
            }

            // Update preview: show first item of new current
            await UpdatePreviewAsync();

            ColumnsChanged?.Invoke();
        }

        /// <summary>
        /// Drill into an archive — open it and show contents.
        /// </summary>
        private async Task DrillIntoArchiveAsync(FileEntry archiveEntry)
        {
            ++_previewGeneration;
            Log.Information("ColumnNavigator: drilling into archive {Path}", archiveEntry.FullPath);

            // Push current state to history
            _history.Push(new ColumnState
            {
                Path = _current.Path,
                Label = _current.Label,
                SelectedIndex = _current.SelectedIndex,
                Entries = _current.Entries
            });

            // Load archive root contents
            var entries = _archiveBrowser.ListEntries(archiveEntry.FullPath, "");

            _current = new ColumnState
            {
                Path = archiveEntry.FullPath,
                Label = archiveEntry.Name,
                Entries = entries.ToList(),
                IsArchive = true,
                ArchiveRootPath = archiveEntry.FullPath,
                ArchiveInternalPath = ""
            };

            // Update preview: show first item of new current
            await UpdatePreviewAsync();

            ColumnsChanged?.Invoke();
        }

        /// <summary>
        /// Drill into a directory inside an archive.
        /// </summary>
        private async Task DrillIntoArchiveSubdirectoryAsync(FileEntry dirEntry)
        {
            ++_previewGeneration;
            Log.Information("ColumnNavigator: drilling into archive subdirectory {Archive}|{Internal}",
                dirEntry.ArchiveRootPath, dirEntry.ArchiveInternalPath);

            // Push current state to history
            _history.Push(new ColumnState
            {
                Path = _current.Path,
                Label = _current.Label,
                SelectedIndex = _current.SelectedIndex,
                Entries = _current.Entries
            });

            // Load subdirectory contents from archive
            var entries = _archiveBrowser.ListEntries(dirEntry.ArchiveRootPath, dirEntry.ArchiveInternalPath);

            _current = new ColumnState
            {
                Path = dirEntry.FullPath,
                Label = dirEntry.Name,
                Entries = entries.ToList(),
                IsArchive = true,
                ArchiveRootPath = dirEntry.ArchiveRootPath,
                ArchiveInternalPath = dirEntry.ArchiveInternalPath
            };

            // Update preview: show first item of new current
            await UpdatePreviewAsync();

            ColumnsChanged?.Invoke();
        }

        /// <summary>
        /// Drill out / go back (B/Left button).
        /// Pop history, restore previous state.
        /// </summary>
        public async Task DrillOutAsync()
        {
            if (_history.Count == 0)
                return;

            ++_previewGeneration;
            var previous = _history.Pop();
            _current = previous;

            await UpdatePreviewAsync();
            ColumnsChanged?.Invoke();
        }

        /// <summary>
        /// Update preview column based on current selection.
        /// If selected item is directory -> show its children.
        /// If selected item is file -> load text/image preview via FilePreviewService.
        /// </summary>
        public async Task UpdatePreviewAsync()
        {
            int gen = ++_previewGeneration;
            var selected = _current.GetSelectedEntry();
            if (selected == null)
            {
                _preview = null;
                return;
            }

            if (selected.IsDirectory)
            {
                if (selected.Name == "..")
                {
                    _preview = null;
                    return;
                }

                if (_current.IsArchive && !string.IsNullOrEmpty(selected.ArchiveInternalPath))
                {
                    _preview = new ColumnState { Path = selected.FullPath, Label = selected.Name };
                    await _preview.LoadArchiveDirectoryAsync(_archiveBrowser, selected.ArchiveRootPath, selected.ArchiveInternalPath);
                    if (_previewGeneration != gen) return;
                }
                else
                {
                    _preview = new ColumnState { Path = selected.FullPath, Label = selected.Name };
                    await _preview.LoadAsync(selected.FullPath);
                    if (_previewGeneration != gen) return;
                }
            }
            else
            {
                if (selected.IsArchive)
                {
                    _preview = new ColumnState { Path = selected.FullPath, Label = selected.Name };
                    await _preview.LoadArchiveDirectoryAsync(_archiveBrowser, selected.FullPath, "");
                    if (_previewGeneration != gen) return;
                }
                else
                {
                    _preview = new ColumnState
                    {
                        Path = selected.FullPath,
                        Label = selected.Name,
                        IsFilePreview = true
                    };

                    FilePreviewResult previewResult;

                    if (!string.IsNullOrEmpty(selected.ArchiveRootPath))
                    {
                        previewResult = await FilePreviewService.GetPreviewFromArchiveAsync(
                            _archiveBrowser, selected.ArchiveRootPath, selected.ArchiveInternalPath);
                    }
                    else
                    {
                        previewResult = await FilePreviewService.GetPreviewAsync(selected.FullPath);
                    }

                    if (_previewGeneration != gen) return;

                    _preview.PreviewType = previewResult.Type;
                    _preview.PreviewTextContent = previewResult.TextContent;
                    _preview.PreviewImageSource = previewResult.ImageSource;
                    _preview.PreviewErrorMessage = previewResult.ErrorMessage;
                    _preview.PreviewFileType = previewResult.FileType;
                    _preview.PreviewFileSize = previewResult.FileSizeBytes;
                    _preview.PreviewIsTruncated = previewResult.IsTruncated;
                    _preview.PreviewPixelWidth = previewResult.PixelWidth;
                    _preview.PreviewPixelHeight = previewResult.PixelHeight;
                    _preview.PreviewFilePath = selected.FullPath;
                }
            }
        }

        /// <summary>
        /// Called when selection changes in current column -> update preview (debounced).
        /// Rapid scrolling cancels previous load; preview loads only after 150ms pause.
        /// </summary>
        public async Task OnSelectionChangedAsync()
        {
            _previewCts?.Cancel();
            _previewCts = new CancellationTokenSource();
            var token = _previewCts.Token;

            try
            {
                await Task.Delay(150, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            await UpdatePreviewAsync();
            PreviewChanged?.Invoke();
        }

        /// <summary>
        /// Re-scan current directory (after rename/delete). Preserves selection by name.
        /// </summary>
        public async Task RefreshCurrentAsync()
        {
            if (_current == null) return;

            string prevName = _current.GetSelectedEntry()?.Name;
            int prevIndex = _current.SelectedIndex;

            // Cancel any in-flight scan
            _loadCts?.Cancel();
            _loadCts = new CancellationTokenSource();
            var token = _loadCts.Token;

            LoadingChanged?.Invoke(true);
            try
            {
                if (_current.IsArchive && _current.ArchiveRootPath != null)
                {
                    await _current.LoadArchiveDirectoryAsync(_archiveBrowser,
                        _current.ArchiveRootPath, _current.ArchiveInternalPath ?? "");
                }
                else
                {
                    await _current.LoadAsync(_current.Path, token);
                }
            }
            catch (OperationCanceledException)
            {
                Log.Information("RefreshCurrentAsync: scan cancelled");
                return;
            }
            finally
            {
                LoadingChanged?.Invoke(false);
            }

            // Try to preserve selection
            if (prevName != null)
            {
                int idx = _current.Entries.FindIndex(e => e.Name == prevName);
                _current.SelectedIndex = idx >= 0 ? idx : Math.Min(prevIndex, _current.Entries.Count - 1);
            }

            ColumnsChanged?.Invoke();
        }
    }

    /// <summary>
    /// State of a single column: path, entries, selection.
    /// </summary>
    public class ColumnState
    {
        public string Path { get; set; }
        public string Label { get; set; }
        public int SelectedIndex { get; set; }
        public List<FileEntry> Entries { get; set; } = new List<FileEntry>();
        public bool IsFilePreview { get; set; }
        public bool IsArchive { get; set; }
        public string ArchiveRootPath { get; set; }
        public string ArchiveInternalPath { get; set; }

        // File preview data
        public FilePreviewType PreviewType { get; set; }
        public string PreviewTextContent { get; set; }
        public ImageSource PreviewImageSource { get; set; }
        public string PreviewErrorMessage { get; set; }
        public string PreviewFileType { get; set; }
        public long PreviewFileSize { get; set; }
        public bool PreviewIsTruncated { get; set; }
        public int PreviewPixelWidth { get; set; }
        public int PreviewPixelHeight { get; set; }
        public string PreviewFilePath { get; set; }

        public string ParentPath
        {
            get
            {
                if (string.IsNullOrEmpty(Path))
                    return null;

                var parent = System.IO.Directory.GetParent(Path);
                return parent?.FullName;
            }
        }

        public async Task LoadAsync(string path, CancellationToken token = default)
        {
            Entries = await DirectoryScanner.ScanAsync(path, token);

            // Ensure ".." is selected if available
            if (SelectedIndex == 0 && Entries.Count > 0 && Entries[0].Name == "..")
                SelectedIndex = 0;
        }

        public async Task LoadArchiveDirectoryAsync(ArchiveBrowser archiveBrowser, string archivePath, string subPath)
        {
            Entries = archiveBrowser.ListEntries(archivePath, subPath).ToList();
            if (SelectedIndex == 0 && Entries.Count > 0)
                SelectedIndex = 0;
            await Task.CompletedTask;
        }

        public FileEntry GetSelectedEntry()
        {
            if (SelectedIndex >= 0 && SelectedIndex < Entries.Count)
                return Entries[SelectedIndex];
            return null;
        }
    }
}
