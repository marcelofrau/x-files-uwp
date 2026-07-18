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

        private CancellationTokenSource _previewCts;

        public ColumnState Parent => _history.Count > 0 ? _history.Peek() : null;
        public ColumnState Current => _current;
        public ColumnState Preview => _preview;

        public event Action ColumnsChanged;
        public event Action PreviewChanged;
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
        /// If item is file -> no-op (files handled by preview).
        /// </summary>
        public async Task DrillInAsync()
        {
            var selected = _current.GetSelectedEntry();
            if (selected == null || !selected.IsDirectory)
                return;

            string path = selected.Name == ".."
                ? _current.ParentPath
                : selected.FullPath;

            if (string.IsNullOrEmpty(path))
            {
                // Going back to root
                await LoadRootAsync();
                return;
            }

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
            await _current.LoadAsync(path);

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
                    // Don't preview ".." — show parent folder info or nothing
                    _preview = null;
                    return;
                }

                _preview = new ColumnState { Path = selected.FullPath, Label = selected.Name };
                await _preview.LoadAsync(selected.FullPath);
            }
            else
            {
                // File preview — load via FilePreviewService
                _preview = new ColumnState
                {
                    Path = selected.FullPath,
                    Label = selected.Name,
                    IsFilePreview = true
                };

                var previewResult = await FilePreviewService.GetPreviewAsync(selected.FullPath);
                _preview.PreviewType = previewResult.Type;
                _preview.PreviewTextContent = previewResult.TextContent;
                _preview.PreviewImageSource = previewResult.ImageSource;
                _preview.PreviewErrorMessage = previewResult.ErrorMessage;
                _preview.PreviewFileType = previewResult.FileType;
                _preview.PreviewFileSize = previewResult.FileSizeBytes;
                _preview.PreviewIsTruncated = previewResult.IsTruncated;
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

        // File preview data
        public FilePreviewType PreviewType { get; set; }
        public string PreviewTextContent { get; set; }
        public ImageSource PreviewImageSource { get; set; }
        public string PreviewErrorMessage { get; set; }
        public string PreviewFileType { get; set; }
        public long PreviewFileSize { get; set; }
        public bool PreviewIsTruncated { get; set; }

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

        public async Task LoadAsync(string path)
        {
            Entries = await DirectoryScanner.ScanAsync(path);

            // Ensure ".." is selected if available
            if (SelectedIndex == 0 && Entries.Count > 0 && Entries[0].Name == "..")
                SelectedIndex = 0;
        }

        public FileEntry GetSelectedEntry()
        {
            if (SelectedIndex >= 0 && SelectedIndex < Entries.Count)
                return Entries[SelectedIndex];
            return null;
        }
    }
}
