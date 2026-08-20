using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Data.Json;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.System;
using Windows.System.Display;
using XFiles.Audio;
using XFiles.FileSystem;
using XFiles.Metadata;
using XFiles.Navigation;
using XFiles.Network;
using XFiles.Services;
using XFiles.Visualizers;


namespace XFiles.Controls
{
    public sealed partial class MillerColumnsPage
    {
        /// <summary>
        /// Appends the underlying failure reason captured by FileOperations to an
        /// alert message so the user always understands WHY an operation failed.
        /// Returns an empty string when there is no reason (should not happen when an
        /// operation actually returned Failed).
        /// </summary>
        private static string FailureSuffix()
        {
            string reason = FileOperations.LastFailure;
            return string.IsNullOrEmpty(reason) ? "" : "\n\n" + reason;
        }

        // --- Batch Selection ---

        public void OnToggleBatch()
        {
            if (IsAnyOverlayVisible || FileActionSheetControl.IsOpen ||
                StartMenuControl.IsOpen || LogsPageControl.IsVisible ||
                ShareDialogControl.IsVisible || _isMediaPlayerActive) return;

            if (_isBatchMode)
                ExitBatchMode();
            else
                EnterBatchMode();
        }

        private void EnterBatchMode()
        {
            if (IsBatchMode) return;
            IsBatchMode = true;
            _batchSelectedPaths.Clear();
            Log.Info("BatchMode: entered");
            UpdateBatchCheckboxes();
            UpdateBatchFooter();
            UpdateFooterLabels();
        }

        private void ExitBatchMode()
        {
            if (!IsBatchMode) return;
            IsBatchMode = false;
            _batchSelectedPaths.Clear();
            foreach (var item in CurrentList.Items)
            {
                if (item is EntryViewModel vm) vm.IsSelected = false;
            }
            Log.Info("BatchMode: exited");
            UpdateBatchCheckboxes();
            UpdateBatchFooter();
            UpdateFooterLabels();
        }

        private void ToggleBatchItem()
        {
            if (!_isBatchMode) return;
            var selected = CurrentList.SelectedItem as EntryViewModel;
            if (selected == null || selected.IsDotDot || selected.IsRootContainer) return;

            string key = selected.FullPath ?? ("P|" + selected.PortalKnownFolder + "|" +
                selected.PortalPackageFullName + "|" + selected.PortalPath + "|" + selected.Name);

            if (_batchSelectedPaths.Contains(key))
            {
                _batchSelectedPaths.Remove(key);
                selected.IsSelected = false;
            }
            else
            {
                _batchSelectedPaths.Add(key);
                selected.IsSelected = true;
            }
#if BATCH_DEBUG
            Log.Verb("BatchMode: toggled {Name} (selected={Sel}, total={Total})",
                selected.Name, selected.IsSelected, _batchSelectedPaths.Count);
#endif

            // Move cursor down to help bulk selection
            if (CurrentList.SelectedIndex < CurrentList.Items.Count - 1)
            {
                CurrentList.SelectedIndex++;
                CurrentList.ScrollIntoView(CurrentList.SelectedItem);
            }

            UpdateBatchCheckboxes();
            UpdateBatchFooter();
        }

        private void UpdateBatchCheckboxes()
        {
            for (int i = 0; i < CurrentList.Items.Count; i++)
            {
                var container = CurrentList.ContainerFromIndex(i) as Windows.UI.Xaml.Controls.ListViewItem;
                if (container == null) continue;

                var check = FindBatchCheck(container);
                if (check == null) continue;

                var vm = CurrentList.Items[i] as EntryViewModel;
                bool showCheck = _isBatchMode && vm != null && !vm.IsDotDot;
                check.Visibility = showCheck ? Visibility.Visible : Visibility.Collapsed;
                bool isSelected = container.IsSelected;
                check.BorderBrush = isSelected ? _checkBorderSelected : _checkBorderNormal;

                var fill = check.FindName("BatchCheckFill") as Windows.UI.Xaml.Controls.Border;
                if (fill != null)
                {
                    bool isChecked = showCheck && vm != null && vm.IsSelected;
                    fill.Visibility = isChecked ? Visibility.Visible : Visibility.Collapsed;
                    fill.Background = isSelected ? _checkFillSelected : _checkFillNormal;
                }
            }
        }

        private static Windows.UI.Xaml.Controls.Border FindBatchCheck(Windows.UI.Xaml.Controls.ListViewItem container)
        {
            // Walk visual tree to find BatchCheck border by Name
            return FindElementByName(container, "BatchCheck") as Windows.UI.Xaml.Controls.Border;
        }

