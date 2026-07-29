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
        private long _previewGeneration;
        private readonly ArchiveBrowser _archiveBrowser = new ArchiveBrowser();

        // Gamelist cache: parsed once per directory, cleared on navigation
        private Dictionary<string, GamelistEntry> _gamelistCache;
        private string _gamelistDirectory;

        public ColumnState Parent => _history.Count > 0 ? _history.Peek() : null;
        public ColumnState Current => _current;
        public ColumnState Preview => _preview;

        /// <summary>
        /// Returns the full filesystem path from root to current column.
        /// At root returns empty string. Otherwise builds e.g. "E:\Users\Documents".
        /// Skips the virtual "(Drives)" root label.
        /// </summary>
        public string GetBreadcrumbPath()
        {
            if (_history.Count == 0)
                return "";

            // Stack enumerator pops from top (most recent) first — reverse to get root→current order
            var labels = new List<string>();
            foreach (var state in _history)
            {
                if (!string.IsNullOrEmpty(state.Label) && state.Label != "(Drives)")
                    labels.Add(state.Label.TrimEnd('\\'));
            }
            labels.Reverse();

            string path = string.Join(@"\", labels);
            if (!string.IsNullOrEmpty(_current?.Label) && _current.Label != "(Drives)")
            {
                string currentLabel = _current.Label.TrimEnd('\\');
                if (path.Length > 0)
                    path = path + @"\" + currentLabel;
                else
                    path = currentLabel;
            }

            return path;
        }

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
            _current.ClearSearch();
            _preview = null;

            await _current.LoadAsync(null);

            // Inject Favorites virtual entry at top (also into AllEntries for ClearSearch)
            var favEntry = new FileEntry
            {
                Name = "Favorites",
                FullPath = null,
                IsDirectory = true,
                IsDrive = false,
                IsVirtual = true
            };
            _current.Entries.Insert(0, favEntry);
            if (_current.AllEntries != null)
                _current.AllEntries.Insert(0, favEntry);

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

            // Handle virtual entries (e.g. Favorites)
            if (selected.IsVirtual)
            {
                await DrillIntoFavoritesAsync();
                return;
            }

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
            _loadCts?.Dispose();
            _loadCts = new CancellationTokenSource();
            var token = _loadCts.Token;

            // Push current state to history (preserve full list for ClearSearch on DrillOut)
            _history.Push(new ColumnState
            {
                Path = _current.Path,
                Label = _current.Label,
                SelectedIndex = _current.SelectedIndex,
                Entries = _current.Entries,
                AllEntries = _current.AllEntries
            });

            // Current becomes the new directory
            _current = new ColumnState { Path = path, Label = selected.Name };
            _current.ClearSearch();

            LoadingChanged?.Invoke(true);
            try
            {
                await _current.LoadAsync(path, token);
            }
            catch (OperationCanceledException)
            {
                Log.Info("DrillInAsync: scan cancelled for {Path}", path);
                return;
            }
            finally
            {
                LoadingChanged?.Invoke(false);
            }

            // Load gamelist.xml for this directory
            await LoadGamelistAsync(path);

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
            Log.Info("ColumnNavigator: drilling into archive {Path}", archiveEntry.FullPath);

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
            _current.ClearSearch();

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
            Log.Info("ColumnNavigator: drilling into archive subdirectory {Archive}|{Internal}",
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
            _current.ClearSearch();

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
            _current.ClearSearch();

            // If returning to favorites column, reload list
            if (_current.IsFavorite)
            {
                var favs = await FavoritesManager.GetAllAsync();
                _current.Entries = favs.Select(f => new FileEntry
                {
                    Name = f.Name,
                    FullPath = f.Path,
                    IsDirectory = f.IsDirectory
                }).ToList();
                _current.IsFilePreview = false;
            }

            // Reload gamelist for the parent directory
            if (_current.IsArchive)
            {
                // Inside archive: gamelist from parent directory still applies
                // (don't clear _gamelistCache)
            }
            else if (!string.IsNullOrEmpty(_current.Path))
            {
                await LoadGamelistAsync(_current.Path);
            }

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
            long gen = ++_previewGeneration;
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
                    // Check gamelist first — zip may have ROM metadata directly
                    GamelistEntry gamelistEntry = _gamelistCache != null
                        ? GamelistParser.FindEntry(_gamelistCache, selected.Name)
                        : null;

                    if (gamelistEntry != null)
                    {
                        // ROM preview — gamelist provides metadata
                        string romSystem = GetSystemFromDirectory(_current.Path);
                        _preview = new ColumnState
                        {
                            Path = selected.FullPath,
                            Label = selected.Name,
                            IsFilePreview = true,
                            PreviewType = FilePreviewType.Rom,
                            PreviewFilePath = selected.FullPath,
                            PreviewFileType = "ZIP Archive",
                            PreviewFileSize = selected.SizeBytes,
                            PreviewRomSystem = romSystem,
                            PreviewRomIconPath = FilePreviewService.GetRomIconPathPublic(romSystem),
                            PreviewHasGamelistData = true,
                            PreviewTextContent = !string.IsNullOrEmpty(gamelistEntry.Name)
                                ? gamelistEntry.Name
                                : System.IO.Path.GetFileNameWithoutExtension(selected.Name),
                            PreviewRomDescription = gamelistEntry.Description,
                            PreviewRomDeveloper = gamelistEntry.Developer,
                            PreviewRomPublisher = gamelistEntry.Publisher,
                            PreviewRomGenre = gamelistEntry.Genre,
                            PreviewRomPlayers = gamelistEntry.Players,
                            PreviewRomRating = gamelistEntry.Rating,
                            PreviewRomCoverLocalPath = GamelistParser.GetCoverPath(gamelistEntry)
                        };
                        if (gamelistEntry.ReleaseDate.HasValue)
                            _preview.PreviewRomReleaseYear = gamelistEntry.ReleaseDate.Value.Year;

                        Log.Verb("ColumnNavigator: archive gamelist enriched '{Name}' — genre={Genre} dev={Dev}",
                            gamelistEntry.Name, gamelistEntry.Genre, gamelistEntry.Developer);
                    }
                    else
                    {
                        // No gamelist — show children in preview list (imitate folder)
                        _preview = new ColumnState { Path = selected.FullPath, Label = selected.Name };
                        await _preview.LoadArchiveDirectoryAsync(_archiveBrowser, selected.FullPath, "");
                        if (_previewGeneration != gen) return;
                    }
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
                    _preview.PreviewPdfPageCount = previewResult.PdfPageCount;
                    _preview.PreviewRomSystem = previewResult.RomSystem;
                    _preview.PreviewRomIconPath = previewResult.RomIconPath;

                    // Enrich with gamelist data if available
                    if (_gamelistCache != null && previewResult.Type == FilePreviewType.Rom)
                    {
                        string lookupName = selected.Name;

                        // For files inside archive, also try parent ZIP name
                        GamelistEntry gamelistEntry = GamelistParser.FindEntry(_gamelistCache, lookupName);
                        if (gamelistEntry == null && !string.IsNullOrEmpty(selected.ArchiveRootPath))
                        {
                            string zipName = System.IO.Path.GetFileName(selected.ArchiveRootPath);
                            gamelistEntry = GamelistParser.FindEntry(_gamelistCache, zipName);
                        }

                        if (gamelistEntry != null)
                        {
                            _preview.PreviewHasGamelistData = true;
                            if (!string.IsNullOrEmpty(gamelistEntry.Name))
                                _preview.PreviewTextContent = gamelistEntry.Name;
                            _preview.PreviewRomDescription = gamelistEntry.Description;
                            _preview.PreviewRomDeveloper = gamelistEntry.Developer;
                            _preview.PreviewRomPublisher = gamelistEntry.Publisher;
                            _preview.PreviewRomGenre = gamelistEntry.Genre;
                            _preview.PreviewRomPlayers = gamelistEntry.Players;
                            _preview.PreviewRomRating = gamelistEntry.Rating;
                            if (gamelistEntry.ReleaseDate.HasValue)
                                _preview.PreviewRomReleaseYear = gamelistEntry.ReleaseDate.Value.Year;
                            _preview.PreviewRomCoverLocalPath = GamelistParser.GetCoverPath(gamelistEntry);

                            Log.Verb("ColumnNavigator: gamelist enriched '{Name}' — genre={Genre} dev={Dev}",
                                gamelistEntry.Name, gamelistEntry.Genre, gamelistEntry.Developer);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Jump to first entry whose name starts with the given letter.
        /// </summary>
        public void JumpToLetter(char letter)
        {
            char upper = char.ToUpperInvariant(letter);
            var entries = _current.Entries;
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].Name.Length > 0 && char.ToUpperInvariant(entries[i].Name[0]) == upper)
                {
                    _current.SelectedIndex = i;
                    ColumnsChanged?.Invoke();
                    return;
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
            _previewCts?.Dispose();
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
        /// Load gamelist.xml for the current directory if it exists.
        /// Uses XmlReader streaming (no DOM overhead).
        /// </summary>
        private async Task LoadGamelistAsync(string directoryPath)
        {
            _gamelistCache = null;
            _gamelistDirectory = null;

            if (string.IsNullOrEmpty(directoryPath)) return;

            string gamelistPath = System.IO.Path.Combine(directoryPath, "gamelist.xml");
            if (!DirectoryScanner.FileExists(gamelistPath)) return;

            try
            {
                _gamelistCache = await GamelistParser.ParseAsync(gamelistPath);
                _gamelistDirectory = directoryPath;
                Log.Info("ColumnNavigator: loaded gamelist from {Path} ({Count} entries)",
                    gamelistPath, _gamelistCache.Count);
            }
            catch (Exception ex)
            {
                Log.Warn("ColumnNavigator: failed to load gamelist from {Path}: {Error}",
                    gamelistPath, ex.Message);
            }
        }

        /// <summary>
        /// Infer ROM system name from directory path (e.g. "E:\Atari 2600" → "Atari 2600").
        /// Falls back to "ROM" if no recognizable system name found.
        /// </summary>
        private static string GetSystemFromDirectory(string dirPath)
        {
            if (string.IsNullOrEmpty(dirPath)) return "ROM";

            string dirName = System.IO.Path.GetFileName(dirPath);
            if (string.IsNullOrEmpty(dirName)) return "ROM";

            string lower = dirName.ToLowerInvariant();
            if (lower.Contains("atari 2600") || lower.Contains("a2600")) return "Atari 2600";
            if (lower.Contains("atari 5200") || lower.Contains("a5200")) return "Atari 5200";
            if (lower.Contains("atari 7800") || lower.Contains("a7800")) return "Atari 7800";
            if (lower.Contains("atari lynx")) return "Atari Lynx";
            if (lower.Contains("atari jaguar")) return "Atari Jaguar";
            if (lower.Contains("genesis") || lower.Contains("mega drive")) return "Genesis/Mega Drive";
            if (lower.Contains("master system")) return "Master System";
            if (lower.Contains("game gear")) return "Game Gear";
            if (lower.Contains("game boy color") || lower.Contains("gbc")) return "Game Boy Color";
            if (lower.Contains("game boy advance") || lower.Contains("gba")) return "GBA";
            if (lower.Contains("game boy") || lower.Contains("gb ")) return "Game Boy";
            if (lower.Contains("snes") || lower.Contains("super nintendo") || lower.Contains("super famicom"))
                return "SNES";
            if (lower.Contains("nes") || lower.Contains("nintendo") || lower.Contains("famicom"))
                return "NES";
            if (lower.Contains("pc engine") || lower.Contains("turbografx")) return "PC Engine/TurboGrafx-16";
            if (lower.Contains("colecovision")) return "ColecoVision";
            if (lower.Contains("intellivision")) return "Intellivision";
            if (lower.Contains("sg-1000")) return "SG-1000";
            if (lower.Contains("msx")) return "MSX";
            if (lower.Contains("zx spectrum") || lower.Contains("spectrum")) return "ZX Spectrum";
            if (lower.Contains("vectrex")) return "Vectrex";
            if (lower.Contains("nintendo 64") || lower.Contains("n64")) return "Nintendo 64";
            if (lower.Contains("nintendo ds") || lower.Contains("nds")) return "Nintendo DS";
            if (lower.Contains("nintendo 3ds") || lower.Contains("3ds")) return "Nintendo 3DS";
            if (lower.Contains("virtualboy") || lower.Contains("virtual boy")) return "Virtual Boy";
            if (lower.Contains("gamecube") || lower.Contains("ngc")) return "GameCube";
            if (lower.Contains("dreamcast")) return "Dreamcast";
            if (lower.Contains("saturn")) return "Saturn";
            if (lower.Contains("playstation 3") || lower.Contains("ps3")) return "PlayStation";
            if (lower.Contains("playstation 2") || lower.Contains("ps2")) return "PlayStation";
            if (lower.Contains("playstation") || lower.Contains("psx") || lower.Contains("ps1")) return "PlayStation";
            if (lower.Contains("psp") || lower.Contains("playstation portable")) return "PSP";
            if (lower.Contains("wii u") || lower.Contains("wiiu")) return "Wii U";
            if (lower.Contains("wii")) return "Wii";
            if (lower.Contains("neo geo pocket") || lower.Contains("ngp")) return "Neo Geo Pocket";
            if (lower.Contains("neo geo")) return "Neo Geo";
            if (lower.Contains("switch") || lower.Contains("nsp") || lower.Contains("xci")) return "Switch";
            if (lower.Contains("32x") || lower.Contains("32-x")) return "Sega 32X";
            if (lower.Contains("segacd") || lower.Contains("segA cd") || lower.Contains("mega cd")) return "Sega CD";
            if (lower.Contains("wonderswan")) return "WonderSwan";

            return dirName;
        }

        /// <summary>
        /// Re-scan current directory (after rename/delete). Preserves selection by name.
        /// </summary>
        public async Task RefreshCurrentAsync(string selectName = null)
        {
            if (_current == null) return;

            string prevName = selectName ?? _current.GetSelectedEntry()?.Name;
            int prevIndex = _current.SelectedIndex;

            // Cancel any in-flight scan
            _loadCts?.Cancel();
            _loadCts?.Dispose();
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
                Log.Info("RefreshCurrentAsync: scan cancelled");
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

        private async Task DrillIntoFavoritesAsync()
        {
            ++_previewGeneration;
            Log.Info("ColumnNavigator: drilling into Favorites");

            _history.Push(new ColumnState
            {
                Path = _current.Path,
                Label = _current.Label,
                SelectedIndex = _current.SelectedIndex,
                Entries = _current.Entries
            });

            var favs = await FavoritesManager.GetAllAsync();
            var entries = favs.Select(f => new FileEntry
            {
                Name = f.Name,
                FullPath = f.Path,
                IsDirectory = f.IsDirectory
            }).ToList();

            _current = new ColumnState
            {
                Path = null,
                Label = "Favorites",
                Entries = entries,
                IsFavorite = true
            };
            _current.ClearSearch();

            await UpdatePreviewAsync();
            ColumnsChanged?.Invoke();
        }

        private static string FormatSizeHuman(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
            return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
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
        public List<FileEntry> AllEntries { get; set; }
        private List<FileEntry> _allEntries;
        public string SearchQuery { get; set; }
        public bool IsSearchActive => !string.IsNullOrEmpty(SearchQuery);
        public bool IsFilePreview { get; set; }
        public bool IsArchive { get; set; }
        public bool IsFavorite { get; set; }
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
        public int PreviewPdfPageCount { get; set; }
        public string PreviewRomSystem { get; set; }
        public string PreviewRomIconPath { get; set; }

        // Gamelist enrichment data
        public bool PreviewHasGamelistData { get; set; }
        public string PreviewRomDescription { get; set; }
        public string PreviewRomDeveloper { get; set; }
        public string PreviewRomPublisher { get; set; }
        public string PreviewRomGenre { get; set; }
        public int PreviewRomPlayers { get; set; }
        public float PreviewRomRating { get; set; }
        public int PreviewRomReleaseYear { get; set; }
        public string PreviewRomCoverLocalPath { get; set; }

        public string ParentPath
        {
            get
            {
                if (string.IsNullOrEmpty(Path))
                    return null;

                return System.IO.Path.GetDirectoryName(Path);
            }
        }

        public async Task LoadAsync(string path, CancellationToken token = default)
        {
            _allEntries = await DirectoryScanner.ScanAsync(path, token);
            AllEntries = _allEntries;
            Entries = new List<FileEntry>(_allEntries);

            // Ensure ".." is selected if available
            if (SelectedIndex == 0 && Entries.Count > 0 && Entries[0].Name == "..")
                SelectedIndex = 0;
        }

        public async Task LoadArchiveDirectoryAsync(ArchiveBrowser archiveBrowser, string archivePath, string subPath)
        {
            _allEntries = archiveBrowser.ListEntries(archivePath, subPath).ToList();
            AllEntries = _allEntries;
            Entries = new List<FileEntry>(_allEntries);
            if (SelectedIndex == 0 && Entries.Count > 0)
                SelectedIndex = 0;
            await Task.CompletedTask;
        }

        public void ApplySearch(string query)
        {
            SearchQuery = query;
            if (string.IsNullOrEmpty(query) || _allEntries == null)
            {
                ClearSearch();
                return;
            }

            string lower = query.ToLowerInvariant();
            Entries = _allEntries.Where(e =>
                e.Name != ".." && e.Name.IndexOf(lower, StringComparison.OrdinalIgnoreCase) >= 0
            ).ToList();
            SelectedIndex = 0;
        }

        public void ClearSearch()
        {
            SearchQuery = null;
            if (_allEntries == null && AllEntries != null)
                _allEntries = AllEntries;
            if (_allEntries != null)
                Entries = new List<FileEntry>(_allEntries);
        }

        public FileEntry GetSelectedEntry()
        {
            if (SelectedIndex >= 0 && SelectedIndex < Entries.Count)
                return Entries[SelectedIndex];
            return null;
        }
    }
}
