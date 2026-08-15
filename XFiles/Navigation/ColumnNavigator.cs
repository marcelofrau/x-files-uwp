using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI.Xaml.Media;
using XFiles.FileSystem;
using XFiles.Network;
using XFiles.Services;

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
        private readonly PortalBrowser _portalBrowser = new PortalBrowser();

        // Gamelist cache: parsed once per directory, cleared on navigation
        private Dictionary<string, GamelistEntry> _gamelistCache;
        private string _gamelistDirectory;

        // True while a portal load (REST listing or archive download) is in flight.
        // Consecutive drill-in/drill-out presses during a slow portal navigation are
        // ignored so they can't queue up and interleave loads.
        private bool _portalBusy;

        // Same guard for network loads (SMB list/connect). SMB round-trips against a
        // remote server can be slow — queueing drill-ins would interleave sessions.
        private bool _networkBusy;

        private readonly SmbBrowser _networkBrowser = new SmbBrowser();

        /// <summary>SMB browser facade (vault + logging). Used by page-level copy/rename/delete.</summary>
        public SmbBrowser NetworkBrowser => _networkBrowser;

        /// <summary>
        /// Raised when a portal drill-in is attempted while the portal is not connected.
        /// MillerColumnsPage shows the setup dialog (exemption instructions + QR).
        /// </summary>
        public event Action PortalSetupRequired;

        /// <summary>
        /// Raised when the user confirms the "＋ Add location" action row.
        /// MillerColumnsPage shows the NetworkLocationDialog and saves the result.
        /// </summary>
        public event Action NetworkAddLocationRequested;
        public event Action NetworkDownloadUrlRequested;

        /// <summary>
        /// Raised when a network operation fails (connect, list, open). MillerColumnsPage
        /// shows the message in the error overlay.
        /// </summary>
        public event Action<NetworkOperationReason, string> NetworkError;

        /// <summary>
        /// Raised when a portal preview (directory listing or file download via REST) is
        /// in flight. MillerColumnsPage shows a spinner over the preview column.
        /// </summary>
        public event Action<bool> PreviewLoadingChanged;

        /// <summary>
        /// Set by MillerColumnsPage: given a label, returns an IProgress<double> bound to
        /// the OperationProgressDialog. Used for explicit portal downloads (> 25 MB).
        /// </summary>
        public Func<string, IProgress<double>> DownloadProgressFactory;

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
            InjectRootVirtualEntries(_current);

            ColumnsChanged?.Invoke();
        }

        /// <summary>
        /// Adds the virtual root entries (Favorites, User Folders portal root, separator)
        /// and moves the AppData entry above the separator. Must run after ANY root
        /// (path == null) scan — both the initial load and refreshes — otherwise those
        /// entries disappear from the drives listing.
        /// </summary>
        private static void InjectRootVirtualEntries(ColumnState col)
        {
            // Inject Favorites virtual entry at top (also into AllEntries for ClearSearch)
            var favEntry = new FileEntry
            {
                Name = "Favorites",
                FullPath = null,
                IsDirectory = true,
                IsDrive = false,
                IsVirtual = true
            };
            col.Entries.Insert(0, favEntry);
            col.AllEntries?.Insert(0, favEntry);

            // Inject User Folders virtual entry (always visible at root)
            var portalEntry = new FileEntry
            {
                Name = PortalBrowser.PortalRootName,
                FullPath = null,
                IsDirectory = true,
                IsDrive = false,
                IsVirtual = true,
                IsPortal = true
            };
            col.Entries.Insert(1, portalEntry);
            col.AllEntries?.Insert(1, portalEntry);

            // Inject Network virtual entry (saved locations + add-location flow)
            var networkEntry = new FileEntry
            {
                Name = "Network",
                FullPath = null,
                IsDirectory = true,
                IsDrive = false,
                IsVirtual = true,
                IsNetwork = true
            };
            col.Entries.Insert(2, networkEntry);
            col.AllEntries?.Insert(2, networkEntry);

            // Separator between the virtual group (Favorites, User Folders, Network) and drives
            var separator = new FileEntry
            {
                Name = "",
                FullPath = null,
                IsSeparator = true
            };
            col.Entries.Insert(3, separator);
            col.AllEntries?.Insert(3, separator);

            // Move the AppData entry (LocalFolder) above the separator so it stays
            // grouped with the virtual folders (Favorites, User Folders, Network)
            int appDataIdx = col.Entries.FindIndex(e => e.Name == "AppData");
            if (appDataIdx > 3)
            {
                var appData = col.Entries[appDataIdx];
                col.Entries.RemoveAt(appDataIdx);
                col.Entries.Insert(3, appData);

                if (col.AllEntries != null)
                {
                    int allIdx = col.AllEntries.FindIndex(e => e.Name == "AppData");
                    if (allIdx > 3)
                    {
                        var allApp = col.AllEntries[allIdx];
                        col.AllEntries.RemoveAt(allIdx);
                        col.AllEntries.Insert(3, allApp);
                    }
                }
            }
        }

        /// <summary>
        /// Jump to the portal root entry ("User Folders") and drill in. Used after a
        /// successful portal connection so the user lands directly on the portal folders.
        /// </summary>
        public async Task DrillIntoPortalRootAsync()
        {
            Log.Info("ColumnNavigator: auto drilling into portal root");
            await LoadRootAsync();
            int idx = _current.Entries.FindIndex(e => e.IsPortal);
            if (idx < 0)
            {
                Log.Warn("ColumnNavigator: portal root entry not found — aborting auto drill-in");
                return;
            }
            _current.SelectedIndex = idx;
            await DrillInAsync();
        }

        /// <summary>
        /// Drill into the currently selected item (A/Right button).
        /// If item is directory -> push current to history, load new dir.
        /// If item is archive -> push current to history, open archive as virtual folder.
        /// If item is file -> no-op (files handled by preview).
        /// </summary>
        public async Task DrillInAsync()
        {
            if (_portalBusy)
            {
                Log.Verb("ColumnNavigator: drill-in ignored — portal navigation in progress");
                return;
            }

            ++_previewGeneration;
            var selected = _current.GetSelectedEntry();
            if (selected == null) return;

            // Portal entries (root "User Folders" + all levels). Must be checked before
            // the generic IsVirtual (Favorites) branch — portal entries are virtual too.
            if (_current.IsPortal || selected.IsPortal)
            {
                await DrillIntoPortalAsync(selected);
                return;
            }

            // Network entries (Network root, saved locations, remote shares/trees).
            // Must be checked before the generic IsVirtual branch — the Network root
            // entry and the action rows are virtual too.
            if (_current.IsNetwork || selected.IsNetwork)
            {
                await DrillIntoNetworkAsync(selected);
                return;
            }

            // Handle virtual entries (e.g. Favorites)
            if (selected.IsVirtual)
            {
                await DrillIntoFavoritesAsync();
                return;
            }

            // Handle chiptune sources (multi-track files drill into a track list)
            if (selected.IsChiptune && selected.ChiptuneTrackIndex < 0)
            {
                await DrillIntoChiptuneAsync(selected);
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
                ArchiveInternalPath = "",
                PortalKnownFolder = _current.PortalKnownFolder,
                PortalPackageFullName = _current.PortalPackageFullName,
                PortalPath = _current.PortalPath
            };
            _current.ClearSearch();

            // Update preview: show first item of new current
            await UpdatePreviewAsync();

            ColumnsChanged?.Invoke();
        }

        /// <summary>
        /// Drill into a chiptune source — probe its subsongs and show them as a
        /// virtual track list (parent column | tracks).
        /// </summary>
        private async Task DrillIntoChiptuneAsync(FileEntry chipEntry)
        {
            ++_previewGeneration;
            Log.Info("ColumnNavigator: drilling into chiptune {Path}", chipEntry.FullPath);

            // Push current state to history
            _history.Push(new ColumnState
            {
                Path = _current.Path,
                Label = _current.Label,
                SelectedIndex = _current.SelectedIndex,
                Entries = _current.Entries
            });

            byte[] data = null;
            if (!string.IsNullOrEmpty(chipEntry.ArchiveRootPath))
            {
                // Chiptune lives inside an archive (.rsn → .spc): read its bytes.
                using (var stream = _archiveBrowser.OpenEntryStream(chipEntry.ArchiveRootPath, chipEntry.ArchiveInternalPath))
                {
                    if (stream != null)
                    {
                        using (var ms = new System.IO.MemoryStream())
                        {
                            await stream.CopyToAsync(ms);
                            data = ms.ToArray();
                        }
                    }
                }
            }

            var entries = ChiptuneBrowser.BuildTrackEntries(
                chipEntry.FullPath, data, System.IO.Path.GetExtension(chipEntry.FullPath));

            if (entries.Count <= 1)
            {
                // Single-track chiptune: drilling into a 1-item list is useless.
                // Restore the pushed history and stay on the current column.
                if (_history.Count > 0)
                    _current = _history.Pop();
                Log.Info("ColumnNavigator: single-track chiptune ({Count}) — skipping drill-in for {Path}", entries.Count, chipEntry.FullPath);
                return;
            }

            _current = new ColumnState
            {
                Path = chipEntry.FullPath,
                Label = chipEntry.Name,
                Entries = entries.ToList(),
                IsChiptune = true,
                ArchiveRootPath = chipEntry.ArchiveRootPath,
                ArchiveInternalPath = chipEntry.ArchiveInternalPath,
                PortalKnownFolder = _current.PortalKnownFolder,
                PortalPackageFullName = _current.PortalPackageFullName,
                PortalPath = _current.PortalPath
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
                ArchiveInternalPath = dirEntry.ArchiveInternalPath,
                PortalKnownFolder = _current.PortalKnownFolder,
                PortalPackageFullName = _current.PortalPackageFullName,
                PortalPath = _current.PortalPath
            };
            _current.ClearSearch();

            // Update preview: show first item of new current
            await UpdatePreviewAsync();

            ColumnsChanged?.Invoke();
        }

        /// <summary>
        /// Drill into a portal node. Handles: root → known folders, known folder →
        /// packages (LocalAppData) or root file list (DevelopmentFiles), package → package
        /// root, directory → deeper file list. Portal zip files are cached then opened via
        /// the archive browser. Regular portal files are no-ops (opened via media/editor).
        /// </summary>
        private async Task DrillIntoPortalAsync(FileEntry selected)
        {
            if (!DevicePortalService.IsPortalConnected)
            {
                Log.Warn("ColumnNavigator.Portal: drill-in while portal not connected — setup required");
                PortalSetupRequired?.Invoke();
                return;
            }
            DevicePortalService.ResetAccessDenied();

            // ".." = drill up one level (same as B button): pop history and reload.
            if (selected.Name == "..")
            {
                Log.Info("ColumnNavigator.Portal: drilling up via '..'");
                await DrillOutAsync();
                return;
            }

            if (!selected.IsDirectory)
            {
                if (selected.IsArchive)
                    await DrillIntoPortalArchiveAsync(selected);
                return;
            }

            ++_previewGeneration;
            Log.Info("ColumnNavigator.Portal: drilling into {Name}", selected.Name);

            // Clear any stale gamelist — portal columns never use it.
            _gamelistCache = null;
            _gamelistDirectory = null;

            var newColumn = new ColumnState { Path = null, Label = selected.Name, IsPortal = true };

            LoadingChanged?.Invoke(true);
            _portalBusy = true;
            try
            {
                if (selected.PortalKnownFolder == null)
                {
                    await newColumn.LoadPortalKnownFoldersAsync(_portalBrowser);
                }
                else if (selected.PortalPackageFullName == null && selected.PortalKnownFolder == "LocalAppData")
                {
                    newColumn.PortalKnownFolder = "LocalAppData";
                    await newColumn.LoadPortalPackagesAsync(_portalBrowser);
                }
                else
                {
                    newColumn.PortalKnownFolder = selected.PortalKnownFolder;
                    newColumn.PortalPackageFullName = selected.PortalPackageFullName ?? "";
                    // Known-folder and package entries are addressed by query params (no path);
                    // everything deeper is addressed by portal path.
                    bool isParamRoot = selected.PortalPackageFullName == null || selected.PortalPath == null;
                    newColumn.PortalPath = isParamRoot
                        ? "\\"
                        : PortalBrowser.CombinePortalPath(selected.PortalPath, selected.Name);
                    await newColumn.LoadPortalDirectoryAsync(_portalBrowser,
                        newColumn.PortalKnownFolder, newColumn.PortalPackageFullName, newColumn.PortalPath);
                }
            }
            catch (Exception ex)
            {
                // Some packages can't be browsed (no accessible LocalAppData, 404/timeout).
                // Surface the failure in the preview instead of navigating into nothing.
                Log.Warn("ColumnNavigator.Portal: failed to list {Name}: {Message}", selected.Name, ex.Message);
                _preview = new ColumnState
                {
                    Path = null,
                    Label = selected.Name,
                    IsFilePreview = true,
                    IsPortal = true,
                    PreviewType = FilePreviewType.Error,
                    PreviewFileType = "Portal entry",
                    PreviewErrorMessage = "Could not list this entry — " + ex.Message
                };
                ColumnsChanged?.Invoke();
                return;
            }
            finally
            {
                _portalBusy = false;
                LoadingChanged?.Invoke(false);
            }

            // Push the previous column only after the load succeeded, and preserve its
            // portal metadata so DrillOut restores a correctly-flagged portal column.
            _history.Push(new ColumnState
            {
                Path = _current.Path,
                Label = _current.Label,
                SelectedIndex = _current.SelectedIndex,
                Entries = _current.Entries,
                IsPortal = _current.IsPortal,
                PortalKnownFolder = _current.PortalKnownFolder,
                PortalPackageFullName = _current.PortalPackageFullName,
                PortalPath = _current.PortalPath
            });

            newColumn.ClearSearch();
            _current = newColumn;

            await UpdatePreviewAsync();
            ColumnsChanged?.Invoke();
        }

        /// <summary>
        /// Drill into a portal zip: ensure it is cached (explicit progress for large files),
        /// then open the cached copy with the archive browser.
        /// </summary>
        private async Task DrillIntoPortalArchiveAsync(FileEntry archiveEntry)
        {
            ++_previewGeneration;
            Log.Info("ColumnNavigator.Portal: drilling into portal archive {Name}", archiveEntry.Name);

            IProgress<double> progress = DownloadProgressFactory?.Invoke(archiveEntry.Name);
            _portalBusy = true;
            string cachePath;
            try
            {
                cachePath = await PortalCache.EnsureAsync(PortalBrowser.ToPortalEntry(archiveEntry), progress);
            }
            finally
            {
                _portalBusy = false;
            }
            if (cachePath == null)
            {
                Log.Warn("ColumnNavigator.Portal: archive download to cache failed for {Name}", archiveEntry.Name);
                return;
            }

            _history.Push(new ColumnState
            {
                Path = _current.Path,
                Label = _current.Label,
                SelectedIndex = _current.SelectedIndex,
                Entries = _current.Entries,
                IsPortal = _current.IsPortal,
                PortalKnownFolder = _current.PortalKnownFolder,
                PortalPackageFullName = _current.PortalPackageFullName,
                PortalPath = _current.PortalPath
            });

            var entries = _archiveBrowser.ListEntries(cachePath, "");
            _current = new ColumnState
            {
                Path = cachePath,
                Label = archiveEntry.Name,
                Entries = entries.ToList(),
                IsArchive = true,
                ArchiveRootPath = cachePath,
                ArchiveInternalPath = "",
                PortalKnownFolder = archiveEntry.PortalKnownFolder,
                PortalPackageFullName = archiveEntry.PortalPackageFullName,
                PortalPath = archiveEntry.PortalPath
            };
            _current.ClearSearch();

            await UpdatePreviewAsync();
            ColumnsChanged?.Invoke();
        }

        // ====================================================================
        // Network (SMB) navigation
        // ====================================================================

        /// <summary>
        /// Drills into a network entry. Handles every level of the network tree:
        /// Network root (→ locations), saved location (→ shares or configured share),
        /// share (→ share root), remote directory (→ drill down).
        /// </summary>
        private async Task DrillIntoNetworkAsync(FileEntry selected)
        {
            if (_networkBusy)
            {
                Log.Verb("ColumnNavigator.Network: drill-in ignored — network navigation in progress");
                return;
            }

            ++_previewGeneration;

            // ".." — go back one level (same as B).
            if (selected.Name == "..")
            {
                Log.Info("ColumnNavigator.Network: drilling up via '..'");
                await DrillOutAsync();
                return;
            }

            // Action row (＋ Add location) — handled outside the tree walk.
            if (selected.ActionKind != ActionKind.None)
            {
                await DrillIntoNetworkActionAsync(selected);
                return;
            }

            // 1. Network root virtual entry (at the drive root) → locations column.
            if (selected.IsVirtual && !_current.IsNetwork)
            {
                Log.Info("ColumnNavigator.Network: entering Network root");
                var state = new ColumnState
                {
                    Path = null,
                    Label = "Network",
                    IsNetwork = true,
                    NetworkLocationId = 0,
                    NetworkShareName = null,
                    NetworkPath = null
                };
                state.LoadNetworkLocations(await BuildNetworkLocationsAsync());
                await CommitNetworkColumnAsync(state);
                return;
            }

            // 2. Saved location row → shares (no share configured) or straight into the share.
            if (_current.IsNetwork && _current.NetworkLocationId == 0 && _current.NetworkShareName == null
                && !selected.IsVirtual)
            {
                var config = await GetNetworkConfigAsync(selected.NetworkLocationId);
                if (config == null) return;

                Log.Info("ColumnNavigator.Network: entering location {Url}", NetworkUrl.Compose(config));

                ColumnState newColumn = string.IsNullOrEmpty(config.Share)
                    ? new ColumnState
                    {
                        Path = null,
                        Label = NetworkUrl.DisplayName(config),
                        IsNetwork = true,
                        NetworkLocationId = config.Id,
                        NetworkShareName = null,
                        NetworkPath = null
                    }
                    : new ColumnState
                    {
                        Path = null,
                        Label = config.Share,
                        IsNetwork = true,
                        NetworkLocationId = config.Id,
                        NetworkShareName = config.Share,
                        NetworkPath = ""
                    };

                if (!await LoadNetworkColumnAsync(newColumn, config, config.Share, "")) return;
                await CommitNetworkColumnAsync(newColumn);
                return;
            }

            // 3. Shares column → selected share root.
            if (_current.NetworkShareName == null && selected.NetworkShareName != null)
            {
                var config = await GetNetworkConfigAsync(_current.NetworkLocationId);
                if (config == null) return;

                var newColumn = new ColumnState
                {
                    Path = null,
                    Label = selected.NetworkShareName,
                    IsNetwork = true,
                    NetworkLocationId = _current.NetworkLocationId,
                    NetworkShareName = selected.NetworkShareName,
                    NetworkPath = ""
                };

                if (!await LoadNetworkColumnAsync(newColumn, config, selected.NetworkShareName, "")) return;
                await CommitNetworkColumnAsync(newColumn);
                return;
            }

            // 4. Remote directory → drill down one level.
            if (_current.NetworkShareName != null && selected.IsDirectory)
            {
                var config = await GetNetworkConfigAsync(_current.NetworkLocationId);
                if (config == null) return;

                string childPath = CombineNetworkPath(_current.NetworkPath, selected.Name);
                var newColumn = new ColumnState
                {
                    Path = null,
                    Label = string.IsNullOrEmpty(childPath)
                        ? _current.NetworkShareName
                        : _current.NetworkShareName + "\\" + childPath,
                    IsNetwork = true,
                    NetworkLocationId = _current.NetworkLocationId,
                    NetworkShareName = _current.NetworkShareName,
                    NetworkPath = childPath
                };

                if (!await LoadNetworkColumnAsync(newColumn, config, _current.NetworkShareName, childPath)) return;
                await CommitNetworkColumnAsync(newColumn);
                return;
            }

            // Remote file — no navigation (preview/play lands in M5).
            Log.Verb("ColumnNavigator.Network: file '{Name}' — no drill-in (preview comes in a later milestone)", selected.Name);
        }

        private async Task DrillIntoNetworkActionAsync(FileEntry selected)
        {
            switch (selected.ActionKind)
            {
                case ActionKind.AddLocation:
                    Log.Info("ColumnNavigator.Network: add location requested");
                    NetworkAddLocationRequested?.Invoke();
                    break;
                case ActionKind.DownloadUrl:
                    Log.Info("ColumnNavigator.Network: download-from-URL requested");
                    NetworkDownloadUrlRequested?.Invoke();
                    break;
            }
            await Task.CompletedTask;
        }

        /// <summary>Builds the locations column entry list (saved locations + action rows).</summary>
        private async Task<List<FileEntry>> BuildNetworkLocationsAsync()
        {
            var list = new List<FileEntry>();

            // Action rows first: Download from URL, separator, Add location,
            // separator, then the saved locations underneath.
            list.Add(new FileEntry
            {
                Name = "Download from URL",
                IsDirectory = true,
                IsVirtual = true,
                IsNetwork = true,
                ActionKind = ActionKind.DownloadUrl
            });

            list.Add(new FileEntry
            {
                Name = "",
                FullPath = null,
                IsSeparator = true
            });

            list.Add(new FileEntry
            {
                Name = "Add location",
                IsDirectory = true,
                IsVirtual = true,
                IsNetwork = true,
                ActionKind = ActionKind.AddLocation
            });

            list.Add(new FileEntry
            {
                Name = "",
                FullPath = null,
                IsSeparator = true
            });

            var configs = await NetworkServerManager.GetAllAsync();
            foreach (var c in configs)
            {
                list.Add(new FileEntry
                {
                    Name = NetworkUrl.DisplayName(c),
                    IsDirectory = true,
                    IsNetwork = true,
                    NetworkLocationId = c.Id,
                    NetworkShareName = null,
                    NetworkPath = null
                });
            }

            return list;
        }

        /// <summary>Fetches a saved location config; raises the error event when missing.</summary>
        public async Task<NetworkServerConfig> GetNetworkConfigAsync(long locationId)
        {
            var config = await NetworkServerManager.GetAsync((int)locationId);
            if (config == null)
            {
                Log.Warn("ColumnNavigator.Network: location id={Id} not found", locationId);
                NetworkError?.Invoke(NetworkOperationReason.Unreachable, "Saved location not found.");
            }
            return config;
        }

        /// <summary>Opens a remote file stream for the given saved location. Returns null when the
        /// location is missing or the read cannot be opened.</summary>
        public async Task<Stream> OpenNetworkStreamAsync(long locationId, string share, string path)
        {
            var config = await GetNetworkConfigAsync(locationId);
            if (config == null) return null;
            try
            {
                return await _networkBrowser.OpenReadAsync(config, share, path, CancellationToken.None);
            }
            catch (NetworkOperationException ex)
            {
                Log.Warn("ColumnNavigator.Network: open stream failed {Url}: {Reason} ({Detail})",
                    NetworkUrl.Compose(config), ex.Reason, ex.Message);
                NetworkError?.Invoke(ex.Reason, NetworkOperationException.FriendlyMessage(ex.Reason, ex.Message));
                return null;
            }
        }

        /// <summary>
        /// Uploads a local file to a remote path over SMB (text-editor save-back).
        /// Returns false (logged) on any failure.
        /// </summary>
        public async Task<bool> WriteNetworkFileAsync(long locationId, string share, string path, string localPath)
        {
            var config = await GetNetworkConfigAsync(locationId);
            if (config == null) return false;
            try
            {
                await _networkBrowser.WriteFileAsync(config, share, path, localPath, CancellationToken.None);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn("ColumnNavigator.WriteNetworkFile: {Reason}", ex.Message);
                return false;
            }
        }

        /// <summary>Loads shares or a directory into the column. Returns false on network failure.</summary>
        private async Task<bool> LoadNetworkColumnAsync(ColumnState column, NetworkServerConfig config, string share, string path)
        {
            LoadingChanged?.Invoke(true);
            _networkBusy = true;
            try
            {
                if (string.IsNullOrEmpty(share))
                    await column.LoadNetworkSharesAsync(_networkBrowser, config, CancellationToken.None);
                else
                    await column.LoadNetworkDirectoryAsync(_networkBrowser, config, share, path, CancellationToken.None);
                return true;
            }
            catch (NetworkOperationException ex)
            {
                Log.Warn("ColumnNavigator.Network: load failed {Url}: {Reason} ({Detail})",
                    NetworkUrl.Compose(config), ex.Reason, ex.Message);
                NetworkError?.Invoke(ex.Reason, NetworkOperationException.FriendlyMessage(ex.Reason, ex.Message));
                return false;
            }
            catch (Exception ex)
            {
                Log.Err("ColumnNavigator.Network: unexpected load failure {Url}", ex, NetworkUrl.Compose(config));
                NetworkError?.Invoke(NetworkOperationReason.Unreachable, "Unexpected network error.");
                return false;
            }
            finally
            {
                _networkBusy = false;
                LoadingChanged?.Invoke(false);
            }
        }

        /// <summary>Pushes the current column and makes the new one current.</summary>
        private async Task CommitNetworkColumnAsync(ColumnState newColumn)
        {
            _history.Push(new ColumnState
            {
                Path = _current.Path,
                Label = _current.Label,
                SelectedIndex = _current.SelectedIndex,
                Entries = _current.Entries,
                AllEntries = _current.AllEntries,
                IsNetwork = _current.IsNetwork,
                NetworkLocationId = _current.NetworkLocationId,
                NetworkShareName = _current.NetworkShareName,
                NetworkPath = _current.NetworkPath
            });

            newColumn.ClearSearch();
            _current = newColumn;
            await UpdatePreviewAsync();
            ColumnsChanged?.Invoke();
        }

        /// <summary>Reloads the given network column (locations, shares, or directory) in place.</summary>
        private async Task ReloadNetworkColumnAsync(ColumnState column)
        {
            if (_networkBusy)
            {
                Log.Verb("ColumnNavigator.Network: reload ignored — network navigation in progress");
                return;
            }
            _networkBusy = true;
            try
            {
                if (column.NetworkLocationId == 0 && column.NetworkShareName == null)
                {
                    column.LoadNetworkLocations(await BuildNetworkLocationsAsync());
                }
                else
                {
                    var config = await NetworkServerManager.GetAsync((int)column.NetworkLocationId);
                    if (config == null) return;
                    if (column.NetworkShareName == null)
                        await column.LoadNetworkSharesAsync(_networkBrowser, config, CancellationToken.None);
                    else
                        await column.LoadNetworkDirectoryAsync(_networkBrowser, config, column.NetworkShareName,
                            column.NetworkPath ?? "", CancellationToken.None);
                }
            }
            catch (NetworkOperationException ex)
            {
                Log.Warn("ColumnNavigator.Network: reload failed: {Reason} ({Detail})", ex.Reason, ex.Message);
                NetworkError?.Invoke(ex.Reason, NetworkOperationException.FriendlyMessage(ex.Reason, ex.Message));
            }
            finally
            {
                _networkBusy = false;
            }
        }

        /// <summary>Joins a remote path segment onto a remote path.</summary>
        public static string CombineNetworkPath(string path, string name)
        {
            if (string.IsNullOrEmpty(path)) return name;
            return path.TrimEnd('\\') + "\\" + name;
        }

        /// <summary>
        /// Drill out / go back (B/Left button).
        /// Pop history, restore previous state.
        /// </summary>
        public async Task DrillOutAsync()
        {
            if (_portalBusy)
            {
                Log.Verb("ColumnNavigator: drill-out ignored — portal navigation in progress");
                return;
            }

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

            // If returning to a portal column, reload from the API
            if (_current.IsPortal)
            {
                _portalBusy = true;
                try
                {
                    await ReloadPortalColumnAsync(_current);
                }
                finally
                {
                    _portalBusy = false;
                }
            }

            // If returning to a network column, reload (locations may have changed after
            // add/rename/delete; remote listings are reloaded for freshness).
            if (_current.IsNetwork)
            {
                await ReloadNetworkColumnAsync(_current);
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
                PreviewLoadingChanged?.Invoke(false);
                return;
            }

            // Favorites column (root level): no folder preview — the UI shows the
            // how-to guide instead. After drilling into an actual favorite, IsFavorite
            // is false and the normal preview path takes over.
            if (_current.IsFavorite)
            {
                _preview = null;
                PreviewLoadingChanged?.Invoke(false);
                return;
            }

            if (_current.IsPortal)
            {
                await UpdatePortalPreviewAsync(selected, gen);
                return;
            }

            // Network column: locations column shows the how-to guide; share/directory
            // columns preview their children; remote files show a metadata card (the
            // real preview/play pipeline lands in a later milestone).
            if (_current.IsNetwork)
            {
                await UpdateNetworkPreviewAsync(selected, gen);
                return;
            }

            // Non-portal preview: ensure any stale portal spinner is cleared.
            PreviewLoadingChanged?.Invoke(false);

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
                    // Chiptune subsong entry: route straight to the audio preview,
                    // addressing the source + track so the media control can decode it.
                    if (selected.IsChiptune && selected.ChiptuneTrackIndex >= 0)
                    {
                        string source = selected.ChiptuneSourcePath ?? selected.FullPath;
                        _preview = new ColumnState
                        {
                            Path = selected.FullPath,
                            Label = selected.Name,
                            IsFilePreview = true,
                            PreviewType = FilePreviewType.Audio,
                            PreviewFilePath = source,
                            PreviewFileType = $"Track {selected.ChiptuneTrackIndex + 1}",
                            PreviewFileSize = selected.SizeBytes,
                            PreviewChiptuneTrack = selected.ChiptuneTrackIndex,
                            PreviewChiptuneSource = source
                        };
                        return;
                    }

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
        /// Preview logic for network columns. The locations column shows the how-to
        /// guide (no preview). Shares/directories list their contents; remote files
        /// show a metadata card — the real stream preview/play lands in a later
        /// milestone. No gamelist enrichment on network paths.
        /// </summary>
        private async Task UpdateNetworkPreviewAsync(FileEntry selected, long gen)
        {
            // Locations column: no folder preview — the UI shows the how-to guide.
            if (_current.NetworkLocationId == 0 && _current.NetworkShareName == null)
            {
                _preview = null;
                PreviewLoadingChanged?.Invoke(false);
                return;
            }

            var config = await NetworkServerManager.GetAsync((int)_current.NetworkLocationId);
            if (config == null)
            {
                _preview = null;
                PreviewLoadingChanged?.Invoke(false);
                return;
            }

            PreviewLoadingChanged?.Invoke(true);
            try
            {
                if (selected.IsDirectory)
                {
                    if (selected.Name == "..")
                    {
                        _preview = null;
                        return;
                    }

                    _preview = new ColumnState
                    {
                        Path = null,
                        Label = selected.Name,
                        IsNetwork = true,
                        NetworkLocationId = config.Id,
                        NetworkShareName = selected.NetworkShareName ?? _current.NetworkShareName,
                        NetworkPath = selected.NetworkPath
                    };

                    try
                    {
                        if (_preview.NetworkShareName == null)
                            await _preview.LoadNetworkSharesAsync(_networkBrowser, config, CancellationToken.None);
                        else
                            await _preview.LoadNetworkDirectoryAsync(_networkBrowser, config,
                                _preview.NetworkShareName, _preview.NetworkPath ?? "", CancellationToken.None);
                    }
                    catch (NetworkOperationException ex)
                    {
                        Log.Warn("ColumnNavigator.Network: preview list failed '{Name}': {Reason}",
                            selected.Name, ex.Reason);
                        _preview.IsFilePreview = true;
                        _preview.PreviewType = FilePreviewType.Error;
                        _preview.PreviewFileType = "Network";
                        _preview.PreviewErrorMessage = NetworkOperationException.FriendlyMessage(ex.Reason, ex.Message);
                    }
                    if (_previewGeneration != gen) return;
                }
                else
                {
                    string share = selected.NetworkShareName ?? _current.NetworkShareName;
                    string path = selected.NetworkPath;

                    if (string.IsNullOrEmpty(share) || string.IsNullOrEmpty(path))
                    {
                        _preview = new ColumnState
                        {
                            Path = null,
                            Label = selected.Name,
                            IsFilePreview = true,
                            IsNetwork = true,
                            NetworkLocationId = config.Id,
                            NetworkShareName = share,
                            NetworkPath = path,
                            PreviewFilePath = selected.Name,
                            PreviewType = FilePreviewType.Unsupported,
                            PreviewFileType = "Network file",
                            PreviewFileSize = selected.SizeBytes,
                            PreviewTextContent = "Remote file — no path context to read it."
                        };
                        return;
                    }

                    FilePreviewResult previewResult;

                    string previewExt = Path.GetExtension(selected.Name ?? "");
                    if (FilePreviewService.IsAudioFile(previewExt) || FilePreviewService.IsVideoFile(previewExt))
                    {
                        // Audio/video stream inline into the preview player — no content
                        // probe needed (the pane's inline player opens its own stream).
                        bool previewIsAudio = FilePreviewService.IsAudioFile(previewExt);
                        _preview = new ColumnState
                        {
                            Path = null,
                            Label = selected.Name,
                            IsFilePreview = true,
                            IsNetwork = true,
                            NetworkLocationId = config.Id,
                            NetworkShareName = share,
                            NetworkPath = path,
                            PreviewFilePath = selected.Name,
                            PreviewType = previewIsAudio ? FilePreviewType.Audio : FilePreviewType.Video,
                            PreviewFileType = FilePreviewService.GetFileTypeLabel(previewExt, null),
                            PreviewFileSize = selected.SizeBytes
                        };
                        return;
                    }

                    try
                    {
                        using (var stream = await _networkBrowser.OpenReadAsync(
                            config, share, path, CancellationToken.None))
                        {
                            previewResult = await FilePreviewService.GetPreviewFromNetworkAsync(
                                stream, selected.Name, selected.SizeBytes);
                        }
                    }
                    catch (NetworkOperationException ex)
                    {
                        Log.Warn("ColumnNavigator.Network: preview read failed '{Name}': {Reason} ({Detail})",
                            selected.Name, ex.Reason, ex.Message);
                        NetworkError?.Invoke(ex.Reason,
                            NetworkOperationException.FriendlyMessage(ex.Reason, ex.Message));
                        _preview = null;
                        return;
                    }
                    catch (Exception ex)
                    {
                        Log.Err("ColumnNavigator.Network: unexpected preview failure '{Name}'", ex, selected.Name);
                        NetworkError?.Invoke(NetworkOperationReason.Unreachable,
                            "Unexpected network error while reading the file.");
                        _preview = null;
                        return;
                    }

                    if (_previewGeneration != gen) return;

                    _preview = new ColumnState
                    {
                        Path = null,
                        Label = selected.Name,
                        IsFilePreview = true,
                        IsNetwork = true,
                        NetworkLocationId = config.Id,
                        NetworkShareName = share,
                        NetworkPath = path,
                        PreviewFilePath = selected.Name,
                        PreviewType = previewResult.Type,
                        PreviewTextContent = previewResult.TextContent,
                        PreviewImageSource = previewResult.ImageSource,
                        PreviewErrorMessage = previewResult.ErrorMessage,
                        PreviewFileType = previewResult.FileType,
                        PreviewFileSize = previewResult.FileSizeBytes,
                        PreviewIsTruncated = previewResult.IsTruncated,
                        PreviewPixelWidth = previewResult.PixelWidth,
                        PreviewPixelHeight = previewResult.PixelHeight,
                        PreviewPdfPageCount = previewResult.PdfPageCount,
                        PreviewRomSystem = previewResult.RomSystem,
                        PreviewRomIconPath = previewResult.RomIconPath
                    };

                    // PDF keeps a metadata card (rendering is path-based; the
                    // fullscreen open caches the file locally first).
                    if (previewResult.Type == FilePreviewType.Pdf)
                    {
                        _preview.PreviewType = FilePreviewType.Unsupported;
                        _preview.PreviewTextContent =
                            $"Press A to open \"{selected.Name}\" (PDF is cached locally first).";
                    }
                    else if (previewResult.Type == FilePreviewType.Unsupported)
                    {
                        _preview.PreviewTextContent = "No preview for this file type. Press A for more options.";
                    }
                }
            }
            finally
            {
                if (_previewGeneration == gen)
                    PreviewLoadingChanged?.Invoke(false);
            }
        }

        /// <summary>
        /// Preview logic for portal columns. Directories list their contents (same levels
        /// as drill-in); files auto-cache previews when ≤ 25 MB, otherwise show a metadata
        /// card without downloading. No gamelist enrichment on portal paths.
        /// </summary>
        private async Task UpdatePortalPreviewAsync(FileEntry selected, long gen)
        {
            PreviewLoadingChanged?.Invoke(true);
            try
            {
                await UpdatePortalPreviewCoreAsync(selected, gen);
            }
            finally
            {
                // Only clear the spinner if this generation is still the active one —
                // a newer preview may have taken over and raised its own 'true'.
                if (_previewGeneration == gen)
                    PreviewLoadingChanged?.Invoke(false);
            }
        }

        private async Task UpdatePortalPreviewCoreAsync(FileEntry selected, long gen)
        {
            if (selected.IsDirectory)
            {
                if (selected.Name == "..")
                {
                    _preview = null;
                    return;
                }

                _preview = new ColumnState { Path = null, Label = selected.Name, IsPortal = true };

                try
                {
                    if (selected.PortalKnownFolder == null)
                    {
                        await _preview.LoadPortalKnownFoldersAsync(_portalBrowser);
                    }
                    else if (selected.PortalPackageFullName == null && selected.PortalKnownFolder == "LocalAppData")
                    {
                        _preview.PortalKnownFolder = "LocalAppData";
                        await _preview.LoadPortalPackagesAsync(_portalBrowser);
                    }
                    else
                    {
                        _preview.PortalKnownFolder = selected.PortalKnownFolder;
                        _preview.PortalPackageFullName = selected.PortalPackageFullName ?? "";
                        bool isParamRoot = selected.PortalPackageFullName == null || selected.PortalPath == null;
                        _preview.PortalPath = isParamRoot
                            ? "\\"
                            : PortalBrowser.CombinePortalPath(selected.PortalPath, selected.Name);
                        await _preview.LoadPortalDirectoryAsync(_portalBrowser,
                            _preview.PortalKnownFolder, _preview.PortalPackageFullName, _preview.PortalPath);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn("ColumnNavigator.Portal: preview list failed for {Name}: {Message}", selected.Name, ex.Message);
                    _preview.IsFilePreview = true;
                    _preview.PreviewType = FilePreviewType.Error;
                    _preview.PreviewFileType = "Portal entry";
                    _preview.PreviewErrorMessage = "Could not list this entry — " + ex.Message;
                }

                if (_previewGeneration != gen) return;
            }
            else
            {
                _preview = new ColumnState
                {
                    Path = null,
                    Label = selected.Name,
                    IsFilePreview = true,
                    IsPortal = true,
                    PortalKnownFolder = selected.PortalKnownFolder,
                    PortalPackageFullName = selected.PortalPackageFullName ?? "",
                    PortalPath = selected.PortalPath
                };

                if (selected.SizeBytes <= PortalCache.AutoPreviewMaxBytes)
                {
                    string cachePath;
                    try
                    {
                        cachePath = await PortalCache.EnsureAsync(PortalBrowser.ToPortalEntry(selected), null);
                    }
                    catch (Exception ex)
                    {
                        Log.Warn("ColumnNavigator.Portal: preview download failed for {Name}: {Message}", selected.Name, ex.Message);
                        _preview.PreviewType = FilePreviewType.Error;
                        _preview.PreviewFileType = "Portal entry";
                        _preview.PreviewErrorMessage = "Portal download failed: " + ex.Message;
                        return;
                    }

                    if (_previewGeneration != gen) return;
                    if (cachePath == null)
                    {
                        _preview.PreviewType = FilePreviewType.Error;
                        _preview.PreviewFileType = "Portal entry";
                        _preview.PreviewErrorMessage = "Portal download failed (see log)";
                        return;
                    }

                    var previewResult = await FilePreviewService.GetPreviewAsync(cachePath);
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
                    _preview.PreviewFilePath = cachePath;
                    _preview.PreviewPdfPageCount = previewResult.PdfPageCount;
                    _preview.PreviewRomSystem = previewResult.RomSystem;
                    _preview.PreviewRomIconPath = previewResult.RomIconPath;
                }
                else
                {
                    // Large file — metadata card, download only when opened.
                    _preview.PreviewType = FilePreviewType.Unsupported;
                    _preview.PreviewFileType = "Portal file";
                    _preview.PreviewFileSize = selected.SizeBytes;
                    _preview.PreviewTextContent = $"Large portal file ({FormatSizeHuman(selected.SizeBytes)}) — open (A) to download and view.";
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
        /// The MillerColumnsPage 90ms debounce already settles rapid scrolling; the scan
        /// runs on a background thread and is cancelled when a newer selection arrives,
        /// so no additional settle delay is needed here.
        /// </summary>
        public async Task OnSelectionChangedAsync()
        {
            _previewCts?.Cancel();
            _previewCts?.Dispose();
            _previewCts = new CancellationTokenSource();
            var token = _previewCts.Token;

            try
            {
                await UpdatePreviewAsync();
            }
            catch (OperationCanceledException)
            {
                return;
            }

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
                else if (_current.IsChiptune)
                {
                    // Virtual chiptune track-list column: the "directory" is really a
                    // chiptune file, so re-probe it and rebuild the track entries
                    // instead of scanning the file path as a directory.
                    byte[] chipData = null;
                    if (!string.IsNullOrEmpty(_current.ArchiveRootPath))
                    {
                        using (var stream = _archiveBrowser.OpenEntryStream(
                            _current.ArchiveRootPath, _current.ArchiveInternalPath))
                        {
                            if (stream != null)
                            {
                                using (var ms = new System.IO.MemoryStream())
                                {
                                    await stream.CopyToAsync(ms);
                                    chipData = ms.ToArray();
                                }
                            }
                        }
                    }
                    var trackEntries = ChiptuneBrowser.BuildTrackEntries(
                        _current.Path, chipData, System.IO.Path.GetExtension(_current.Path));
                    if (trackEntries.Count > 0)
                        _current.Entries = trackEntries.ToList();
                }
                else if (_current.IsPortal)
                {
                    _portalBusy = true;
                    try
                    {
                        await ReloadPortalColumnAsync(_current);
                    }
                    finally
                    {
                        _portalBusy = false;
                    }
                }
                else if (_current.IsNetwork)
                {
                    await ReloadNetworkColumnAsync(_current);
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

            // Root column: re-inject virtual entries (Favorites, User Folders, Network)
            // that a plain drive scan does not include.
            if (_current.Path == null && !_current.IsArchive && !_current.IsPortal && !_current.IsNetwork)
                InjectRootVirtualEntries(_current);

            // Try to preserve selection
            if (prevName != null)
            {
                int idx = _current.Entries.FindIndex(e => e.Name == prevName);
                _current.SelectedIndex = idx >= 0 ? idx : Math.Min(prevIndex, _current.Entries.Count - 1);
            }

            ColumnsChanged?.Invoke();

            // Recompute the preview for the preserved selection. The selection-changed
            // handler is suppressed during rebind (_updating), so without this the preview
            // column would keep showing the pre-refresh entry (e.g. a deleted file).
            await OnSelectionChangedAsync();
        }

        /// <summary>
        /// Reloads a portal column from the API, preserving level semantics
        /// (known folders / packages / file list).
        /// </summary>
        private async Task ReloadPortalColumnAsync(ColumnState col)
        {
            if (col.PortalKnownFolder == null)
            {
                await col.LoadPortalKnownFoldersAsync(_portalBrowser);
            }
            else if (col.PortalPackageFullName == null && col.PortalKnownFolder == "LocalAppData")
            {
                await col.LoadPortalPackagesAsync(_portalBrowser);
            }
            else
            {
                await col.LoadPortalDirectoryAsync(_portalBrowser,
                    col.PortalKnownFolder, col.PortalPackageFullName, col.PortalPath ?? "\\");
            }
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
        public bool IsChiptune { get; set; }
        public bool IsFavorite { get; set; }
        public bool IsPortal { get; set; }
        public bool IsNetwork { get; set; }
        public long NetworkLocationId { get; set; }
        public string NetworkShareName { get; set; }
        public string NetworkPath { get; set; }
        public string PortalKnownFolder { get; set; }
        public string PortalPackageFullName { get; set; }
        public string PortalPath { get; set; }
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

        // Chiptune subsong preview: PreviewChiptuneTrack >= 0 means the current
        // selection is a subsong of PreviewChiptuneSource (file or "archive|internal").
        public int PreviewChiptuneTrack { get; set; } = -1;
        public string PreviewChiptuneSource { get; set; }

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

        public async Task LoadPortalKnownFoldersAsync(PortalBrowser portalBrowser)
        {
            SetPortalEntries(await portalBrowser.ListKnownFoldersAsync(), null, null, null);
        }

        public async Task LoadPortalPackagesAsync(PortalBrowser portalBrowser)
        {
            SetPortalEntries(await portalBrowser.ListPackagesAsync(), null, null, null);
        }

        public async Task LoadPortalDirectoryAsync(PortalBrowser portalBrowser, string knownFolder, string packageFullName, string portalPath)
        {
            SetPortalEntries(await portalBrowser.ListDirectoryAsync(knownFolder, packageFullName, portalPath),
                knownFolder, packageFullName ?? "", portalPath ?? "\\");
        }

        private void SetPortalEntries(List<FileEntry> entries, string knownFolder, string packageFullName, string portalPath)
        {
            var list = new List<FileEntry>(entries.Count + 1);
            list.Add(new FileEntry
            {
                Name = "..",
                FullPath = null,
                IsDirectory = true,
                IsVirtual = true,
                IsPortal = true,
                // Carry the folder's portal context so ".." is treated as the current
                // folder (not a root container) for file operations (paste/new-folder).
                PortalKnownFolder = knownFolder,
                PortalPackageFullName = packageFullName ?? "",
                PortalPath = portalPath
            });
            list.AddRange(entries);

            _allEntries = list;
            AllEntries = _allEntries;
            Entries = new List<FileEntry>(_allEntries);
            if (SelectedIndex == 0 && Entries.Count > 0)
                SelectedIndex = 0;
        }

        /// <summary>Loads the Network locations column (saved locations + action rows).
        /// The entry list is built by the caller (ColumnNavigator.BuildNetworkLocationsAsync).</summary>
        public void LoadNetworkLocations(List<FileEntry> entries)
        {
            var list = new List<FileEntry>(entries.Count + 1);
            list.Add(new FileEntry
            {
                Name = "..",
                FullPath = null,
                IsDirectory = true,
                IsVirtual = true,
                IsNetwork = true
            });
            list.AddRange(entries);

            _allEntries = list;
            AllEntries = _allEntries;
            Entries = new List<FileEntry>(_allEntries);
            if (SelectedIndex == 0 && Entries.Count > 0)
                SelectedIndex = 0;
        }

        public async Task LoadNetworkSharesAsync(SmbBrowser browser, NetworkServerConfig config,
            CancellationToken token)
        {
            SetNetworkEntries((await browser.ListSharesAsync(config, token))
                .Select(s => new FileEntry
                {
                    Name = s,
                    IsDirectory = true,
                    IsNetwork = true,
                    NetworkLocationId = config.Id,
                    NetworkShareName = s,
                    NetworkPath = ""
                }).ToList());
        }

        public async Task LoadNetworkDirectoryAsync(SmbBrowser browser, NetworkServerConfig config,
            string share, string path, CancellationToken token)
        {
            SetNetworkEntries((await browser.ListDirectoryAsync(config, share, path, token))
                .Select(f => new FileEntry
                {
                    Name = f.Name,
                    IsDirectory = f.IsDirectory,
                    IsNetwork = true,
                    NetworkLocationId = config.Id,
                    NetworkShareName = share,
                    NetworkPath = ColumnNavigator.CombineNetworkPath(path, f.Name),
                    SizeBytes = f.IsDirectory ? 0 : f.Size,
                    LastModified = f.LastWriteTime
                }).ToList());
        }

        private void SetNetworkEntries(List<FileEntry> entries)
        {
            var list = new List<FileEntry>(entries.Count + 1);
            list.Add(new FileEntry
            {
                Name = "..",
                FullPath = null,
                IsDirectory = true,
                IsVirtual = true,
                IsNetwork = true,
                NetworkLocationId = NetworkLocationId,
                NetworkShareName = NetworkShareName,
                NetworkPath = NetworkPath
            });
            list.AddRange(entries);

            _allEntries = list;
            AllEntries = _allEntries;
            Entries = new List<FileEntry>(_allEntries);
            if (SelectedIndex == 0 && Entries.Count > 0)
                SelectedIndex = 0;
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
            {
                var e = Entries[SelectedIndex];
                if (e != null && !e.IsSeparator)
                    return e;
            }
            return null;
        }
    }
}