        private static Windows.UI.Xaml.FrameworkElement FindElementByName(Windows.UI.Xaml.FrameworkElement root, string name)
        {
            int count = Windows.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = Windows.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i) as Windows.UI.Xaml.FrameworkElement;
                if (child == null) continue;
                if (child.Name == name) return child;
                var found = FindElementByName(child, name);
                if (found != null) return found;
            }
            return null;
        }

        private void UpdateBatchFooter()
        {
            if (_isBatchMode)
            {
                int count = _batchSelectedPaths.Count;
                FooterBatchStatus.Text = count > 0
                    ? $"{count} file{(count == 1 ? "" : "s")} selected"
                    : "Batch select mode";
                FooterBatchStatus.Visibility = Visibility.Visible;
                FooterClipboardIndicator.Visibility = Visibility.Collapsed;
            }
            else
            {
                FooterBatchStatus.Visibility = Visibility.Collapsed;
                UpdateClipboardIndicator();
            }
        }

        private void UpdateFooterLabels()
        {
            if (_isBatchMode)
            {
                FooterALabel.Text = "Select";
                FooterBLabel.Text = "Exit";
                FooterXLabel.Text = "Deselect All";
                FooterYLabel.Text = "Menu";
                FooterViewLabelText.Text = "Batch";
            }
            else
            {
                UpdateFooterALabelFromSelection();
                FooterBLabel.Text = "Back";
                FooterXLabel.Text = "Refresh";
                FooterYLabel.Text = "Menu";
                FooterViewLabelText.Text = "Batch";
            }
        }

        private static string BatchKey(FileEntry e)
        {
            return e.FullPath ?? ("P|" + e.PortalKnownFolder + "|" +
                e.PortalPackageFullName + "|" + e.PortalPath + "|" + e.Name);
        }

        /// <summary>
        /// Get selected FileEntry objects from batch selection.
        /// </summary>
        private List<FileEntry> GetBatchEntries()
        {
            var entries = new List<FileEntry>();
            if (_navigator.Current == null) return entries;
            foreach (var fe in _navigator.Current.Entries)
            {
                if (fe.Name == "..") continue;
                if (_batchSelectedPaths.Contains(BatchKey(fe)))
                    entries.Add(fe);
            }
            return entries;
        }

        /// <summary>
        /// True when source and destination live on the same volume (drive letter).
        /// Same-volume moves are renames — they consume no free space.
        /// </summary>
        private static bool IsSameVolume(string path, string destDir)
        {
            try
            {
                return string.Equals(
                    Path.GetPathRoot(path),
                    Path.GetPathRoot(destDir),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Warns (and asks to continue) when the destination volume lacks free space
        /// for an operation. Returns true when the operation may proceed. Skips the
        /// check when requiredBytes is unknown (<= 0) or the query fails.
        /// </summary>
        private async Task<bool> EnsureDiskSpaceAsync(string destDir, long requiredBytes)
        {
            if (requiredBytes <= 0) return true;

            var space = FileOperations.GetDriveFreeSpace(destDir);
            if (space == null)
            {
                Log.Warn("EnsureDiskSpaceAsync: cannot query free space for {Dir}", destDir);
                return true;
            }

            long free = (long)space.Value.FreeBytes;
            if (!DiskSpaceGuard.IsInsufficient(free, requiredBytes)) return true;

            UpdateFooterALabel("Confirm");
            bool ok = await AlertDialogControl.ShowConfirmAsync(
                DiskSpaceGuard.BuildWarning(free, requiredBytes) + " Continue anyway?");
            UpdateFooterALabelFromSelection();

            Log.Info("EnsureDiskSpaceAsync: insufficient space (need {Req}, free {Free}) — continue={Ok}",
                requiredBytes, free, ok);
            return ok;
        }

        // --- File Action Sheet ---

        private async Task ShowFileActionSheetAsync()
        {
            var selected = CurrentList.SelectedItem as EntryViewModel;

            FileEntry entry;
            if (selected != null)
            {
                entry = new FileEntry
                {
                    Name = selected.Name,
                    FullPath = selected.FullPath,
                    IsDirectory = selected.IsDirectory,
                    IsDrive = selected.IsDrive,
                    IsArchive = selected.IsArchive,
                    SizeBytes = selected.SizeBytes,
                    ArchiveRootPath = selected.ArchiveRootPath,
                    ArchiveInternalPath = selected.ArchiveInternalPath,
                    IsChiptune = selected.IsChiptune,
                    ChiptuneTrackIndex = selected.ChiptuneTrackIndex,
                    ChiptuneSourcePath = selected.ChiptuneSourcePath,
                    IsPortal = selected.IsPortal,
                    PortalKnownFolder = selected.PortalKnownFolder,
                    PortalPackageFullName = selected.PortalPackageFullName,
                    PortalPath = selected.PortalPath,
                    IsNetwork = selected.IsNetwork,
                    ActionKind = selected.ActionKind,
                    NetworkLocationId = selected.NetworkLocationId,
                    NetworkShareName = selected.NetworkShareName,
                    NetworkPath = selected.NetworkPath
                };
            }
            else
            {
                var currentPath = _navigator.Current?.Path ?? "";
                entry = new FileEntry
                {
                    Name = System.IO.Path.GetFileName(currentPath) ?? currentPath,
                    FullPath = currentPath,
                    IsDirectory = true
                };
            }

            Log.Info("ShowFileActionSheetAsync: file={File}, isDir={IsDir}, isArchive={IsArchive}",
                entry.Name, entry.IsDirectory, entry.IsArchive);

            UpdateFooterALabel("Select");

            // Favorites column gets limited action sheet
            bool inFavorites = _navigator.Current?.IsFavorite == true;
            FileAction? action;
            if (inFavorites)
                action = await FileActionSheetControl.ShowFavoritesActionsAsync(entry);
            else if (entry.IsNetwork && entry.NetworkShareName == null && !entry.IsVirtual)
                action = await FileActionSheetControl.ShowNetworkLocationActionsAsync(entry);
            else if (entry.IsNetwork)
                action = await FileActionSheetControl.ShowNetworkFileActionsAsync(entry);
            else
                action = await FileActionSheetControl.ShowAsync(entry);
            UpdateFooterALabelFromSelection();
            if (action == null)
            {
                #if BATCH_DEBUG
                Log.Verb("ShowFileActionSheetAsync: cancelled");
                #endif
                return;
            }

            Log.Info("ShowFileActionSheetAsync: action={Action}", action);

            try
            {
                switch (action)
                {
                    case FileAction.Copy:
                        await HandleCopyAsync(entry);
                        break;
                    case FileAction.Paste:
                        await HandlePasteAsync();
                        break;
                    case FileAction.Move:
                        await HandleMoveAsync(entry);
                        break;
                    case FileAction.Rename:
                        await HandleRenameAsync(entry);
                        break;
                    case FileAction.Delete:
                        await HandleDeleteAsync(entry);
                        break;
                    case FileAction.Extract:
                        if (entry.IsPortal)
                            await HandleExtractPortalZipAsync(entry);
                        else
                            await HandleExtractAsync(entry);
                        break;
                    case FileAction.ExtractFile:
                        await HandleExtractFileAsync(entry);
                        break;
                    case FileAction.CreateFolder:
                        await HandleCreateFolderAsync(entry);
                        break;
                    case FileAction.CreateZip:
                        if (entry.IsPortal)
                            await HandleCreatePortalZipAsync(new List<FileEntry> { entry });
                        else
                            await HandleCreateZipAsync(entry);
                        break;
                    case FileAction.Refresh:
                        OnRefresh();
                        break;
                    case FileAction.Edit:
                        if (entry.IsNetwork)
                            await HandleNetworkTextEditAsync(entry);
                        else
                            await HandleEditAsync(entry);
                        break;
                    case FileAction.Share:
                        await HandleShareAsync(entry);
                        break;
                    case FileAction.AddToFavorites:
                        await AddFavoriteAsync(entry.Name, entry.FullPath, entry.IsDirectory);
                        break;
                    case FileAction.RemoveFromFavorites:
                        await RemoveFavoriteAsync(entry.FullPath);
                        break;
                    case FileAction.DiskSpace:
                        await HandleDiskSpaceAsync(entry);
                        break;
                    case FileAction.RenameLocation:
                        await HandleRenameLocationAsync(entry);
                        break;
                    case FileAction.DeleteLocation:
                        await HandleDeleteLocationAsync(entry);
                        break;
                }
            }
            catch (NetworkOperationException ex)
            {
                Log.Warn("ShowFileActionSheetAsync: network operation failed {Reason}: {Message}",
                    ex.Reason, ex.Message);
                var hint = ex.Reason == NetworkOperationReason.AccessDenied
                    ? "\n\n" + NetworkOperationException.FriendlyMessage(ex.Reason)
                    : "\n\nCheck the connection and try again.";
                _ = AlertDialogControl.ShowAsync(ex.Message + hint, AlertType.Error);
            }
            catch (Exception ex)
            {
                Log.Err($"ShowFileActionSheetAsync: operation failed: {ex.Message}", ex);
                _ = AlertDialogControl.ShowAsync("Operation failed: " + ex.Message, AlertType.Error);
            }
        }

        private async Task ShowFileActionSheetBatchAsync()
        {
            if (_batchSelectedPaths.Count == 0)
            {
                #if BATCH_DEBUG
                Log.Verb("ShowFileActionSheetBatchAsync: no items selected");
                #endif
                return;
            }

            Log.Info("ShowFileActionSheetBatchAsync: {Count} items selected", _batchSelectedPaths.Count);

            UpdateFooterALabel("Select");
            var action = await FileActionSheetControl.ShowBatchAsync(_batchSelectedPaths.Count);
            UpdateFooterALabelFromSelection();
            if (action == null)
            {
                #if BATCH_DEBUG
                Log.Verb("ShowFileActionSheetBatchAsync: cancelled");
                #endif
                return;
            }

            Log.Info("ShowFileActionSheetBatchAsync: action={Action}", action);

            var entries = GetBatchEntries();

            if (action == FileAction.Share &&
                entries.Any(e => e.IsPortal))
            {
                Log.Warn("ShowFileActionSheetBatchAsync: Share unsupported for portal items");
                _ = AlertDialogControl.ShowAsync("Sharing portal items is not supported yet.", AlertType.Info);
                return;
            }

            switch (action)
            {
                case FileAction.Copy:
                    Log.Info("BatchCopy: {Count} items → clipboard", entries.Count);
                    ClipboardState.Copy(entries);
                    UpdateClipboardIndicator();
                    ExitBatchMode();
                    break;
                case FileAction.Move:
                    await HandleBatchMoveAsync(entries);
                    break;
                case FileAction.Delete:
                    await HandleBatchDeleteAsync(entries);
                    break;
                case FileAction.CreateZip:
                    if (entries.Any(e => e.IsPortal))
                        await HandleCreatePortalZipAsync(entries);
                    else
                        await HandleBatchCreateZipAsync(entries);
                    break;
                case FileAction.Share:
                    await HandleBatchShareAsync(entries);
                    break;
            }
        }

        private async Task HandleBatchMoveAsync(List<FileEntry> entries)
        {
            Log.Info("HandleBatchMoveAsync: {Count} items", entries.Count);

            UpdateFooterALabel("Select");
            var destDir = await FolderBrowserDialogControl.ShowAsync(MoveDialogInitialPath());
            UpdateFooterALabelFromSelection();

            if (string.IsNullOrEmpty(destDir))
            {
                #if BATCH_DEBUG
                Log.Verb("HandleBatchMoveAsync: cancelled at folder browser");
                #endif
                return;
            }

            // Build combined file list for confirmation (portal entries have no local paths)
            var allFiles = new List<string>();
            int folderCount = 0;
            foreach (var entry in entries)
            {
                if (entry.IsPortal)
                {
                    if (entry.IsDirectory) folderCount++;
                    continue;
                }
                var (files, folders) = await FileOperations.ListRecursiveAsync(entry.FullPath);
                allFiles.AddRange(files);
                folderCount += folders;
            }

            UpdateFooterALabel("Confirm");
            bool confirmed = await FileOperationConfirmDialogControl.ShowMoveAsync(
                $"{entries.Count} items", destDir, allFiles, folderCount);
            UpdateFooterALabelFromSelection();

            if (!confirmed)
            {
                #if BATCH_DEBUG
                Log.Verb("HandleBatchMoveAsync: confirmation cancelled");
                #endif
                return;
            }

            // Cross-source batch move: portal → local (download, then delete on portal).
            if (entries.Any(e => e.IsPortal))
            {
                await ExecuteBatchMovePortalAsync(entries, destDir);
                ExitBatchMode();
                await _navigator.RefreshCurrentAsync();
                return;
            }

            // Pre-scan for accurate progress
            var sourcePaths = entries.Select(e => e.FullPath).ToList();
            var scan = await FileOperations.ScanEntriesAsync(entries);

            // Same-volume moves are renames — no free space consumed.
            bool sameVolume = sourcePaths.All(p => IsSameVolume(p, destDir));
            if (!sameVolume && !await EnsureDiskSpaceAsync(destDir, scan.TotalBytes))
            {
                #if BATCH_DEBUG
                Log.Verb("HandleBatchMoveAsync: cancelled — insufficient free space");
                #endif
                return;
            }

            OpProgressDialog.Show("Moving", $"{entries.Count} items", destDir,
                0, scan.FileCount);

            int completedFiles = 0;
            long completedBytes = 0;
            int success = 0, failed = 0;
            var conflict = BuildConflictCallback();

            foreach (var entry in entries)
            {
                long lastEntryTotal = 0;
                var progress = new Progress<FileOperations.OperationProgress>(p =>
                {
                    if (p.TotalBytes > 0) lastEntryTotal = p.TotalBytes;
                    OpProgressDialog.UpdateProgress(new FileOperations.OperationProgress
                    {
                        FileName = p.FileName,
                        FileIndex = completedFiles,
                        FileTotal = scan.FileCount,
                        BytesCopied = completedBytes + p.BytesCopied,
                        TotalBytes = scan.TotalBytes
                    });
                });

                var result = await FileOperations.MoveAsync(entry.FullPath, destDir, progress, OpProgressDialog.CancelToken, conflict);

                if (result == FileOperations.OperationResult.Cancelled)
                {
                    Log.Info("HandleBatchMoveAsync: cancelled");
                    OpProgressDialog.Cancel();
                    await Task.Delay(1500);
                    OpProgressDialog.Close();
                    ExitBatchMode();
                    await _navigator.RefreshCurrentAsync();
                    return;
                }

                completedFiles++;
                completedBytes += entry.IsDirectory
                    ? lastEntryTotal
                    : Math.Max(0, entry.SizeBytes);

                if (result == FileOperations.OperationResult.Success)
                    success++;
                else
                    failed++;
            }

            OpProgressDialog.Complete();
            await Task.Delay(400);
            OpProgressDialog.Close();

            if (failed > 0)
                _ = AlertDialogControl.ShowAsync($"Moved {success} items, failed to move {failed}.{FailureSuffix()}", AlertType.Error);
            else
                Log.Info("HandleBatchMoveAsync: {Count} items moved", success);

            ExitBatchMode();
            await _navigator.RefreshCurrentAsync();
        }

        private async Task ExecuteBatchMovePortalAsync(List<FileEntry> entries, string destDir)
        {
            Log.Info("ExecuteBatchMovePortalAsync: {Count} items → {Dest}", entries.Count, destDir);

            long portalFileBytes = entries
                .Where(e => e.IsPortal && !e.IsDirectory)
                .Sum(e => Math.Max(0, e.SizeBytes));
            var localEntries = entries
                .Where(e => !e.IsPortal && !string.IsNullOrEmpty(e.FullPath))
                .ToList();
            var localPaths = localEntries.Select(e => e.FullPath).ToList();
            long required = portalFileBytes;
            if (localPaths.Count > 0 && !localPaths.All(p => IsSameVolume(p, destDir)))
            {
                var ls = await FileOperations.ScanEntriesAsync(localEntries);
                required += ls.TotalBytes;
            }
            if (required > 0 && !await EnsureDiskSpaceAsync(destDir, required))
            {
                #if BATCH_DEBUG
                Log.Verb("ExecuteBatchMovePortalAsync: cancelled — insufficient free space");
                #endif
                return;
            }

            OpProgressDialog.Show("Moving", $"{entries.Count} items", destDir, 0, entries.Count);

            int failed = 0;
            foreach (var entry in entries)
            {
                if (OpProgressDialog.CancelToken.IsCancellationRequested)
                {
                    Log.Dbg("ExecuteBatchMovePortalAsync: cancelled");
                    OpProgressDialog.Cancel();
                    await Task.Delay(1500);
                    OpProgressDialog.Close();
                    return;
                }

                try
                {
                    var progress = new Progress<FileOperations.OperationProgress>(p =>
                    {
                        OpProgressDialog.UpdateProgress(p);
                    });

                    if (entry.IsPortal)
                    {
                        var portalEntry = XFiles.FileSystem.PortalBrowser.ToPortalEntry(entry);
                        await XFiles.FileSystem.PortalBrowser.CopyPortalToLocalAsync(
                            portalEntry, destDir, progress, OpProgressDialog.CancelToken);
                        await DevicePortalService.DeletePortalEntryAsync(portalEntry);
                    }
                    else
                    {
                        var result = await FileOperations.MoveAsync(
                            entry.FullPath, destDir, progress, OpProgressDialog.CancelToken);
                        if (result != FileOperations.OperationResult.Success)
                        {
                            failed++;
                            continue;
                        }
                    }

                    OpProgressDialog.TrackCompleted(entry.Name);
                    Log.Info("ExecuteBatchMovePortalAsync: moved {Name}", entry.Name);
                }
                catch (Exception ex)
                {
                    Log.Warn("ExecuteBatchMovePortalAsync: {Name} failed: {Message}", entry.Name, ex.Message);
                    failed++;
                }
            }

            OpProgressDialog.Complete();
            await Task.Delay(400);
            OpProgressDialog.Close();

            if (failed > 0)
                _ = AlertDialogControl.ShowAsync($"Moved {entries.Count - failed} items, failed to move {failed}.{FailureSuffix()}", AlertType.Error);
            else
                Log.Info("ExecuteBatchMovePortalAsync: {Count} items moved", entries.Count);
        }

        private async Task HandleBatchDeleteAsync(List<FileEntry> entries)
        {
            Log.Info("HandleBatchDeleteAsync: {Count} items", entries.Count);

            // Build combined file list (portal/network entries have no local paths to enumerate)
            var allFiles = new List<string>();
            int folderCount = 0;
            foreach (var entry in entries)
            {
                if (entry.IsPortal || entry.IsNetwork)
                {
                    if (entry.IsDirectory) folderCount++;
                    continue;
                }
                var (files, folders) = await FileOperations.ListRecursiveAsync(entry.FullPath);
                allFiles.AddRange(files);
                folderCount += folders;
            }

            bool confirmed = await FileOperationConfirmDialogControl.ShowAsync(
                $"{entries.Count} items", true, allFiles, folderCount);
            if (!confirmed)
            {
                #if BATCH_DEBUG
                Log.Verb("HandleBatchDeleteAsync: confirmation cancelled");
                #endif
                return;
            }

            int success = 0, failed = 0;
            foreach (var entry in entries)
            {
                if (entry.IsPortal)
                {
                    try
                    {
                        await DevicePortalService.DeletePortalEntryAsync(PortalBrowser.ToPortalEntry(entry));
                        Log.Info("HandleBatchDeleteAsync: portal item {Name} deleted", entry.Name);
                        success++;
                    }
                    catch (Exception ex)
                    {
                        Log.Warn("HandleBatchDeleteAsync: portal item {Name} delete failed: {Message}", entry.Name, ex.Message);
                        failed++;
                    }
                    continue;
                }

                if (entry.IsNetwork)
                {
                    try
                    {
                        var config = await _navigator.GetNetworkConfigAsync(entry.NetworkLocationId);
                        if (config != null)
                        {
                            await NetworkCopyService.DeleteRemoteAsync(
                                _navigator.BrowserFor(config.Protocol), config,
                                entry.NetworkShareName, entry.NetworkPath,
                                entry.IsDirectory, CancellationToken.None);
                            Log.Info("HandleBatchDeleteAsync: network item {Name} deleted", entry.Name);
                            success++;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warn("HandleBatchDeleteAsync: network item {Name} delete failed: {Message}", entry.Name, ex.Message);
                        failed++;
                    }
                    continue;
                }

                FileOperations.OperationResult result;
                if (entry.IsDirectory)
                    result = await FileOperations.DeleteDirectoryAsync(entry.FullPath);
                else
                    result = await FileOperations.DeleteAsync(entry.FullPath);

                if (result == FileOperations.OperationResult.Success)
                    success++;
                else
                    failed++;
            }

            if (failed > 0)
                _ = AlertDialogControl.ShowAsync($"Deleted {success} items, failed to delete {failed}.{FailureSuffix()}", AlertType.Error);
            else
                Log.Info("HandleBatchDeleteAsync: {Count} items deleted", success);

            ExitBatchMode();
            await _navigator.RefreshCurrentAsync();
        }

        private async Task HandleBatchCreateZipAsync(List<FileEntry> entries)
        {
            Log.Info("HandleBatchCreateZipAsync: {Count} items", entries.Count);

            string defaultName = entries.Count == 1
                ? entries[0].Name + ".zip"
                : "archive.zip";
            var zipName = await InputDialogControl.ShowAsync("Create ZIP", defaultName);
            if (string.IsNullOrEmpty(zipName))
            {
                #if BATCH_DEBUG
                Log.Verb("HandleBatchCreateZipAsync: cancelled");
                #endif
                return;
            }

            var currentPath = _navigator.Current?.Path;
            if (string.IsNullOrEmpty(currentPath)) return;
            var zipPath = System.IO.Path.Combine(currentPath, zipName);
            Log.Info("HandleBatchCreateZipAsync: zipPath={Zip}", zipPath);

            var scanZip = await FileOperations.ScanEntriesAsync(entries);
            if (!await EnsureDiskSpaceAsync(currentPath, scanZip.TotalBytes))
            {
                #if BATCH_DEBUG
                Log.Verb("HandleBatchCreateZipAsync: cancelled — insufficient free space");
                #endif
                return;
            }

            OpProgressDialog.Show("Creating ZIP", $"{entries.Count} items", zipPath);
            var progress = new Progress<FileOperations.OperationProgress>(p =>
            {
                OpProgressDialog.UpdateProgress(p);
            });
            var result = await FileOperations.CreateZipAsync(entries.Select(e => e.FullPath).ToList(), zipPath, progress, OpProgressDialog.CancelToken);

            if (result == FileOperations.OperationResult.Cancelled)
            {
                Log.Info("HandleBatchCreateZipAsync: cancelled");
                OpProgressDialog.Cancel();
                await Task.Delay(1500);
                OpProgressDialog.Close();
                return;
            }

            OpProgressDialog.Complete();
            await Task.Delay(400);
            OpProgressDialog.Close();

            if (result == FileOperations.OperationResult.Success)
            {
                Log.Info("HandleBatchCreateZipAsync: success — selecting '{Name}'", zipName);
                ExitBatchMode();
                await _navigator.RefreshCurrentAsync(selectName: zipName);
            }
            else
            {
                Log.Warn("HandleBatchCreateZipAsync: failed");
                _ = AlertDialogControl.ShowAsync($"Failed to create ZIP \"{zipName}\".{FailureSuffix()}", AlertType.Error);
            }
        }

        private async Task HandleBatchShareAsync(List<FileEntry> entries)
        {
            Log.Info("HandleBatchShareAsync: {Count} items", entries.Count);

            if (entries.Count == 1)
            {
                // Single file: share directly
                await HandleShareAsync(entries[0]);
                ExitBatchMode();
                return;
            }

            // Multiple files: create ZIP first, then share
            var tempZip = System.IO.Path.Combine(
                Windows.Storage.ApplicationData.Current.TemporaryFolder.Path,
                $"share-{Guid.NewGuid():N}.zip");

            OpProgressDialog.Show("Compressing", $"{entries.Count} files", "Creating ZIP for sharing...");
            var result = await FileOperations.CreateZipAsync(
                entries.Select(e => e.FullPath).ToList(), tempZip, null, OpProgressDialog.CancelToken);

            if (result != FileOperations.OperationResult.Success)
            {
                Log.Warn("HandleBatchShareAsync: ZIP creation failed");
                OpProgressDialog.Cancel();
                await Task.Delay(1500);
                OpProgressDialog.Close();
                _ = AlertDialogControl.ShowAsync("Failed to create ZIP for sharing." + FailureSuffix(), AlertType.Error);
                return;
            }

            OpProgressDialog.Close();
            await Task.Delay(200);

            // Share the temp ZIP
            var zipEntry = new FileEntry
            {
                Name = $"{entries.Count} file{(entries.Count == 1 ? "" : "s")}",
                FullPath = tempZip
            };

            try
            {
                await HandleShareAsync(zipEntry);
            }
            finally
            {
                try { System.IO.File.Delete(tempZip); } catch { }
            }

            ExitBatchMode();
        }

        private async Task HandleCopyAsync(FileEntry entry)
        {
            if (entry.IsNetwork)
            {
                Log.Info("HandleCopyAsync: network {File} → clipboard", entry.Name);
                ClipboardState.Copy(new[] { entry });
                UpdateClipboardIndicator();
                return;
            }
            Log.Info("HandleCopyAsync: {File} → clipboard", entry.FullPath);
            ClipboardState.Copy(new[] { entry });
            UpdateClipboardIndicator();
            await Task.CompletedTask;
        }

        /// <summary>
        /// Pastes clipboard entries (local or remote) into the current network directory.
        /// Local→remote uploads; remote→remote streams between servers (or the same server).
        /// A self-paste into the same directory becomes "Copy of {name}" (mirrors CopyAsync).
        /// </summary>
        private async Task HandlePasteToNetworkAsync()
        {
            var current = _navigator.Current;
            if (current == null || !current.IsNetwork) return;
            if (current.NetworkProtocol == NetworkProtocol.Smb && string.IsNullOrEmpty(current.NetworkShareName))
            {
                _ = AlertDialogControl.ShowAsync("Open a shared folder before pasting.", AlertType.Info);
                return;
            }

            var config = await _navigator.GetNetworkConfigAsync(current.NetworkLocationId);
            if (config == null) return;

            var entries = ClipboardState.Entries;
            if (entries.Count == 0) return;

            string share = current.NetworkShareName;
            string destDir = current.NetworkPath ?? "";

            int fileCount = 0;
            long totalBytes = 0;
            foreach (var e in entries)
            {
                if (e.IsNetwork)
                {
                    var srcConfig = await _navigator.GetNetworkConfigAsync(e.NetworkLocationId);
                    if (srcConfig == null) continue;
                    var (fc, tb) = await NetworkCopyService.ScanRemoteEntriesAsync(
                        _navigator.BrowserFor(srcConfig.Protocol), srcConfig, e.NetworkShareName, e.NetworkPath,
                        e.IsDirectory, CancellationToken.None);
                    fileCount += fc;
                    totalBytes += tb;
                }
                else
                {
                    var scan = await FileOperations.ScanEntriesAsync(new[] { e });
                    fileCount += scan.FileCount;
                    totalBytes += scan.TotalBytes;
                }
            }

            Log.Info("HandlePasteToNetworkAsync: {Count} items → {Share}/{Dest} ({Bytes} bytes)",
                entries.Count, share, destDir, totalBytes);

            OpProgressDialog.Show("Copying", $"{entries.Count} items", share + "\\" + destDir, 0, fileCount);
            if (totalBytes > 0)
                OpProgressDialog.UpdateProgress(new FileOperations.OperationProgress
                {
                    TotalBytes = totalBytes,
                    FileTotal = fileCount
                });

            int success = 0, failed = 0;
            long completedBytes = 0;
            var sw = Stopwatch.StartNew();
            var conflict = BuildConflictCallback();
            NetworkOperationException lastNetworkError = null;

            foreach (var entry in entries)
            {
                if (OpProgressDialog.CancelToken.IsCancellationRequested)
                {
                    Log.Dbg("HandlePasteToNetworkAsync: cancelled");
                    OpProgressDialog.Cancel();
                    await Task.Delay(1500);
                    OpProgressDialog.Close();
                    UpdateClipboardIndicator();
                    return;
                }

                bool sameDir = entry.IsNetwork
                    && entry.NetworkLocationId == current.NetworkLocationId
                    && string.Equals(entry.NetworkShareName, share, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(NetworkParentOf(entry.NetworkPath), destDir, StringComparison.OrdinalIgnoreCase);
                string destName = sameDir ? "Copy of " + entry.Name : null;

                bool ok;
                long entryBytes = entry.IsDirectory ? 0 : Math.Max(0, entry.SizeBytes);
                try
                {
                    var progress = new Progress<FileOperations.OperationProgress>(p =>
                    {
                        var overall = new FileOperations.OperationProgress
                        {
                            FileName = p.FileName,
                            FileIndex = success + failed,
                            FileTotal = fileCount,
                            BytesCopied = completedBytes + p.BytesCopied,
                            TotalBytes = totalBytes
                        };
                        OpProgressDialog.UpdateProgress(overall);
                    });

                    if (entry.IsNetwork)
                    {
                        var srcConfig = await _navigator.GetNetworkConfigAsync(entry.NetworkLocationId);
                        ok = srcConfig != null && await NetworkCopyService.CopyRemoteToRemoteAsync(
                            _navigator.BrowserFor(srcConfig.Protocol), srcConfig, entry.NetworkShareName, entry.NetworkPath,
                            _navigator.BrowserFor(config.Protocol), config, share, destDir,
                            entry.IsDirectory, entry.Name, progress, OpProgressDialog.CancelToken,
                            destName, sameDir ? AutoRenameConflict : conflict);
                    }
                    else
                    {
                        ok = await NetworkCopyService.CopyLocalToRemoteAsync(
                            _navigator.BrowserFor(config.Protocol), config, share, destDir,
                            entry.FullPath, entry.IsDirectory, entry.Name, progress,
                            OpProgressDialog.CancelToken, destName, sameDir ? AutoRenameConflict : conflict);
                    }
                }
                catch (OperationCanceledException)
                {
                    Log.Info("HandlePasteToNetworkAsync: cancelled by user");
                    OpProgressDialog.Cancel();
                    await Task.Delay(1500);
                    OpProgressDialog.Close();
                    UpdateClipboardIndicator();
                    await _navigator.RefreshCurrentAsync();
                    return;
                }
                catch (NetworkOperationException ex)
                {
                    ok = false;
                    Log.Warn("HandlePasteToNetworkAsync: {File} — {Reason} ({Ex})", entry.Name, ex.Reason, ex.Message);
                    lastNetworkError = ex;
                }
                catch (Exception ex)
                {
                    ok = false;
                    Log.Err($"HandlePasteToNetworkAsync: {entry.Name} failed: {ex.Message}", ex);
                }

                if (ok) success++; else failed++;
                completedBytes += entryBytes;
                OpProgressDialog.TrackCompleted(entry.Name, entryBytes);
                if (failed > 0)
                {
                    var hint = lastNetworkError?.Reason == NetworkOperationReason.AccessDenied
                        ? "\n\n" + NetworkOperationException.FriendlyMessage(lastNetworkError.Reason)
                        : "";
                    _ = AlertDialogControl.ShowAsync($"Cannot copy \"{entry.Name}\"{hint}\n\n{FailureSuffix()}", AlertType.Error);
                }
            }

            sw.Stop();
            Log.Info("HandlePasteToNetworkAsync: COMPLETE — {S}/{T} items in {E:0.0}s ({M:0.00} MB/s)",
                success, entries.Count, sw.Elapsed.TotalSeconds,
                sw.Elapsed.TotalSeconds > 0 ? completedBytes / 1048576.0 / sw.Elapsed.TotalSeconds : 0);

            OpProgressDialog.Complete();
            await Task.Delay(400);
            OpProgressDialog.Close();
            UpdateClipboardIndicator();
            await _navigator.RefreshCurrentAsync();
        }

        /// <summary>Downloads network clipboard entries into a local directory.</summary>
        private async Task HandlePasteNetworkToLocalAsync(string destDir)
        {
            var entries = ClipboardState.Entries.Where(e => e.IsNetwork).ToList();
            if (entries.Count == 0) return;

            var srcConfigs = new List<NetworkServerConfig>();
            int fileCount = 0;
            long totalBytes = 0;
            foreach (var e in entries)
            {
                var cfg = await _navigator.GetNetworkConfigAsync(e.NetworkLocationId);
                if (cfg == null) continue;
                srcConfigs.Add(cfg);
                var (fc, tb) = await NetworkCopyService.ScanRemoteEntriesAsync(
                    _navigator.BrowserFor(cfg.Protocol), cfg, e.NetworkShareName, e.NetworkPath,
                    e.IsDirectory, CancellationToken.None);
                fileCount += fc;
                totalBytes += tb;
            }
            if (!await EnsureDiskSpaceAsync(destDir, totalBytes)) return;

            Log.Info("HandlePasteNetworkToLocalAsync: {Count} items → {Dest} ({Bytes} bytes)",
                entries.Count, destDir, totalBytes);

            OpProgressDialog.Show("Copying", $"{entries.Count} items", destDir, 0, fileCount);
            if (totalBytes > 0)
                OpProgressDialog.UpdateProgress(new FileOperations.OperationProgress
                {
                    TotalBytes = totalBytes,
                    FileTotal = fileCount
                });

            int success = 0, failed = 0;
            long completedBytes = 0;
            var sw = Stopwatch.StartNew();
            var conflict = BuildConflictCallback();
            NetworkOperationException lastNetworkError = null;

            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (OpProgressDialog.CancelToken.IsCancellationRequested)
                {
                    Log.Dbg("HandlePasteNetworkToLocalAsync: cancelled");
                    OpProgressDialog.Cancel();
                    await Task.Delay(1500);
                    OpProgressDialog.Close();
                    UpdateClipboardIndicator();
                    return;
                }

                var cfg = srcConfigs[i];
                bool ok;
                long entryBytes = entry.IsDirectory ? 0 : Math.Max(0, entry.SizeBytes);
                long resumeFrom = 0;
                try
                {
                    // Detect partial file from a previous failed copy attempt.
                    if (!entry.IsDirectory)
                    {
                        string destPath = Path.Combine(destDir, Path.GetFileName(entry.NetworkPath.Replace('/', '\\')));
                        if (File.Exists(destPath))
                        {
                            long existingSize = 0;
                            try { existingSize = new FileInfo(destPath).Length; } catch { }
                            if (existingSize > 0 && existingSize < entry.SizeBytes)
                            {
                                OpProgressDialog.Close();
                                var decision = await ShowPartialCopyDialogAsync(
                                    Path.GetFileName(entry.NetworkPath.Replace('/', '\\')),
                                    existingSize, entry.SizeBytes);
                                if (decision == ConflictDecision.Cancel)
                                {
                                    UpdateClipboardIndicator();
                                    return;
                                }
                                if (decision == ConflictDecision.Resume)
                                {
                                    resumeFrom = existingSize;
                                }
                                // ReplaceAll → start fresh, resumeFrom stays 0
                                OpProgressDialog.Show("Copying", $"{entries.Count} items", destDir, 0, fileCount);
                                if (totalBytes > 0)
                                    OpProgressDialog.UpdateProgress(new FileOperations.OperationProgress
                                    {
                                        TotalBytes = totalBytes,
                                        FileTotal = fileCount
                                    });
                            }
                        }
                    }

                    var progress = new Progress<FileOperations.OperationProgress>(p =>
                    {
                        var overall = new FileOperations.OperationProgress
                        {
                            FileName = p.FileName,
                            FileIndex = success + failed,
                            FileTotal = fileCount,
                            BytesCopied = completedBytes + p.BytesCopied,
                            TotalBytes = totalBytes
                        };
                        OpProgressDialog.UpdateProgress(overall);
                    });
                    ok = await NetworkCopyService.CopyRemoteToLocalAsync(
                        _navigator.BrowserFor(cfg.Protocol), cfg, entry.NetworkShareName, entry.NetworkPath,
                        destDir, entry.IsDirectory, progress, OpProgressDialog.CancelToken, conflict, resumeFrom);
                }
                catch (OperationCanceledException)
                {
                    Log.Info("HandlePasteNetworkToLocalAsync: cancelled by user");
                    OpProgressDialog.Cancel();
                    await Task.Delay(1500);
                    OpProgressDialog.Close();
                    UpdateClipboardIndicator();
                    return;
                }
                catch (NetworkOperationException ex)
                {
                    ok = false;
                    Log.Warn("HandlePasteNetworkToLocalAsync: {File} — {Reason} ({Ex})", entry.Name, ex.Reason, ex.Message);
                    lastNetworkError = ex;
                }
                catch (Exception ex)
                {
                    ok = false;
                    Log.Err($"HandlePasteNetworkToLocalAsync: {entry.Name} failed: {ex.Message}", ex);
                }

                if (ok) success++; else failed++;
                completedBytes += entryBytes;
                OpProgressDialog.TrackCompleted(entry.Name, entryBytes);
                if (failed > 0)
                {
                    var hint = lastNetworkError?.Reason == NetworkOperationReason.AccessDenied
                        ? "\n\n" + NetworkOperationException.FriendlyMessage(lastNetworkError.Reason)
                        : "";
                    _ = AlertDialogControl.ShowAsync($"Cannot copy \"{entry.Name}\"{hint}\n\n{FailureSuffix()}", AlertType.Error);
                }
            }

            sw.Stop();
            Log.Info("HandlePasteNetworkToLocalAsync: COMPLETE — {S}/{T} items in {E:0.0}s ({M:0.00} MB/s)",
                success, entries.Count, sw.Elapsed.TotalSeconds,
                sw.Elapsed.TotalSeconds > 0 ? completedBytes / 1048576.0 / sw.Elapsed.TotalSeconds : 0);

            OpProgressDialog.Complete();
            await Task.Delay(400);
            OpProgressDialog.Close();
            UpdateClipboardIndicator();
            await _navigator.RefreshCurrentAsync();
        }

        private static string NetworkParentOf(string path) => NetworkPathUtil.Parent(path);

        /// <summary>Always-rename conflict resolver for same-directory pastes (no prompt).</summary>
        private static readonly Func<string, Task<ConflictDecision>> AutoRenameConflict =
            _ => Task.FromResult(ConflictDecision.RenameAll);

        /// <summary>
        /// Builds a memoizing conflict callback for copy/move: the first collision shows
        /// FileConflictDialog on the UI thread (REPLACE ALL / RENAME ALL / CANCEL), and the
        /// chosen "All" decision is cached for the rest of the operation so the dialog only
        /// appears once. Cancellation surfaces as OperationCanceledException.
        /// </summary>
        private Func<string, Task<ConflictDecision>> BuildConflictCallback()
        {
            ConflictDecision? cached = null;
            return conflictPath =>
            {
                if (cached.HasValue) return Task.FromResult(cached.Value);
                var tcs = new TaskCompletionSource<ConflictDecision>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, async () =>
                {
                    try
                    {
                        var decision = await FileConflictDialogControl.ShowAsync(conflictPath);
                        cached = decision;
                        tcs.TrySetResult(decision);
                    }
                    catch (Exception ex)
                    {
                        Log.Warn("BuildConflictCallback: dialog error", ex);
                        cached = ConflictDecision.Cancel;
                        tcs.TrySetResult(ConflictDecision.Cancel);
                    }
                });
                return tcs.Task;
            };
        }

        /// <summary>
        /// Shows the partial-file dialog (RESUME / OVERWRITE / CANCEL) on the UI thread.
        /// Called when a re-paste detects a partially-copied file from a previous attempt.
        /// </summary>
        private Task<ConflictDecision> ShowPartialCopyDialogAsync(string name, long existingBytes, long totalBytes)
        {
            var tcs = new TaskCompletionSource<ConflictDecision>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, async () =>
            {
                try
                {
                    var decision = await FileConflictDialogControl.ShowPartialAsync(name, existingBytes, totalBytes);
                    tcs.TrySetResult(decision);
                }
                catch (Exception ex)
                {
                    Log.Warn("ShowPartialCopyDialogAsync: dialog error", ex);
                    tcs.TrySetResult(ConflictDecision.Cancel);
                }
            });
            return tcs.Task;
        }

        private async Task HandlePasteAsync()
        {
            try
            {
            if (!ClipboardState.HasItems) return;

            var current = _navigator.Current;

            // Paste into a network column → upload local/remote clipboard entries over SMB.
            if (current != null && current.IsNetwork)
            {
                await HandlePasteToNetworkAsync();
                return;
            }

            // Paste into a portal column → upload local clipboard entries to the portal.
            if (current != null && current.IsPortal)
            {
                await HandlePasteToPortalAsync();
                return;
            }

            var destDir = current?.Path;
            if (string.IsNullOrEmpty(destDir))
            {
                Log.Warn("HandlePasteAsync: no current directory");
                await Task.CompletedTask;
                return;
            }

            // Pre-flight: check destination directory write permission
            var writeError = FileOperations.CheckWritable(destDir, true);
            if (writeError != null)
            {
                Log.Warn("HandlePasteAsync: dest not writable — {Reason}", writeError);
                _ = AlertDialogControl.ShowAsync($"Cannot paste here\n\n{writeError}", AlertType.Error);
                return;
            }

            var entries = ClipboardState.Entries;

            // Paste network clipboard entries into a local directory → download to disk.
            if (entries.Any(e => e.IsNetwork))
            {
                await HandlePasteNetworkToLocalAsync(destDir);
                return;
            }

            // Paste portal clipboard entries into a local directory → download to disk.
            if (entries.Any(e => e.IsPortal))
            {
                await HandlePastePortalToLocalAsync(destDir);
                return;
            }

            Log.Info("HandlePasteAsync: {Count} items → {Dest}",
                entries.Count, destDir);

            // Pre-scan all source paths for accurate total
            var sourcePaths = entries.Select(e => e.FullPath).ToList();
            var scan = await FileOperations.ScanEntriesAsync(entries);

            if (!await EnsureDiskSpaceAsync(destDir, scan.TotalBytes))
            {
                #if BATCH_DEBUG
                Log.Verb("HandlePasteAsync: cancelled — insufficient free space");
                #endif
                return;
            }

            OpProgressDialog.Show("Copying", $"{entries.Count} items", destDir,
                0, scan.FileCount);
            if (scan.TotalBytes > 0)
                OpProgressDialog.UpdateProgress(new FileOperations.OperationProgress
                {
                    TotalBytes = scan.TotalBytes,
                    FileTotal = scan.FileCount
                });

            int completedFiles = 0;
            long completedBytes = 0;
            int success = 0, failed = 0;
            var pasteSw = Stopwatch.StartNew();
            var conflict = BuildConflictCallback();

            foreach (var entry in entries)
            {
                bool sameDir = string.Equals(
                    System.IO.Path.GetDirectoryName(entry.FullPath)?.TrimEnd('\\'),
                    destDir.TrimEnd('\\'),
                    StringComparison.OrdinalIgnoreCase);

                long lastEntryTotal = 0;
                // Progress updates overall completed bytes across all entries
                // Progress<T> already marshals to the UI thread via its captured
                // SynchronizationContext — no extra Dispatcher.RunAsync needed.
                var progress = new Progress<FileOperations.OperationProgress>(p =>
                {
                    if (p.TotalBytes > 0) lastEntryTotal = p.TotalBytes;
                    // Overlay this entry's progress onto overall tracking
                    var overall = new FileOperations.OperationProgress
                    {
                        FileName = p.FileName,
                        FileIndex = completedFiles,
                        FileTotal = scan.FileCount,
                        BytesCopied = completedBytes + p.BytesCopied,
                        TotalBytes = scan.TotalBytes
                    };
                    OpProgressDialog.UpdateProgress(overall);
                });

                var result = await FileOperations.CopyAsync(
                    entry.FullPath, destDir, progress, sameDir, OpProgressDialog.CancelToken,
                    conflict: sameDir ? null : conflict);

                if (result == FileOperations.OperationResult.Cancelled)
                {
                    Log.Dbg("HandlePasteAsync: cancelled at file {Completed}/{Total}",
                        completedFiles, scan.FileCount);
                    OpProgressDialog.Cancel();
                    await Task.Delay(1500);
                    OpProgressDialog.Close();
                    break;
                }

                // After this entry completes, advance completedFiles by entry's file count
                completedFiles++;
                long entryBytes = entry.IsDirectory
                    ? lastEntryTotal
                    : Math.Max(0, entry.SizeBytes);
                completedBytes += entryBytes;

                OpProgressDialog.TrackCompleted(entry.Name, entryBytes);

                if (result != FileOperations.OperationResult.Success)
                {
                    failed++;
                    Log.Warn("HandlePasteAsync: {File} failed", entry.Name);
                    _ = AlertDialogControl.ShowAsync($"Copy failed: \"{entry.Name}\".{FailureSuffix()}", AlertType.Error);
                }
                else
                {
                    success++;
                }
            }

            pasteSw.Stop();
            Log.Info("HandlePasteAsync: COMPLETE — {Success}/{Total} items, {Bytes} bytes in {Elapsed:0.0}s ({Mbps:0.00} MB/s avg)",
                success, entries.Count, completedBytes, pasteSw.Elapsed.TotalSeconds,
                pasteSw.Elapsed.TotalSeconds > 0 ? completedBytes / (1024.0 * 1024.0) / pasteSw.Elapsed.TotalSeconds : 0);

            OpProgressDialog.Complete();
            await Task.Delay(400);
            OpProgressDialog.Close();

            UpdateClipboardIndicator();
            await _navigator.RefreshCurrentAsync();
        }
        catch (Exception ex) { Log.Err("HandlePasteAsync: {Ex}", ex); _ = AlertDialogControl.ShowAsync($"Paste failed.\n\n{ex.Message}", AlertType.Error); }
        }

        /// <summary>
        /// Pastes local clipboard entries into the current portal directory via REST upload.
        /// </summary>
        private async Task HandlePasteToPortalAsync()
        {
            var current = _navigator.Current;
            if (current == null || !current.IsPortal)
            {
                Log.Warn("HandlePasteToPortalAsync: not in a portal column");
                return;
            }
            if (string.IsNullOrEmpty(current.PortalKnownFolder))
            {
                Log.Warn("HandlePasteToPortalAsync: portal column has no target folder");
                _ = AlertDialogControl.ShowAsync("Choose a folder inside the portal to paste into.", AlertType.Info);
                return;
            }

            var knownFolder = current.PortalKnownFolder;
            var packageFullName = current.PortalPackageFullName ?? "";
            var portalPath = current.PortalPath ?? "\\";

            var entries = ClipboardState.Entries;
            if (entries.Count == 0) return;

            var driveRoot = PortalCore.DestinationDriveRoot(knownFolder);
            if (driveRoot != null)
            {
                long knownBytes = entries
                    .Where(e => !e.IsDirectory && e.SizeBytes > 0)
                    .Sum(e => e.SizeBytes);
                if (!await EnsureDiskSpaceAsync(driveRoot, knownBytes)) return;
            }

            Log.Info("HandlePasteToPortalAsync: {Count} items ({Portal} from portal) → portal {Known}/{Pkg}{Path}",
                entries.Count, entries.Count(e => e.IsPortal), knownFolder, packageFullName, portalPath);

            // Portal-to-portal items round-trip through local staging (download + upload).
            string staging = null;
            try
            {
                OpProgressDialog.Show("Copying", $"{entries.Count} items", knownFolder + portalPath, 0, entries.Count);

                int failed = 0;
                foreach (var entry in entries)
                {
                    if (!entry.IsPortal && !entry.IsNetwork && string.IsNullOrEmpty(entry.FullPath)) continue;

                    if (OpProgressDialog.CancelToken.IsCancellationRequested)
                    {
                        Log.Dbg("HandlePasteToPortalAsync: cancelled");
                        OpProgressDialog.Cancel();
                        await Task.Delay(1500);
                        OpProgressDialog.Close();
                        UpdateClipboardIndicator();
                        return;
                    }

                    var progress = new Progress<FileOperations.OperationProgress>(p =>
                    {
                        OpProgressDialog.UpdateProgress(p);
                    });

                    try
                    {
                        if (entry.IsPortal)
                        {
                            if (staging == null) staging = CreatePortalOpStagingDir();
                            await XFiles.FileSystem.PortalBrowser.CopyPortalToPortalAsync(
                                XFiles.FileSystem.PortalBrowser.ToPortalEntry(entry),
                                knownFolder, packageFullName, portalPath, staging, progress, OpProgressDialog.CancelToken);
                        }
                        else if (entry.IsNetwork)
                        {
                            if (staging == null) staging = CreatePortalOpStagingDir();
                            string stagingPath = System.IO.Path.Combine(staging, entry.Name);
                            var config = await _navigator.GetNetworkConfigAsync(entry.NetworkLocationId);
                            if (config != null)
                            {
                                using (var stream = await _navigator.BrowserFor(config.Protocol)
                                    .OpenReadAsync(config, entry.NetworkShareName, entry.NetworkPath,
                                        OpProgressDialog.CancelToken))
                                {
                                    using (var fs = new System.IO.FileStream(stagingPath,
                                        System.IO.FileMode.Create, System.IO.FileAccess.Write,
                                        System.IO.FileShare.None, 81920, true))
                                    {
                                        await stream.CopyToAsync(fs, 81920, OpProgressDialog.CancelToken);
                                    }
                                }
                                await XFiles.FileSystem.PortalBrowser.UploadLocalToPortalAsync(
                                    stagingPath, knownFolder, packageFullName, portalPath,
                                    progress, OpProgressDialog.CancelToken);
                            }
                        }
                        else
                        {
                            await XFiles.FileSystem.PortalBrowser.UploadLocalToPortalAsync(
                                entry.FullPath, knownFolder, packageFullName, portalPath,
                                progress, OpProgressDialog.CancelToken);
                        }
                        OpProgressDialog.TrackCompleted(entry.Name);
                        Log.Info("HandlePasteToPortalAsync: uploaded {Name}", entry.Name);
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        Log.Warn("HandlePasteToPortalAsync: {File} failed: {Message}", entry.Name, ex.Message);
                    }
                }

                OpProgressDialog.Complete();
                await Task.Delay(400);
                OpProgressDialog.Close();

                if (failed > 0)
                    _ = AlertDialogControl.ShowAsync($"Upload failed for {failed} item(s).{FailureSuffix()}", AlertType.Error);

                UpdateClipboardIndicator();
                await _navigator.RefreshCurrentAsync();
            }
            catch (Exception ex)
            {
                Log.Err("HandlePasteToPortalAsync: {Ex}", ex);
                OpProgressDialog.Close();
                _ = AlertDialogControl.ShowAsync($"Paste failed.\n\n{ex.Message}", AlertType.Error);
            }
            finally
            {
                if (staging != null)
                {
                    try { System.IO.Directory.Delete(staging, true); }
                    catch (Exception ex) { Log.Warn("HandlePasteToPortalAsync: staging cleanup failed: {Message}", ex.Message); }
                }
            }
        }

        /// <summary>
        /// Creates a ZIP from portal items: downloads each item to a temp staging dir,
        /// compresses locally, then uploads the resulting ZIP to the current portal folder.
        /// </summary>
        private async Task HandleCreatePortalZipAsync(List<FileEntry> entries)
        {
            var current = _navigator.Current;
            if (current == null || !current.IsPortal || string.IsNullOrEmpty(current.PortalKnownFolder))
            {
                Log.Warn("HandleCreatePortalZipAsync: not in a portal column");
                return;
            }

            string defaultName = entries.Count == 1 ? entries[0].Name + ".zip" : "archive.zip";
            var zipName = await InputDialogControl.ShowAsync("Create ZIP", defaultName);
            if (string.IsNullOrEmpty(zipName))
            {
                #if BATCH_DEBUG
                Log.Verb("HandleCreatePortalZipAsync: cancelled");
                #endif
                return;
            }

            var knownFolder = current.PortalKnownFolder;
            var packageFullName = current.PortalPackageFullName ?? "";
            var portalPath = current.PortalPath ?? "\\";

            // Local temp gate: known file bytes only (directories are not crawled).
            long knownBytes = entries
                .Where(e => !e.IsDirectory && e.SizeBytes > 0)
                .Sum(e => e.SizeBytes);
            if (!await EnsureDiskSpaceAsync(Windows.Storage.ApplicationData.Current.TemporaryFolder.Path, knownBytes))
            {
                #if BATCH_DEBUG
                Log.Verb("HandleCreatePortalZipAsync: cancelled — insufficient temp space");
                #endif
                return;
            }

            string staging = CreatePortalOpStagingDir();
            try
            {
                var progress = new Progress<FileOperations.OperationProgress>(p =>
                {
                    OpProgressDialog.UpdateProgress(p);
                });

                OpProgressDialog.Show("Downloading from portal", $"{entries.Count} items", staging, 0, entries.Count);
                foreach (var entry in entries)
                {
                    if (OpProgressDialog.CancelToken.IsCancellationRequested)
                    {
                        OpProgressDialog.Cancel();
                        await Task.Delay(1500);
                        OpProgressDialog.Close();
                        return;
                    }
                    try
                    {
                        await XFiles.FileSystem.PortalBrowser.CopyPortalToLocalAsync(
                            XFiles.FileSystem.PortalBrowser.ToPortalEntry(entry), staging, progress, OpProgressDialog.CancelToken);
                        OpProgressDialog.TrackCompleted(entry.Name);
                    }
                    catch (Exception ex)
                    {
                        Log.Warn("HandleCreatePortalZipAsync: download {Name} failed: {Message}", entry.Name, ex.Message);
                        OpProgressDialog.Complete();
                        await Task.Delay(400);
                        OpProgressDialog.Close();
                        _ = AlertDialogControl.ShowAsync($"Failed to download \"{entry.Name}\" from the portal.\n\n{ex.Message}", AlertType.Error);
                        return;
                    }
                }

                var sources = entries.Select(e => System.IO.Path.Combine(staging, e.Name)).ToList();
                string zipPath = System.IO.Path.Combine(staging, zipName);

                OpProgressDialog.SetPhase("Creating ZIP", entries.Count == 1 ? entries[0].Name : $"{entries.Count} items", zipName);
                var result = await FileOperations.CreateZipAsync(sources, zipPath, progress, OpProgressDialog.CancelToken);
                if (result == FileOperations.OperationResult.Cancelled)
                {
                    OpProgressDialog.Cancel();
                    await Task.Delay(1500);
                    OpProgressDialog.Close();
                    return;
                }
                if (result != FileOperations.OperationResult.Success)
                {
                    OpProgressDialog.Complete();
                    await Task.Delay(400);
                    OpProgressDialog.Close();
                    _ = AlertDialogControl.ShowAsync($"Failed to create ZIP \"{zipName}\".{FailureSuffix()}", AlertType.Error);
                    return;
                }

                long zipSize = new System.IO.FileInfo(zipPath).Length;
                var driveRoot = PortalCore.DestinationDriveRoot(knownFolder);
                if (driveRoot != null && !await EnsureDiskSpaceAsync(driveRoot, zipSize))
                {
                    OpProgressDialog.Complete();
                    await Task.Delay(400);
                    OpProgressDialog.Close();
                    return;
                }

                if (!await ConfirmPortalOverwriteOnceAsync(knownFolder, packageFullName, portalPath, new List<string> { zipName }))
                {
                    OpProgressDialog.Complete();
                    await Task.Delay(400);
                    OpProgressDialog.Close();
                    Log.Info("HandleCreatePortalZipAsync: skipped — overwrite declined");
                    return;
                }

                OpProgressDialog.SetPhase("Uploading to portal", zipName, knownFolder + portalPath);
                await XFiles.FileSystem.PortalBrowser.UploadLocalToPortalAsync(
                    zipPath, knownFolder, packageFullName, portalPath, progress, OpProgressDialog.CancelToken);

                OpProgressDialog.Complete();
                await Task.Delay(400);
                OpProgressDialog.Close();

                Log.Info("HandleCreatePortalZipAsync: success — '{Zip}' uploaded to portal {Known}/{Pkg}{Path}", zipName, knownFolder, packageFullName, portalPath);
                await _navigator.RefreshCurrentAsync(zipName);
            }
            catch (Exception ex)
            {
                Log.Err("HandleCreatePortalZipAsync: {Ex}", ex);
                OpProgressDialog.Close();
                _ = AlertDialogControl.ShowAsync($"Failed to create ZIP on the portal.\n\n{ex.Message}", AlertType.Error);
            }
            finally
            {
                try { System.IO.Directory.Delete(staging, true); }
                catch (Exception ex) { Log.Warn("HandleCreatePortalZipAsync: cleanup failed: {Message}", ex.Message); }
            }
        }

        /// <summary>
        /// Extracts a ZIP that lives in the portal: downloads it to the portal cache,
        /// extracts into a temp staging dir, then uploads the extracted items to the
        /// current portal folder. Single-root ZIPs upload their contents directly
        /// (mirroring the local "extract here" behaviour); multi-root ZIPs ask whether
        /// to create a folder named after the archive or extract in place.
        /// </summary>
        private async Task HandleExtractPortalZipAsync(FileEntry entry)
        {
            var current = _navigator.Current;
            if (current == null || !current.IsPortal || string.IsNullOrEmpty(current.PortalKnownFolder))
            {
                Log.Warn("HandleExtractPortalZipAsync: not in a portal column");
                return;
            }

            var knownFolder = current.PortalKnownFolder;
            var packageFullName = current.PortalPackageFullName ?? "";
            var portalPath = current.PortalPath ?? "\\";

            string staging = CreatePortalOpStagingDir();
            try
            {
                var progress = new Progress<FileOperations.OperationProgress>(p =>
                {
                    OpProgressDialog.UpdateProgress(p);
                });

                OpProgressDialog.Show("Downloading", entry.Name, "portal");
                var portalEntry = XFiles.FileSystem.PortalBrowser.ToPortalEntry(entry);
                string cacheZip = await PortalCache.EnsureAsync(portalEntry, new Progress<double>(p =>
                {
                    ((System.IProgress<FileOperations.OperationProgress>)progress).Report(new FileOperations.OperationProgress
                    {
                        FileName = entry.Name,
                        PercentComplete = (int)(p * 100),
                        BytesCopied = (long)(p * entry.SizeBytes),
                        TotalBytes = entry.SizeBytes
                    });
                }));
                if (cacheZip == null)
                {
                    OpProgressDialog.Close();
                    _ = AlertDialogControl.ShowAsync($"Failed to download \"{entry.Name}\" from the portal.{FailureSuffix()}", AlertType.Error);
                    return;
                }

                string extractDir = System.IO.Path.Combine(staging, "extracted");
                System.IO.Directory.CreateDirectory(extractDir);

                OpProgressDialog.SetPhase("Extracting", entry.Name, extractDir);
                string rootFolder = await Task.Run(() => FileOperations.GetSingleRootFolder(cacheZip));
                var extract = await FileOperations.ExtractAsync(cacheZip, extractDir, progress, null, OpProgressDialog.CancelToken);
                if (extract.Result == FileOperations.OperationResult.Cancelled)
                {
                    OpProgressDialog.Cancel();
                    await Task.Delay(1500);
                    OpProgressDialog.Close();
                    return;
                }
                if (extract.Result != FileOperations.OperationResult.Success)
                {
                    OpProgressDialog.Complete();
                    await Task.Delay(400);
                    OpProgressDialog.Close();
                    string msg = string.IsNullOrEmpty(extract.ErrorMessage)
                        ? $"Failed to extract \"{entry.Name}\".{FailureSuffix()}"
                        : $"Failed to extract \"{entry.Name}\".\n\n{extract.ErrorMessage}";
                    _ = AlertDialogControl.ShowAsync(msg, AlertType.Error);
                    return;
                }

                // Mirror the local extract UX: single-root archives extract in place;
                // multi-root archives ask whether to create a folder or extract here.
                var archiveName = System.IO.Path.GetFileNameWithoutExtension(entry.Name);
                bool extractToFolder = false;
                List<string> uploadItems;
                if (rootFolder != null)
                {
                    string rootPath = System.IO.Path.Combine(extractDir, rootFolder);
                    uploadItems = System.IO.Directory.EnumerateFileSystemEntries(rootPath).ToList();
                }
                else
                {
                    OpProgressDialog.Close();
                    var choice = await FileActionSheetControl.ShowExtractChoiceAsync(archiveName);
                    if (choice == null)
                    {
                        #if BATCH_DEBUG
                        Log.Verb("HandleExtractPortalZipAsync: choice cancelled");
                        #endif
                        return;
                    }
                    extractToFolder = choice == FileAction.ExtractToFolder;
                    uploadItems = System.IO.Directory.EnumerateFileSystemEntries(extractDir).ToList();
                }

                if (uploadItems.Count == 0)
                {
                    OpProgressDialog.Complete();
                    await Task.Delay(400);
                    OpProgressDialog.Close();
                    _ = AlertDialogControl.ShowAsync("Nothing to extract.", AlertType.Info);
                    return;
                }

                // Upload gate: total extracted bytes (files already on disk — cheap local scan).
                long uploadBytes = uploadItems.Sum(item =>
                    System.IO.Directory.Exists(item)
                        ? System.IO.Directory.EnumerateFiles(item, "*", System.IO.SearchOption.AllDirectories).Sum(f => new System.IO.FileInfo(f).Length)
                        : new System.IO.FileInfo(item).Length);
                var driveRoot = PortalCore.DestinationDriveRoot(knownFolder);
                if (driveRoot != null && !await EnsureDiskSpaceAsync(driveRoot, uploadBytes))
                {
                    OpProgressDialog.Complete();
                    await Task.Delay(400);
                    OpProgressDialog.Close();
                    return;
                }

                // "Extract to folder": rename the extracted content into a folder named
                // after the archive, then upload it as a single tree.
                string uploadRoot = extractDir;
                List<string> overwriteNames;
                if (extractToFolder)
                {
                    string wrapper = System.IO.Path.Combine(staging, archiveName);
                    System.IO.Directory.Move(extractDir, wrapper);
                    uploadRoot = wrapper;
                    overwriteNames = new List<string> { archiveName };
                }
                else
                {
                    overwriteNames = uploadItems
                        .Select(i => System.IO.Path.GetFileName(i.TrimEnd('\\', '/')))
                        .ToList();
                }

                if (!await ConfirmPortalOverwriteOnceAsync(knownFolder, packageFullName, portalPath, overwriteNames))
                {
                    OpProgressDialog.Complete();
                    await Task.Delay(400);
                    OpProgressDialog.Close();
                    Log.Info("HandleExtractPortalZipAsync: skipped — overwrite declined");
                    return;
                }

                if (extractToFolder)
                {
                    // Dialog was closed for the choice; reopen for the upload phase.
                    OpProgressDialog.Show("Uploading to portal", entry.Name, knownFolder + portalPath);
                    await XFiles.FileSystem.PortalBrowser.UploadLocalToPortalAsync(
                        uploadRoot, knownFolder, packageFullName, portalPath, progress, OpProgressDialog.CancelToken);
                    OpProgressDialog.TrackCompleted(archiveName);
                }
                else
                {
                    OpProgressDialog.SetPhase("Uploading to portal", entry.Name, knownFolder + portalPath);
                    foreach (var item in uploadItems)
                    {
                        if (OpProgressDialog.CancelToken.IsCancellationRequested)
                        {
                            OpProgressDialog.Cancel();
                            await Task.Delay(1500);
                            OpProgressDialog.Close();
                            return;
                        }
                        try
                        {
                            await XFiles.FileSystem.PortalBrowser.UploadLocalToPortalAsync(
                                item, knownFolder, packageFullName, portalPath, progress, OpProgressDialog.CancelToken);
                            OpProgressDialog.TrackCompleted(System.IO.Path.GetFileName(item));
                        }
                        catch (Exception ex)
                        {
                            Log.Warn("HandleExtractPortalZipAsync: upload {Item} failed: {Message}", item, ex.Message);
                            OpProgressDialog.Complete();
                            await Task.Delay(400);
                            OpProgressDialog.Close();
                            _ = AlertDialogControl.ShowAsync($"Failed to upload \"{System.IO.Path.GetFileName(item)}\".\n\n{ex.Message}", AlertType.Error);
                            return;
                        }
                    }
                }

                OpProgressDialog.Complete();
                await Task.Delay(400);
                OpProgressDialog.Close();

                Log.Info("HandleExtractPortalZipAsync: success — '{Name}' extracted to portal {Known}/{Pkg}{Path}", entry.Name, knownFolder, packageFullName, portalPath);
                await _navigator.RefreshCurrentAsync();
            }
            catch (Exception ex)
            {
                Log.Err("HandleExtractPortalZipAsync: {Ex}", ex);
                OpProgressDialog.Close();
                _ = AlertDialogControl.ShowAsync($"Failed to extract \"{entry.Name}\".\n\n{ex.Message}", AlertType.Error);
            }
            finally
            {
                try { System.IO.Directory.Delete(staging, true); }
                catch (Exception ex) { Log.Warn("HandleExtractPortalZipAsync: cleanup failed: {Message}", ex.Message); }
            }
        }

        /// <summary>
        /// Creates a unique temp staging folder for portal zip operations.
        /// </summary>
        private static string CreatePortalOpStagingDir()
        {
            string dir = System.IO.Path.Combine(
                Windows.Storage.ApplicationData.Current.TemporaryFolder.Path,
                "portal-op-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(dir);
            return dir;
        }

        /// <summary>
        /// Lists the portal directory and, if any of the given names already exist,
        /// asks once (Overwrite / Skip) before the upload proceeds. Returns true when
        /// the upload may continue. Listing failures proceed (no false blocking).
        /// </summary>
        private async Task<bool> ConfirmPortalOverwriteOnceAsync(
            string knownFolder, string packageFullName, string portalPath, List<string> names)
        {
            try
            {
                var existing = await DevicePortalService.ListPortalFilesAsync(knownFolder, packageFullName ?? "", portalPath ?? "\\");
                string first = names.FirstOrDefault(n =>
                    existing.Any(e => string.Equals(e.Name, n, StringComparison.OrdinalIgnoreCase)));
                if (first == null) return true;

                UpdateFooterALabel("Select");
                int decision = await OverwriteDialogControl.ShowAsync(first);
                UpdateFooterALabelFromSelection();

                Log.Info("ConfirmPortalOverwriteOnceAsync: '{First}' exists on portal — decision={Decision}", first, decision);
                return decision != 0;
            }
            catch (Exception ex)
            {
                Log.Warn("ConfirmPortalOverwriteOnceAsync: listing failed — proceeding: {Message}", ex.Message);
                return true;
            }
        }

        /// <summary>
        /// Pastes portal clipboard entries into a local directory via REST download.
        /// </summary>
        private async Task HandlePastePortalToLocalAsync(string destDir)
        {
            var entries = ClipboardState.Entries;
            Log.Info("HandlePastePortalToLocalAsync: {Count} portal items → {Dest}", entries.Count, destDir);

            long required = entries
                .Where(e => e.IsPortal && !e.IsDirectory)
                .Sum(e => Math.Max(0, e.SizeBytes));
            if (required > 0 && !await EnsureDiskSpaceAsync(destDir, required))
            {
                #if BATCH_DEBUG
                Log.Verb("HandlePastePortalToLocalAsync: cancelled — insufficient free space");
                #endif
                return;
            }

            OpProgressDialog.Show("Copying", $"{entries.Count} items", destDir, 0, entries.Count);

            int failed = 0;
            foreach (var entry in entries)
            {
                if (!entry.IsPortal) continue;

                if (OpProgressDialog.CancelToken.IsCancellationRequested)
                {
                    Log.Dbg("HandlePastePortalToLocalAsync: cancelled");
                    OpProgressDialog.Cancel();
                    await Task.Delay(1500);
                    OpProgressDialog.Close();
                    UpdateClipboardIndicator();
                    await _navigator.RefreshCurrentAsync();
                    return;
                }

                var progress = new Progress<FileOperations.OperationProgress>(p =>
                {
                    OpProgressDialog.UpdateProgress(p);
                });

                try
                {
                    await XFiles.FileSystem.PortalBrowser.CopyPortalToLocalAsync(
                        XFiles.FileSystem.PortalBrowser.ToPortalEntry(entry),
                        destDir, progress, OpProgressDialog.CancelToken);
                    OpProgressDialog.TrackCompleted(entry.Name);
                    Log.Info("HandlePastePortalToLocalAsync: copied {Name}", entry.Name);
                }
                catch (Exception ex)
                {
                    failed++;
                    Log.Warn("HandlePastePortalToLocalAsync: {File} failed: {Message}", entry.Name, ex.Message);
                }
            }

            OpProgressDialog.Complete();
            await Task.Delay(400);
            OpProgressDialog.Close();

            if (failed > 0)
                _ = AlertDialogControl.ShowAsync($"Copy failed for {failed} item(s).{FailureSuffix()}", AlertType.Error);

            UpdateClipboardIndicator();
            await _navigator.RefreshCurrentAsync();
        }

        /// <summary>
        /// Initial path for the move destination dialog. Only real local folders are
        /// usable; portals and archives open the dialog at the drives root instead.
        /// </summary>
        private string MoveDialogInitialPath()
        {
            var current = _navigator.Current;
            return current != null && !current.IsPortal && !current.IsArchive
                ? current.Path
                : null;
        }

        private async Task HandleMoveAsync(FileEntry entry)
        {
            Log.Info("HandleMoveAsync: {File}", entry.FullPath);

            // Portal → local move: copy to chosen folder, then delete on the portal.
            if (entry.IsPortal)
            {
                await HandlePortalMoveAsync(entry);
                return;
            }

            // 1. Choose destination folder
            UpdateFooterALabel("Select");
            var destDir = await FolderBrowserDialogControl.ShowAsync(MoveDialogInitialPath());
            UpdateFooterALabelFromSelection();

            if (string.IsNullOrEmpty(destDir))
            {
                #if BATCH_DEBUG
                Log.Verb("HandleMoveAsync: cancelled at folder browser");
                #endif
                return;
            }

            // Don't move to same directory
            if (string.Equals(
                System.IO.Path.GetDirectoryName(entry.FullPath)?.TrimEnd('\\'),
                destDir.TrimEnd('\\'),
                StringComparison.OrdinalIgnoreCase))
            {
                        Log.Dbg("HandleMoveAsync: same directory, skipping");
                _ = AlertDialogControl.ShowAsync("Source and destination are the same folder.", AlertType.Error);
                return;
            }

            // 2. Build file list for confirmation
            var (files, folderCount) = await FileOperations.ListRecursiveAsync(entry.FullPath);
            UpdateFooterALabel("Confirm");
            bool confirmed = await FileOperationConfirmDialogControl.ShowMoveAsync(entry.Name, destDir, files, folderCount);
            UpdateFooterALabelFromSelection();

            if (!confirmed)
            {
                #if BATCH_DEBUG
                Log.Verb("HandleMoveAsync: confirmation cancelled");
                #endif
                return;
            }

            // 3. Execute move with progress
            var scan = await FileOperations.ScanEntriesAsync(new List<FileEntry> { entry });

            // Same-volume moves are renames — no free space consumed.
            if (!IsSameVolume(entry.FullPath, destDir) &&
                !await EnsureDiskSpaceAsync(destDir, scan.TotalBytes))
            {
                #if BATCH_DEBUG
                Log.Verb("HandleMoveAsync: cancelled — insufficient free space");
                #endif
                return;
            }

            var progress = new Progress<FileOperations.OperationProgress>(p =>
            {
                OpProgressDialog.UpdateProgress(p);
            });

            OpProgressDialog.Show("Moving", entry.Name, destDir,
                0, scan.FileCount);
            var moveSw = Stopwatch.StartNew();
            var result = await FileOperations.MoveAsync(entry.FullPath, destDir, progress,
                OpProgressDialog.CancelToken, BuildConflictCallback());
            moveSw.Stop();

            if (result == FileOperations.OperationResult.Cancelled)
            {
                    Log.Dbg("HandleMoveAsync: cancelled");
                OpProgressDialog.Cancel();
                await Task.Delay(1500);
                OpProgressDialog.Close();
                return;
            }

            OpProgressDialog.Complete();
            await Task.Delay(400);
            OpProgressDialog.Close();

            Log.Info("HandleMoveAsync: {File} -> {Dest} COMPLETE — {Bytes} bytes in {Elapsed:0.0}s (result={Result})",
                entry.Name, destDir, scan.TotalBytes, moveSw.Elapsed.TotalSeconds, result);

            if (result == FileOperations.OperationResult.Success)
            {
                Log.Info("HandleMoveAsync: success");
                await _navigator.RefreshCurrentAsync();
            }
            else
            {
                Log.Warn("HandleMoveAsync: failed");
                _ = AlertDialogControl.ShowAsync($"Failed to move \"{entry.Name}\".{FailureSuffix()}", AlertType.Error);
            }
        }

        private async Task HandlePortalMoveAsync(FileEntry entry)
        {
            Log.Info("HandlePortalMoveAsync: {Name}", entry.Name);

            // 1. Choose local destination folder
            UpdateFooterALabel("Select");
            var destDir = await FolderBrowserDialogControl.ShowAsync(MoveDialogInitialPath());
            UpdateFooterALabelFromSelection();

            if (string.IsNullOrEmpty(destDir))
            {
                #if BATCH_DEBUG
                Log.Verb("HandlePortalMoveAsync: cancelled at folder browser");
                #endif
                return;
            }

            // 2. Confirm
            UpdateFooterALabel("Confirm");
            bool confirmed = await FileOperationConfirmDialogControl.ShowMoveAsync(
                entry.Name, destDir, null, entry.IsDirectory ? 1 : 0);
            UpdateFooterALabelFromSelection();

            if (!confirmed)
            {
                #if BATCH_DEBUG
                Log.Verb("HandlePortalMoveAsync: confirmation cancelled");
                #endif
                return;
            }

            // 2.5 Check free space for single portal files
            if (!entry.IsDirectory)
            {
                long required = Math.Max(0, entry.SizeBytes);
                if (required > 0 && !await EnsureDiskSpaceAsync(destDir, required))
                {
                    #if BATCH_DEBUG
                    Log.Verb("HandlePortalMoveAsync: cancelled — insufficient free space");
                    #endif
                    return;
                }
            }

            // 3. Copy to local, then delete on the portal
            var portalEntry = XFiles.FileSystem.PortalBrowser.ToPortalEntry(entry);
            var progress = new Progress<FileOperations.OperationProgress>(p =>
            {
                OpProgressDialog.UpdateProgress(p);
            });

            OpProgressDialog.Show("Moving", entry.Name, destDir);
            try
            {
                await XFiles.FileSystem.PortalBrowser.CopyPortalToLocalAsync(
                    portalEntry, destDir, progress, OpProgressDialog.CancelToken);
                await DevicePortalService.DeletePortalEntryAsync(portalEntry);

                OpProgressDialog.Complete();
                await Task.Delay(400);
                OpProgressDialog.Close();
                Log.Info("HandlePortalMoveAsync: success");
                await _navigator.RefreshCurrentAsync();
            }
            catch (Exception ex)
            {
                Log.Err("HandlePortalMoveAsync: failed", ex);
                OpProgressDialog.Close();
                _ = AlertDialogControl.ShowAsync($"Failed to move \"{entry.Name}\": {ex.Message}", AlertType.Error);
            }
        }

        private async Task HandleRenameAsync(FileEntry entry)
        {
            Log.Info("HandleRenameAsync: {File}", entry.FullPath);
            var newName = await InputDialogControl.ShowAsync("Rename", entry.Name);
            if (string.IsNullOrEmpty(newName) || newName == entry.Name)
            {
                #if BATCH_DEBUG
                Log.Verb("HandleRenameAsync: cancelled or unchanged");
                #endif
                return;
            }

            var invalidChars = new char[] { '\\', '/', ':', '*', '?', '"', '<', '>', '|' };
            if (newName.IndexOfAny(invalidChars) >= 0)
            {
                Log.Warn("HandleRenameAsync: invalid characters in name");
                CurrentStatus.Text = "Invalid characters in name";
                return;
            }

            var reservedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "CON", "PRN", "AUX", "NUL",
                "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
                "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
            };
            var nameNoExt = Path.GetFileNameWithoutExtension(newName);
            if (reservedNames.Contains(nameNoExt))
            {
                Log.Warn("HandleRenameAsync: reserved name");
                CurrentStatus.Text = "Reserved name";
                return;
            }

            if (entry.IsPortal)
            {
                await HandlePortalRenameAsync(entry, newName);
                return;
            }

            if (entry.IsNetwork)
            {
                await HandleNetworkRenameAsync(entry, newName);
                return;
            }

            // Pre-flight: check write permission before prompting
            var writeError = FileOperations.CheckWritable(entry.FullPath, entry.IsDirectory);
            if (writeError != null)
            {
                Log.Warn("HandleRenameAsync: not writable — {Reason}", writeError);
                _ = AlertDialogControl.ShowAsync($"Cannot rename \"{entry.Name}\"\n\n{writeError}", AlertType.Error);
                return;
            }

            var confirmed = await AlertDialogControl.ShowConfirmAsync($"Rename '{entry.Name}' to '{newName}'?");
            if (!confirmed)
            {
                #if BATCH_DEBUG
                Log.Verb("HandleRenameAsync: confirmation cancelled");
                #endif
                return;
            }

            var result = await FileOperations.RenameAsync(entry.FullPath, newName);
            if (result == FileOperations.OperationResult.Success)
            {
                Log.Info("HandleRenameAsync: success — refreshing");
                await _navigator.RefreshCurrentAsync(newName);
            }
            else
            {
                Log.Warn("HandleRenameAsync: failed");
                _ = AlertDialogControl.ShowAsync($"Failed to rename \"{entry.Name}\".{FailureSuffix()}", AlertType.Error);
            }
        }

        private async Task HandleRenameLocationAsync(FileEntry entry)
        {
            Log.Info("HandleRenameLocationAsync: location id={Id}", entry.NetworkLocationId);

            var config = await NetworkServerManager.GetAsync((int)entry.NetworkLocationId);
            if (config == null)
            {
                Log.Warn("HandleRenameLocationAsync: location not found");
                _ = AlertDialogControl.ShowAsync("Saved location not found.", AlertType.Error);
                return;
            }

            UpdateFooterALabel("Select");
            var result = await NetworkLocationDialogControl.ShowAsync("Edit Network Location", config, isEdit: true);
            UpdateFooterALabelFromSelection();
            if (result == null)
            {
                #if BATCH_DEBUG
                Log.Verb("HandleRenameLocationAsync: cancelled");
                #endif
                return;
            }

            await NetworkServerManager.UpdateAsync(config.Id, result.Config,
                result.PasswordEdited ? result.Password : null);
            Log.Info("HandleRenameLocationAsync: updated {Url}", NetworkUrl.Compose(result.Config));
            await _navigator.RefreshCurrentAsync();
        }

        private async Task HandleDeleteLocationAsync(FileEntry entry)
        {
            Log.Info("HandleDeleteLocationAsync: location id={Id}", entry.NetworkLocationId);

            bool confirmed = await AlertDialogControl.ShowConfirmAsync($"Delete network location '{entry.Name}'?");
            if (!confirmed)
            {
                #if BATCH_DEBUG
                Log.Verb("HandleDeleteLocationAsync: cancelled");
                #endif
                return;
            }

            await NetworkServerManager.RemoveAsync((int)entry.NetworkLocationId);
            Log.Info("HandleDeleteLocationAsync: removed location");
            await _navigator.RefreshCurrentAsync();
        }

        private async Task HandleNetworkRenameAsync(FileEntry entry, string newName)
        {
            Log.Info("HandleNetworkRenameAsync: {Name} → {New} (location={Id})",
                entry.Name, newName, entry.NetworkLocationId);
            bool confirmed = await AlertDialogControl.ShowConfirmAsync($"Rename '{entry.Name}' to '{newName}'?");
            if (!confirmed)
            {
                #if BATCH_DEBUG
                Log.Verb("HandleNetworkRenameAsync: confirmation cancelled");
                #endif
                return;
            }

            try
            {
                var config = await _navigator.GetNetworkConfigAsync(entry.NetworkLocationId);
                if (config == null) return;
                await _navigator.BrowserFor(config.Protocol).RenameFileAsync(config, entry.NetworkShareName,
                    entry.NetworkPath, newName, entry.IsDirectory, CancellationToken.None);
                Log.Info("HandleNetworkRenameAsync: success — refreshing");
                await _navigator.RefreshCurrentAsync(newName);
            }
            catch (NetworkOperationException ex)
            {
                Log.Warn($"HandleNetworkRenameAsync: {entry.Name} — {ex.Reason} ({ex.Message})");
                var hint = ex.Reason == NetworkOperationReason.AccessDenied
                    ? "\n\n" + NetworkOperationException.FriendlyMessage(ex.Reason)
                    : "";
                _ = AlertDialogControl.ShowAsync($"Cannot rename \"{entry.Name}\"\n\n{ex.Message}{hint}", AlertType.Error);
            }
            catch (Exception ex)
            {
                Log.Err("HandleNetworkRenameAsync: failed", ex);
                _ = AlertDialogControl.ShowAsync($"Failed to rename \"{entry.Name}\".\n\n{ex.Message}", AlertType.Error);
            }
        }

        private async Task HandlePortalRenameAsync(FileEntry entry, string newName)
        {
            Log.Info("HandlePortalRenameAsync: {Name} → {New}", entry.Name, newName);
            bool confirmed = await AlertDialogControl.ShowConfirmAsync($"Rename '{entry.Name}' to '{newName}'?");
            if (!confirmed)
            {
                #if BATCH_DEBUG
                Log.Verb("HandlePortalRenameAsync: confirmation cancelled");
                #endif
                return;
            }

            try
            {
                await DevicePortalService.RenamePortalEntryAsync(
                    XFiles.FileSystem.PortalBrowser.ToPortalEntry(entry), newName);
                Log.Info("HandlePortalRenameAsync: success — refreshing");
                await _navigator.RefreshCurrentAsync(newName);
            }
            catch (Exception ex)
            {
                Log.Err("HandlePortalRenameAsync: failed", ex);
                _ = AlertDialogControl.ShowAsync($"Failed to rename \"{entry.Name}\".\n\n{ex.Message}", AlertType.Error);
            }
        }

        private async Task HandleDeleteAsync(FileEntry entry)
        {
            Log.Info("HandleDeleteAsync: {File}", entry.FullPath);

            if (entry.IsPortal)
            {
                await HandlePortalDeleteAsync(entry);
                return;
            }

            if (entry.IsNetwork)
            {
                await HandleNetworkDeleteAsync(entry);
                return;
            }

            // Pre-flight: check parent directory write permission before prompting
            var parentDir = Path.GetDirectoryName(entry.FullPath);
            if (!string.IsNullOrEmpty(parentDir))
            {
                var writeError = FileOperations.CheckWritable(parentDir, true);
                if (writeError != null)
                {
                    Log.Warn("HandleDeleteAsync: not writable — {Reason}", writeError);
                    _ = AlertDialogControl.ShowAsync($"Cannot delete \"{entry.Name}\"\n\n{writeError}", AlertType.Error);
                    return;
                }
            }

            // Build file list for confirmation dialog
            var (files, folderCount) = await FileOperations.ListRecursiveAsync(entry.FullPath);
            bool confirmed = await FileOperationConfirmDialogControl.ShowAsync(
                entry.Name, entry.IsDirectory, files, folderCount);
            if (!confirmed)
            {
                #if BATCH_DEBUG
                Log.Verb("HandleDeleteAsync: confirmation cancelled");
                #endif
                return;
            }

            FileOperations.OperationResult result;
            if (entry.IsDirectory)
            {
                result = await FileOperations.DeleteDirectoryAsync(entry.FullPath);
            }
            else
            {
                result = await FileOperations.DeleteAsync(entry.FullPath);
            }

            if (result == FileOperations.OperationResult.Success)
            {
                Log.Info("HandleDeleteAsync: success — refreshing");
                await _navigator.RefreshCurrentAsync();
            }
            else
            {
                Log.Warn("HandleDeleteAsync: failed");
                _ = AlertDialogControl.ShowAsync($"Failed to delete \"{entry.Name}\".{FailureSuffix()}", AlertType.Error);
            }
        }

        private async Task HandleNetworkDeleteAsync(FileEntry entry)
        {
            Log.Info("HandleNetworkDeleteAsync: {Name} (location={Id})", entry.Name, entry.NetworkLocationId);
            bool confirmed = await FileOperationConfirmDialogControl.ShowAsync(
                entry.Name, entry.IsDirectory, null, entry.IsDirectory ? 1 : 0);
            if (!confirmed)
            {
                #if BATCH_DEBUG
                Log.Verb("HandleNetworkDeleteAsync: confirmation cancelled");
                #endif
                return;
            }

            try
            {
                var config = await _navigator.GetNetworkConfigAsync(entry.NetworkLocationId);
                if (config == null) return;
                await NetworkCopyService.DeleteRemoteAsync(_navigator.BrowserFor(config.Protocol), config,
                    entry.NetworkShareName, entry.NetworkPath, entry.IsDirectory, CancellationToken.None);
                Log.Info("HandleNetworkDeleteAsync: success — refreshing");
                await _navigator.RefreshCurrentAsync();
            }
            catch (NetworkOperationException ex)
            {
                Log.Warn($"HandleNetworkDeleteAsync: {entry.Name} — {ex.Reason} ({ex.Message})");
                var hint = ex.Reason == NetworkOperationReason.AccessDenied
                    ? "\n\n" + NetworkOperationException.FriendlyMessage(ex.Reason)
                    : "";
                _ = AlertDialogControl.ShowAsync($"Cannot delete \"{entry.Name}\"\n\n{ex.Message}{hint}", AlertType.Error);
            }
            catch (Exception ex)
            {
                Log.Err("HandleNetworkDeleteAsync: failed", ex);
                _ = AlertDialogControl.ShowAsync($"Failed to delete \"{entry.Name}\".\n\n{ex.Message}", AlertType.Error);
            }
        }

        private async Task HandlePortalDeleteAsync(FileEntry entry)
        {
            Log.Info("HandlePortalDeleteAsync: {Name}", entry.Name);
            bool confirmed = await FileOperationConfirmDialogControl.ShowAsync(
                entry.Name, entry.IsDirectory, null, entry.IsDirectory ? 1 : 0);
            if (!confirmed)
            {
                #if BATCH_DEBUG
                Log.Verb("HandlePortalDeleteAsync: confirmation cancelled");
                #endif
                return;
            }

            try
            {
                await DevicePortalService.DeletePortalEntryAsync(
                    XFiles.FileSystem.PortalBrowser.ToPortalEntry(entry));
                Log.Info("HandlePortalDeleteAsync: success — refreshing");
                await _navigator.RefreshCurrentAsync();
            }
            catch (Exception ex)
            {
                Log.Err("HandlePortalDeleteAsync: failed", ex);
                _ = AlertDialogControl.ShowAsync($"Failed to delete \"{entry.Name}\".\n\n{ex.Message}", AlertType.Error);
            }
        }

        private async Task HandleExtractAsync(FileEntry entry)
        {
            Log.Info("HandleExtractAsync: {File}", entry.FullPath);
            var currentPath = _navigator.Current?.Path;
            if (string.IsNullOrEmpty(currentPath)) return;

            var archiveName = System.IO.Path.GetFileNameWithoutExtension(entry.Name);

            // Smart unzip: if archive has a single root folder, extract here directly
            string rootFolder = await Task.Run(() => FileOperations.GetSingleRootFolder(entry.FullPath));
            bool singleRoot = rootFolder != null;
            string selectAfter = null;

            if (singleRoot)
            {
                Log.Info("HandleExtractAsync: single root folder '{Folder}' — extracting here directly", rootFolder);
                selectAfter = rootFolder;
            }
            else
            {
                var choice = await FileActionSheetControl.ShowExtractChoiceAsync(archiveName);
                if (choice == null)
                {
                    #if BATCH_DEBUG
                    Log.Verb("HandleExtractAsync: choice cancelled");
                    #endif
                    return;
                }

                if (choice == FileAction.ExtractToFolder)
                    selectAfter = archiveName;
            }

            var destDir = singleRoot
                ? currentPath
                : System.IO.Path.Combine(currentPath, archiveName);

            // If a FILE already occupies the destination folder name (common when a ROM
            // zip sits next to its loose file, e.g. extracting "X.iso.zip" next to an
            // existing "X.iso"), divert to "name (1)" instead of silently failing to
            // create the folder.
            if (!singleRoot && destDir != currentPath)
            {
                string adjusted = FileOperations.GetAvailableFolderPath(destDir);
                if (!string.Equals(adjusted, destDir, System.StringComparison.OrdinalIgnoreCase))
                {
                    Log.Warn("HandleExtractAsync: destination '{Dest}' is occupied by a file — using '{Adjusted}'", destDir, adjusted);
                    destDir = adjusted;
                    selectAfter = System.IO.Path.GetFileName(adjusted);
                }
            }

            var progress = new Progress<FileOperations.OperationProgress>(p =>
            {
                OpProgressDialog.UpdateProgress(p);
            });

            // Conflict callback: shows OverwriteDialog on UI thread, returns 0=skip/1=overwrite/2=all
            var conflictCallback = new Func<string, Task<int>>(conflictFileName =>
            {
                var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
                _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, async () =>
                {
                    try
                    {
                        int decision = await OverwriteDialogControl.ShowAsync(conflictFileName);
                        tcs.TrySetResult(decision);
                    }
                    catch (Exception ex)
                    {
                        Log.Warn("OverwriteDialog error", ex);
                        tcs.TrySetResult(0); // Skip on error
                    }
                });
                return tcs.Task;
            });

            long required = await FileOperations.GetArchiveUncompressedSizeAsync(entry.FullPath);
            if (!await EnsureDiskSpaceAsync(destDir, required))
            {
                #if BATCH_DEBUG
                Log.Verb("HandleExtractAsync: cancelled — insufficient free space");
                #endif
                return;
            }

            OpProgressDialog.Show("Extracting", entry.Name, destDir);
            var extract = await FileOperations.ExtractAsync(entry.FullPath, destDir, progress, conflictCallback, OpProgressDialog.CancelToken);
            var result = extract.Result;

            if (result == FileOperations.OperationResult.Cancelled)
            {
                Log.Info("HandleExtractAsync: cancelled");
                OpProgressDialog.Cancel();
                await Task.Delay(1500);
                OpProgressDialog.Close();
                return;
            }

            OpProgressDialog.Complete();
            await Task.Delay(400);
            OpProgressDialog.Close();

            if (result == FileOperations.OperationResult.Success)
            {
                Log.Info("HandleExtractAsync: success — selecting {Select}", selectAfter ?? "(none)");
                await _navigator.RefreshCurrentAsync(selectAfter);
            }
            else
            {
                string msg = string.IsNullOrEmpty(extract.ErrorMessage)
                    ? $"Failed to extract \"{entry.Name}\".{FailureSuffix()}"
                    : $"Failed to extract \"{entry.Name}\".\n\n{extract.ErrorMessage}";
                Log.Warn("HandleExtractAsync: failed — {Reason}", extract.ErrorMessage ?? "(no detail)");
                _ = AlertDialogControl.ShowAsync(msg, AlertType.Error);
            }
        }

        private async Task HandleExtractFileAsync(FileEntry entry)
        {
            Log.Info("HandleExtractFileAsync: {Archive}|{Internal}",
                entry.ArchiveRootPath, entry.ArchiveInternalPath);

            if (string.IsNullOrEmpty(entry.ArchiveRootPath) || string.IsNullOrEmpty(entry.ArchiveInternalPath))
            {
                Log.Warn("HandleExtractFileAsync: missing archive path info");
                return;
            }

            // Extracting a file from inside a portal ZIP: write to temp, then upload
            // to the ZIP's parent portal folder (the cached copy is not a real target).
            var current = _navigator.Current;
            if (current != null && current.IsArchive && !string.IsNullOrEmpty(current.PortalKnownFolder))
            {
                await HandleExtractFileFromPortalZipAsync(entry);
                return;
            }

            // Extracting a file from inside a remote (SMB) ZIP: write to temp, then
            // upload back to the ZIP's parent remote folder — mirrors the portal flow.
            if (current != null && current.IsArchive && current.IsNetwork)
            {
                await HandleExtractFileFromNetworkZipAsync(entry);
                return;
            }

            var destDir = System.IO.Path.GetDirectoryName(entry.ArchiveRootPath);
            if (string.IsNullOrEmpty(destDir)) return;

            var fileName = System.IO.Path.GetFileName(entry.ArchiveInternalPath);

            // Conflict callback: shows OverwriteDialog on UI thread, returns 0=skip/1=overwrite/2=all
            var conflictCallback = new Func<string, Task<int>>(conflictFileName =>
            {
                var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
                _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, async () =>
                {
                    try
                    {
                        int decision = await OverwriteDialogControl.ShowAsync(conflictFileName);
                        tcs.TrySetResult(decision);
                    }
                    catch (Exception ex)
                    {
                        Log.Warn("OverwriteDialog error", ex);
                        tcs.TrySetResult(0);
                    }
                });
                return tcs.Task;
            });

            long required = await FileOperations.GetArchiveEntryUncompressedSizeAsync(
                entry.ArchiveRootPath, entry.ArchiveInternalPath);
            if (!await EnsureDiskSpaceAsync(destDir, required))
            {
                #if BATCH_DEBUG
                Log.Verb("HandleExtractFileAsync: cancelled — insufficient free space");
                #endif
                return;
            }

            OpProgressDialog.Show("Extracting", fileName, destDir);
            // Progress<T> marshals to the UI thread via its captured SynchronizationContext.
            var progress = new Progress<FileOperations.OperationProgress>(p =>
            {
                OpProgressDialog.UpdateProgress(p);
            });
            var result = await FileOperations.ExtractFileAsync(
                entry.ArchiveRootPath, entry.ArchiveInternalPath, destDir, conflictCallback, OpProgressDialog.CancelToken, progress);

            if (result == FileOperations.OperationResult.Cancelled)
            {
                Log.Info("HandleExtractFileAsync: cancelled");
                OpProgressDialog.Cancel();
                await Task.Delay(1500);
                OpProgressDialog.Close();
                return;
            }

            OpProgressDialog.Complete();
            await Task.Delay(400);
            OpProgressDialog.Close();

            if (result == FileOperations.OperationResult.Success)
            {
                Log.Info("HandleExtractFileAsync: success — selecting {File}", fileName);
                await _navigator.RefreshCurrentAsync(selectName: fileName);
            }
            else
            {
                Log.Warn("HandleExtractFileAsync: failed");
                _ = AlertDialogControl.ShowAsync($"Failed to extract \"{fileName}\".{FailureSuffix()}", AlertType.Error);
            }
        }

        /// <summary>
        /// Extracts a single file from inside a portal ZIP and uploads it to the ZIP's
        /// parent portal folder. The archive column's cached copy is never the target —
        /// the file goes back to the console.
        /// </summary>
        private async Task HandleExtractFileFromPortalZipAsync(FileEntry entry)
        {
            var current = _navigator.Current;
            string knownFolder = current.PortalKnownFolder;
            string packageFullName = current.PortalPackageFullName ?? "";
            string portalPath = current.PortalPath ?? "\\";
            string fileName = System.IO.Path.GetFileName(entry.ArchiveInternalPath);

            Log.Info("HandleExtractFileFromPortalZipAsync: {Archive}|{Internal} → portal {Known}/{Pkg}{Path}",
                entry.ArchiveRootPath, entry.ArchiveInternalPath, knownFolder, packageFullName, portalPath);

            string staging = CreatePortalOpStagingDir();
            try
            {
                var progress = new Progress<FileOperations.OperationProgress>(p =>
                {
                    OpProgressDialog.UpdateProgress(p);
                });

                OpProgressDialog.Show("Extracting", fileName, staging);
                var result = await FileOperations.ExtractFileAsync(
                    entry.ArchiveRootPath, entry.ArchiveInternalPath, staging, null, OpProgressDialog.CancelToken, progress);
                if (result == FileOperations.OperationResult.Cancelled)
                {
                    OpProgressDialog.Cancel();
                    await Task.Delay(1500);
                    OpProgressDialog.Close();
                    return;
                }
                if (result != FileOperations.OperationResult.Success)
                {
                    OpProgressDialog.Complete();
                    await Task.Delay(400);
                    OpProgressDialog.Close();
                    _ = AlertDialogControl.ShowAsync($"Failed to extract \"{fileName}\".{FailureSuffix()}", AlertType.Error);
                    return;
                }

                string extractedPath = System.IO.Path.Combine(staging, fileName);
                if (!System.IO.File.Exists(extractedPath))
                {
                    OpProgressDialog.Complete();
                    await Task.Delay(400);
                    OpProgressDialog.Close();
                    _ = AlertDialogControl.ShowAsync($"Extracted file not found: \"{fileName}\".{FailureSuffix()}", AlertType.Error);
                    return;
                }

                long fileSize = new System.IO.FileInfo(extractedPath).Length;
                var driveRoot = PortalCore.DestinationDriveRoot(knownFolder);
                if (driveRoot != null && !await EnsureDiskSpaceAsync(driveRoot, fileSize))
                {
                    OpProgressDialog.Complete();
                    await Task.Delay(400);
                    OpProgressDialog.Close();
                    return;
                }

                if (!await ConfirmPortalOverwriteOnceAsync(knownFolder, packageFullName, portalPath, new List<string> { fileName }))
                {
                    OpProgressDialog.Complete();
                    await Task.Delay(400);
                    OpProgressDialog.Close();
                    Log.Info("HandleExtractFileFromPortalZipAsync: skipped — overwrite declined");
                    return;
                }

                OpProgressDialog.SetPhase("Uploading to portal", fileName, knownFolder + portalPath);
                await XFiles.FileSystem.PortalBrowser.UploadLocalToPortalAsync(
                    extractedPath, knownFolder, packageFullName, portalPath, progress, OpProgressDialog.CancelToken);

                OpProgressDialog.Complete();
                await Task.Delay(400);
                OpProgressDialog.Close();

                Log.Info("HandleExtractFileFromPortalZipAsync: uploaded {File} to portal {Known}/{Pkg}{Path}", fileName, knownFolder, packageFullName, portalPath);
            }
            catch (Exception ex)
            {
                Log.Err("HandleExtractFileFromPortalZipAsync: {Ex}", ex);
                OpProgressDialog.Close();
                _ = AlertDialogControl.ShowAsync($"Failed to extract \"{fileName}\" to the portal.\n\n{ex.Message}", AlertType.Error);
            }
            finally
            {
                try { System.IO.Directory.Delete(staging, true); }
                catch (Exception ex) { Log.Warn("HandleExtractFileFromPortalZipAsync: cleanup failed: {Message}", ex.Message); }
            }
        }

        private async Task HandleExtractFileFromNetworkZipAsync(FileEntry entry)
        {
            var current = _navigator.Current;
            long locationId = current.NetworkLocationId;
            string share = current.NetworkShareName;
            string zipRemotePath = current.NetworkPath ?? "";
            string fileName = System.IO.Path.GetFileName(entry.ArchiveInternalPath);

            string parentRemoteDir = NetworkPathUtil.Parent(zipRemotePath);
            string destRemotePath = NetworkPathUtil.Join(parentRemoteDir, fileName);

            Log.Info("HandleExtractFileFromNetworkZipAsync: {Archive}|{Internal} → {Share}\\{Dest}",
                entry.ArchiveRootPath, entry.ArchiveInternalPath, share, destRemotePath);

            string staging = CreatePortalOpStagingDir();
            try
            {
                var progress = new Progress<FileOperations.OperationProgress>(p =>
                {
                    OpProgressDialog.UpdateProgress(p);
                });

                OpProgressDialog.Show("Extracting", fileName, parentRemoteDir);

                // Read the entry straight from the stream-backed archive cache — no
                // full ZIP download. Copy it to staging, then upload to the parent
                // remote folder (mirrors the portal flow).
                string stagingPath = System.IO.Path.Combine(staging, fileName);
                using (Stream src = _navigator.ArchiveBrowser.OpenEntryStream(entry.ArchiveRootPath, entry.ArchiveInternalPath))
                {
                    if (src == null)
                    {
                        OpProgressDialog.Complete();
                        await Task.Delay(400);
                        OpProgressDialog.Close();
                        _ = AlertDialogControl.ShowAsync($"Failed to read \"{fileName}\" from the remote archive.{FailureSuffix()}", AlertType.Error);
                        return;
                    }

                    using (var dst = System.IO.File.Create(stagingPath))
                    {
                        await src.CopyToAsync(dst);
                    }
                }

                if (string.IsNullOrEmpty(parentRemoteDir))
                {
                    OpProgressDialog.Complete();
                    await Task.Delay(400);
                    OpProgressDialog.Close();
                    _ = AlertDialogControl.ShowAsync($"Cannot extract \"{fileName}\" — the ZIP is at the share root.{FailureSuffix()}", AlertType.Error);
                    return;
                }

                // Overwrite check on the remote destination.
                var config = await _navigator.GetNetworkConfigAsync(locationId);
                if (config == null)
                {
                    OpProgressDialog.Complete();
                    await Task.Delay(400);
                    OpProgressDialog.Close();
                    return;
                }

                bool exists = false;
                try
                {
                    exists = await _navigator.BrowserFor(config.Protocol).EntryExistsAsync(
                        config, share, destRemotePath, false, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    Log.Warn("HandleExtractFileFromNetworkZipAsync: existence check failed: {Message}", ex.Message);
                }

                if (exists)
                {
                    int decision = await OverwriteDialogControl.ShowAsync(fileName);
                    if (decision == 0)
                    {
                        OpProgressDialog.Complete();
                        await Task.Delay(400);
                        OpProgressDialog.Close();
                        Log.Info("HandleExtractFileFromNetworkZipAsync: skipped — overwrite declined");
                        return;
                    }
                }

                OpProgressDialog.SetPhase("Uploading to server", fileName, parentRemoteDir);
                bool uploaded = await _navigator.WriteNetworkFileAsync(locationId, share, destRemotePath, stagingPath);

                if (!uploaded)
                {
                    OpProgressDialog.Complete();
                    await Task.Delay(400);
                    OpProgressDialog.Close();
                    _ = AlertDialogControl.ShowAsync($"Failed to upload \"{fileName}\" to the server.{FailureSuffix()}", AlertType.Error);
                    return;
                }

                OpProgressDialog.Complete();
                await Task.Delay(400);
                OpProgressDialog.Close();
                Log.Info("HandleExtractFileFromNetworkZipAsync: uploaded {File} to {Share}\\{Dest}", fileName, share, destRemotePath);
            }
            catch (Exception ex)
            {
                Log.Err("HandleExtractFileFromNetworkZipAsync: {Ex}", ex);
                OpProgressDialog.Close();
                _ = AlertDialogControl.ShowAsync($"Failed to extract \"{fileName}\" to the server.\n\n{ex.Message}", AlertType.Error);
            }
            finally
            {
                try { System.IO.Directory.Delete(staging, true); }
                catch (Exception ex) { Log.Warn("HandleExtractFileFromNetworkZipAsync: cleanup failed: {Message}", ex.Message); }
            }
        }

        private async Task HandleCreateFolderAsync(FileEntry entry)
        {
            Log.Info("HandleCreateFolderAsync: {File}", entry?.Name ?? "(none)");

            var current = _navigator.Current;
            var targetDir = current?.Path;
            if (string.IsNullOrEmpty(targetDir) && current?.IsPortal != true)
            {
                Log.Warn("HandleCreateFolderAsync: no target directory");
                return;
            }

            // Debounce: suggest unique name if "New Folder" already exists
            var entries = _navigator.Current?.Entries;
            string defaultName = "New Folder";
            if (entries != null)
            {
                int counter = 1;
                while (entries.Any(e => string.Equals(e.Name, defaultName, StringComparison.OrdinalIgnoreCase)))
                {
                    defaultName = $"New Folder ({counter})";
                    counter++;
                }
            }

            var folderName = await InputDialogControl.ShowAsync("New Folder", defaultName);
            if (string.IsNullOrEmpty(folderName))
            {
                #if BATCH_DEBUG
                Log.Verb("HandleCreateFolderAsync: name cancelled");
                #endif
                return;
            }

            if (_navigator.Current?.IsPortal == true)
            {
                await HandlePortalCreateFolderAsync(entry, folderName);
                return;
            }

            if (_navigator.Current?.IsNetwork == true)
            {
                await HandleNetworkCreateFolderAsync(folderName);
                return;
            }

            // Pre-flight: check parent directory write permission
            var writeError = FileOperations.CheckWritable(targetDir, true);
            if (writeError != null)
            {
                Log.Warn("HandleCreateFolderAsync: not writable — {Reason}", writeError);
                _ = AlertDialogControl.ShowAsync($"Cannot create folder \"{folderName}\"\n\n{writeError}", AlertType.Error);
                return;
            }

            var fullPath = System.IO.Path.Combine(targetDir, folderName);
            var result = await FileOperations.CreateFolderAsync(fullPath);
            if (result == FileOperations.OperationResult.Success)
            {
                Log.Info("HandleCreateFolderAsync: success — refreshing and selecting '{Name}'", folderName);
                await _navigator.RefreshCurrentAsync(selectName: folderName);
            }
            else
            {
                Log.Warn("HandleCreateFolderAsync: failed");
                _ = AlertDialogControl.ShowAsync($"Failed to create folder \"{folderName}\".{FailureSuffix()}", AlertType.Error);
            }
        }

        private async Task HandlePortalCreateFolderAsync(FileEntry entry, string folderName)
        {
            var current = _navigator.Current;
            Log.Info("HandlePortalCreateFolderAsync: '{Name}' in {Known}/{Pkg}{Path}",
                folderName, current?.PortalKnownFolder, current?.PortalPackageFullName, current?.PortalPath);
            try
            {
                await DevicePortalService.CreatePortalFolderAsync(
                    current?.PortalKnownFolder ?? "",
                    current?.PortalPackageFullName ?? "",
                    current?.PortalPath ?? "\\",
                    folderName);
                Log.Info("HandlePortalCreateFolderAsync: success — refreshing and selecting '{Name}'", folderName);
                await _navigator.RefreshCurrentAsync(selectName: folderName);
            }
            catch (Exception ex)
            {
                Log.Err("HandlePortalCreateFolderAsync: failed", ex);
                _ = AlertDialogControl.ShowAsync($"Failed to create folder \"{folderName}\".\n\n{ex.Message}", AlertType.Error);
            }
        }

        private async Task HandleNetworkCreateFolderAsync(string folderName)
        {
            var current = _navigator.Current;
            if (string.IsNullOrEmpty(current?.NetworkShareName))
            {
                _ = AlertDialogControl.ShowAsync("Open a shared folder before creating one.", AlertType.Info);
                return;
            }
            Log.Info("HandleNetworkCreateFolderAsync: '{Name}' in {Share}/{Path}",
                folderName, current?.NetworkShareName, current?.NetworkPath);
            try
            {
                var config = await _navigator.GetNetworkConfigAsync(current.NetworkLocationId);
                if (config == null) return;
                string basePath = (current.NetworkPath ?? "").TrimEnd('\\');
                string newPath = string.IsNullOrEmpty(basePath) ? folderName : basePath + "\\" + folderName;
                await _navigator.BrowserFor(config.Protocol).CreateDirectoryAsync(config,
                    current.NetworkShareName, newPath, CancellationToken.None);
                Log.Info("HandleNetworkCreateFolderAsync: success — refreshing and selecting '{Name}'", folderName);
                await _navigator.RefreshCurrentAsync(selectName: folderName);
            }
            catch (NetworkOperationException ex)
            {
                Log.Warn($"HandleNetworkCreateFolderAsync: {folderName} — {ex.Reason} ({ex.Message})");
                var hint = ex.Reason == NetworkOperationReason.AccessDenied
                    ? "\n\n" + NetworkOperationException.FriendlyMessage(ex.Reason)
                    : "";
                _ = AlertDialogControl.ShowAsync($"Cannot create folder \"{folderName}\"\n\n{ex.Message}{hint}", AlertType.Error);
            }
            catch (Exception ex)
            {
                Log.Err("HandleNetworkCreateFolderAsync: failed", ex);
                _ = AlertDialogControl.ShowAsync($"Failed to create folder \"{folderName}\".\n\n{ex.Message}", AlertType.Error);
            }
        }

        private async Task HandleCreateZipAsync(FileEntry entry)
        {
            Log.Info("HandleCreateZipAsync: {File}", entry.FullPath);
            var zipName = await InputDialogControl.ShowAsync("Create ZIP", entry.Name + ".zip");
            if (string.IsNullOrEmpty(zipName))
            {
                #if BATCH_DEBUG
                Log.Verb("HandleCreateZipAsync: cancelled");
                #endif
                return;
            }

            var currentPath = _navigator.Current?.Path;
            if (string.IsNullOrEmpty(currentPath)) return;
            var zipPath = System.IO.Path.Combine(currentPath, zipName);
            Log.Info("HandleCreateZipAsync: zipPath={Zip}", zipPath);

            var scanZip = await FileOperations.ScanEntriesAsync(new List<FileEntry> { entry });
            if (!await EnsureDiskSpaceAsync(currentPath, scanZip.TotalBytes))
            {
                #if BATCH_DEBUG
                Log.Verb("HandleCreateZipAsync: cancelled — insufficient free space");
                #endif
                return;
            }

            OpProgressDialog.Show("Creating ZIP", entry.Name, zipPath);
            var progress = new Progress<FileOperations.OperationProgress>(p =>
            {
                OpProgressDialog.UpdateProgress(p);
            });
            var result = await FileOperations.CreateZipAsync(entry.FullPath, zipPath, progress, OpProgressDialog.CancelToken);

            if (result == FileOperations.OperationResult.Cancelled)
            {
                Log.Info("HandleCreateZipAsync: cancelled");
                OpProgressDialog.Cancel();
                await Task.Delay(1500);
                OpProgressDialog.Close();
                return;
            }

            OpProgressDialog.Complete();
            await Task.Delay(400);
            OpProgressDialog.Close();

            if (result == FileOperations.OperationResult.Success)
            {
                Log.Info("HandleCreateZipAsync: success — selecting '{Name}'", zipName);
                await _navigator.RefreshCurrentAsync(selectName: zipName);
            }
            else
            {
                Log.Warn("HandleCreateZipAsync: failed");
                _ = AlertDialogControl.ShowAsync($"Failed to create ZIP \"{zipName}\".{FailureSuffix()}", AlertType.Error);
            }
        }
        private async Task HandleDiskSpaceAsync(FileEntry entry)
        {
            Log.Info("HandleDiskSpaceAsync: entry={Name} path={Path} isPortal={IsPortal} isRootContainer={IsRootContainer} root={Root}",
                entry?.Name ?? "<null>", entry?.FullPath ?? "<null>", entry?.IsPortal ?? false,
                entry?.IsRootContainer ?? false, entry?.PortalKnownFolder ?? "<none>");

            var volumes = entry != null && entry.Name == ".."
                ? ResolveCurrentFolderVolumes()
                : ResolveDiskSpaceVolumes(entry);
            Log.Info("HandleDiskSpaceAsync: {Volumes}",
                volumes.Count == 0 ? "<none>" : string.Join(", ", volumes.Select(v => v.Label == null ? v.Root : $"{v.Root} ({v.Label})")));

            if (volumes.Count == 0)
                Log.Warn("HandleDiskSpaceAsync: no volumes resolved — modal will not open");

            UpdateFooterALabel("Close");
            DiskUsageDialogControl.Show(volumes.ToArray());
            #if BATCH_DEBUG
            Log.Verb("HandleDiskSpaceAsync: modal Show called");
            #endif
            await Task.Delay(1);
        }

        private List<DiskVolumeInfo> ResolveCurrentFolderVolumes()
        {
            var volumes = new List<DiskVolumeInfo>();
            var current = _navigator.Current;
#if BATCH_DEBUG
            Log.Verb("ResolveCurrentFolderVolumes: current={Path} isPortal={IsPortal} knownFolder={Known}",
                current?.Path ?? "<null>", current?.IsPortal ?? false, current?.PortalKnownFolder ?? "<none>");
#endif
            if (current == null)
            {
                Log.Warn("ResolveCurrentFolderVolumes: no current column");
                return volumes;
            }

            if (current.IsPortal)
            {
                if (string.IsNullOrEmpty(current.PortalKnownFolder))
                {
                    volumes.Add(new DiskVolumeInfo(PortalCore.DestinationDriveRoot("LocalAppData"), "LocalAppData"));
                    volumes.Add(new DiskVolumeInfo(PortalCore.DestinationDriveRoot("DevelopmentFiles"), "DevelopmentFiles"));
                }
                else
                {
                    volumes.Add(new DiskVolumeInfo(PortalCore.DestinationDriveRoot(current.PortalKnownFolder), current.PortalKnownFolder));
                }
            }
            else if (!string.IsNullOrEmpty(current.Path))
            {
                volumes.Add(new DiskVolumeInfo(current.Path, null));
            }
#if BATCH_DEBUG
            Log.Verb("ResolveCurrentFolderVolumes: resolved {Count} volume(s) — {Volumes}",
                volumes.Count,
                volumes.Count == 0 ? "<none>" : string.Join(", ", volumes.Select(v => v.Label == null ? v.Root : $"{v.Root} ({v.Label})")));
#endif
            return volumes.Where(v => !string.IsNullOrEmpty(v.Root)).ToList();
        }

        private static List<DiskVolumeInfo> ResolveDiskSpaceVolumes(FileEntry entry)
        {
            var volumes = new List<DiskVolumeInfo>();
            if (entry == null)
            {
                Log.Warn("ResolveDiskSpaceVolumes: entry is null");
                return volumes;
            }
#if BATCH_DEBUG
            Log.Verb("ResolveDiskSpaceVolumes: name={Name} path={Path} isRootContainer={IsRoot} isDrive={IsDrive} isPortal={IsPortal}",
                entry.Name, entry.FullPath ?? "<null>", entry.IsRootContainer, entry.IsDrive, entry.IsPortal);
#endif

            if (entry.IsRootContainer)
            {
                if (entry.IsPortal)
                {
                    if (string.IsNullOrEmpty(entry.PortalKnownFolder))
                    {
                        // User Folders root — spans both portal volumes.
                        volumes.Add(new DiskVolumeInfo(PortalCore.DestinationDriveRoot("LocalAppData"), "LocalAppData"));
                        volumes.Add(new DiskVolumeInfo(PortalCore.DestinationDriveRoot("DevelopmentFiles"), "DevelopmentFiles"));
                    }
                    else
                    {
                        volumes.Add(new DiskVolumeInfo(PortalCore.DestinationDriveRoot(entry.PortalKnownFolder), entry.PortalKnownFolder));
                    }
                }
                else if (entry.IsDrive)
                {
                    volumes.Add(new DiskVolumeInfo(entry.FullPath, null));
                }
                else if (!string.IsNullOrEmpty(entry.FullPath))
                {
                    // AppData shortcut.
                    volumes.Add(new DiskVolumeInfo(entry.FullPath, null));
                }
            }
            else if (!string.IsNullOrEmpty(entry.FullPath))
            {
                volumes.Add(new DiskVolumeInfo(entry.FullPath, null));
            }
#if BATCH_DEBUG
            Log.Verb("ResolveDiskSpaceVolumes: resolved {Count} volume(s) — {Volumes}",
                volumes.Count,
                volumes.Count == 0 ? "<none>" : string.Join(", ", volumes.Select(v => v.Label == null ? v.Root : $"{v.Root} ({v.Label})")));
#endif
            return volumes.Where(v => !string.IsNullOrEmpty(v.Root)).ToList();
        }

        /// <summary>
        /// Download from URL into a user-chosen local folder. The destination is
        /// picked first (folder picker, always a real local path); B-cancel at the
        /// picker aborts without prompting for a URL. Direct links are streamed to
        /// disk; links that resolve to an HTML page fall through to the WebView
        /// overlay for a manual click-through download.
        /// </summary>
        private async Task HandleDownloadFromUrlAsync(FileEntry entry)
        {
            UpdateFooterALabel("OK");
            string url = await InputDialogControl.ShowAsync("Download from URL", "");
            UpdateFooterALabelFromSelection();
            if (string.IsNullOrWhiteSpace(url))
            {
                #if BATCH_DEBUG
                Log.Verb("HandleDownloadFromUrlAsync: URL prompt dismissed");
                #endif
                _ = AlertDialogControl.ShowAsync("URL cannot be empty.", AlertType.Warning);
                return;
            }

            url = url.Trim();
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                url = "https://" + url;
            }

            UpdateFooterALabel("Select");
            string destDir = await FolderBrowserDialogControl.ShowAsync(
                MoveDialogInitialPath(),
                PickerMode.Folder,
                null,
                "Download Here",
                "ms-appx:///Assets/Views/FileActionSheet/fileactionsheet-download-48.png");
            UpdateFooterALabelFromSelection();

            if (string.IsNullOrEmpty(destDir))
            {
                #if BATCH_DEBUG
                Log.Verb("HandleDownloadFromUrlAsync: destination cancelled");
                #endif
                return;
            }

            Log.Info("HandleDownloadFromUrlAsync: url={Url} dest={Dest}", url, destDir);

            string directUrl = await DownloadService.ResolveAsync(url, CancellationToken.None) ?? url;

            OpProgressDialog.Show("Downloading", url, destDir, 0, 1);
            var result = await DownloadService.TryDownloadAsync(
                directUrl,
                destDir,
                (copied, total) =>
                {
                    _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                    {
                        OpProgressDialog.UpdateProgress(new FileOperations.OperationProgress
                        {
                            FileName = "Downloading...",
                            BytesCopied = copied,
                            TotalBytes = total,
                            PercentComplete = total > 0 ? Math.Min(100, copied * 100.0 / total) : 0
                        });
                    });
                },
                OpProgressDialog.CancelToken);

            if (result.Outcome == DownloadService.DownloadOutcome.Downloaded)
            {
                OpProgressDialog.Complete();
                await Task.Delay(300);
                OpProgressDialog.Close();
                OnRefresh();
                _ = AlertDialogControl.ShowAsync($"Downloaded \"{Path.GetFileName(result.SavedPath)}\" to {destDir}.", AlertType.Success);
                return;
            }

            OpProgressDialog.Close();

            if (result.Outcome == DownloadService.DownloadOutcome.Canceled)
            {
                Log.Info("HandleDownloadFromUrlAsync: cancelled");
                return;
            }

            if (result.Outcome == DownloadService.DownloadOutcome.NeedsBrowser)
            {
                Log.Info("HandleDownloadFromUrlAsync: opening browser overlay for {Url}", url);
                var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                Action<string> onDownloaded = null;
                onDownloaded = path => tcs.TrySetResult(true);
                Action onClosed = null;
                onClosed = () => tcs.TrySetResult(false);

                UrlDownloadOverlayControl.DownloadCompleted += onDownloaded;
                UrlDownloadOverlayControl.OnClosed += onClosed;
                UrlDownloadOverlayControl.Show(url, destDir);
                bool downloaded = await tcs.Task;
                UrlDownloadOverlayControl.DownloadCompleted -= onDownloaded;
                UrlDownloadOverlayControl.OnClosed -= onClosed;

                if (downloaded)
                {
                    OnRefresh();
                    _ = AlertDialogControl.ShowAsync($"Downloaded to {destDir}.", AlertType.Success);
                }
                return;
            }

            Log.Warn("HandleDownloadFromUrlAsync: failed — {Error}", result.Error ?? "unknown");
            _ = AlertDialogControl.ShowAsync($"Download failed.\n\n{result.Error ?? "See the log for details."}", AlertType.Error);
        }
    }
}
