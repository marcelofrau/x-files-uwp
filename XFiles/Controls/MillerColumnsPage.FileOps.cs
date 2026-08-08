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
            Log.Verb("BatchMode: toggled {Name} (selected={Sel}, total={Total})",
                selected.Name, selected.IsSelected, _batchSelectedPaths.Count);

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
                    IsPortal = selected.IsPortal,
                    PortalKnownFolder = selected.PortalKnownFolder,
                    PortalPackageFullName = selected.PortalPackageFullName,
                    PortalPath = selected.PortalPath
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
            else
                action = await FileActionSheetControl.ShowAsync(entry);
            UpdateFooterALabelFromSelection();
            if (action == null)
            {
                Log.Verb("ShowFileActionSheetAsync: cancelled");
                return;
            }

            Log.Info("ShowFileActionSheetAsync: action={Action}", action);

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
            }
        }

        private async Task ShowFileActionSheetBatchAsync()
        {
            if (_batchSelectedPaths.Count == 0)
            {
                Log.Verb("ShowFileActionSheetBatchAsync: no items selected");
                return;
            }

            Log.Info("ShowFileActionSheetBatchAsync: {Count} items selected", _batchSelectedPaths.Count);

            UpdateFooterALabel("Select");
            var action = await FileActionSheetControl.ShowBatchAsync(_batchSelectedPaths.Count);
            UpdateFooterALabelFromSelection();
            if (action == null)
            {
                Log.Verb("ShowFileActionSheetBatchAsync: cancelled");
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
            var destDir = await FolderBrowserDialogControl.ShowAsync(_navigator.Current?.Path ?? null);
            UpdateFooterALabelFromSelection();

            if (string.IsNullOrEmpty(destDir))
            {
                Log.Verb("HandleBatchMoveAsync: cancelled at folder browser");
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
                Log.Verb("HandleBatchMoveAsync: confirmation cancelled");
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
                Log.Verb("HandleBatchMoveAsync: cancelled — insufficient free space");
                return;
            }

            OpProgressDialog.Show("Moving", $"{entries.Count} items", destDir,
                0, scan.FileCount);

            int completedFiles = 0;
            long completedBytes = 0;
            int success = 0, failed = 0;

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

                var result = await FileOperations.MoveAsync(entry.FullPath, destDir, progress, OpProgressDialog.CancelToken);

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
                Log.Verb("ExecuteBatchMovePortalAsync: cancelled — insufficient free space");
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

            // Build combined file list (portal entries have no local paths to enumerate)
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

            bool confirmed = await FileOperationConfirmDialogControl.ShowAsync(
                $"{entries.Count} items", true, allFiles, folderCount);
            if (!confirmed)
            {
                Log.Verb("HandleBatchDeleteAsync: confirmation cancelled");
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
                Log.Verb("HandleBatchCreateZipAsync: cancelled");
                return;
            }

            var currentPath = _navigator.Current?.Path;
            if (string.IsNullOrEmpty(currentPath)) return;
            var zipPath = System.IO.Path.Combine(currentPath, zipName);
            Log.Info("HandleBatchCreateZipAsync: zipPath={Zip}", zipPath);

            var scanZip = await FileOperations.ScanEntriesAsync(entries);
            if (!await EnsureDiskSpaceAsync(currentPath, scanZip.TotalBytes))
            {
                Log.Verb("HandleBatchCreateZipAsync: cancelled — insufficient free space");
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
            Log.Info("HandleCopyAsync: {File} → clipboard", entry.FullPath);
            ClipboardState.Copy(new[] { entry });
            UpdateClipboardIndicator();
            await Task.CompletedTask;
        }

        private async Task HandlePasteAsync()
        {
            try
            {
            if (!ClipboardState.HasItems) return;

            var current = _navigator.Current;

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

            var entries = ClipboardState.Entries;

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
                Log.Verb("HandlePasteAsync: cancelled — insufficient free space");
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
                    entry.FullPath, destDir, progress, sameDir, OpProgressDialog.CancelToken);

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
                    if (!entry.IsPortal && string.IsNullOrEmpty(entry.FullPath)) continue;

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
                Log.Verb("HandleCreatePortalZipAsync: cancelled");
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
                Log.Verb("HandleCreatePortalZipAsync: cancelled — insufficient temp space");
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
                        Log.Verb("HandleExtractPortalZipAsync: choice cancelled");
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
                Log.Verb("HandlePastePortalToLocalAsync: cancelled — insufficient free space");
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
            var destDir = await FolderBrowserDialogControl.ShowAsync(_navigator.Current?.Path ?? null);
            UpdateFooterALabelFromSelection();

            if (string.IsNullOrEmpty(destDir))
            {
                Log.Verb("HandleMoveAsync: cancelled at folder browser");
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
                Log.Verb("HandleMoveAsync: confirmation cancelled");
                return;
            }

            // 3. Execute move with progress
            var scan = await FileOperations.ScanEntriesAsync(new List<FileEntry> { entry });

            // Same-volume moves are renames — no free space consumed.
            if (!IsSameVolume(entry.FullPath, destDir) &&
                !await EnsureDiskSpaceAsync(destDir, scan.TotalBytes))
            {
                Log.Verb("HandleMoveAsync: cancelled — insufficient free space");
                return;
            }

            var progress = new Progress<FileOperations.OperationProgress>(p =>
            {
                OpProgressDialog.UpdateProgress(p);
            });

            OpProgressDialog.Show("Moving", entry.Name, destDir,
                0, scan.FileCount);
            var moveSw = Stopwatch.StartNew();
            var result = await FileOperations.MoveAsync(entry.FullPath, destDir, progress, OpProgressDialog.CancelToken);
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
            var destDir = await FolderBrowserDialogControl.ShowAsync(_navigator.Current?.Path ?? null);
            UpdateFooterALabelFromSelection();

            if (string.IsNullOrEmpty(destDir))
            {
                Log.Verb("HandlePortalMoveAsync: cancelled at folder browser");
                return;
            }

            // 2. Confirm
            UpdateFooterALabel("Confirm");
            bool confirmed = await FileOperationConfirmDialogControl.ShowMoveAsync(
                entry.Name, destDir, null, entry.IsDirectory ? 1 : 0);
            UpdateFooterALabelFromSelection();

            if (!confirmed)
            {
                Log.Verb("HandlePortalMoveAsync: confirmation cancelled");
                return;
            }

            // 2.5 Check free space for single portal files
            if (!entry.IsDirectory)
            {
                long required = Math.Max(0, entry.SizeBytes);
                if (required > 0 && !await EnsureDiskSpaceAsync(destDir, required))
                {
                    Log.Verb("HandlePortalMoveAsync: cancelled — insufficient free space");
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
                Log.Verb("HandleRenameAsync: cancelled or unchanged");
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

                var confirmed = await AlertDialogControl.ShowConfirmAsync($"Rename '{entry.Name}' to '{newName}'?");
            if (!confirmed)
            {
                Log.Verb("HandleRenameAsync: confirmation cancelled");
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

        private async Task HandlePortalRenameAsync(FileEntry entry, string newName)
        {
            Log.Info("HandlePortalRenameAsync: {Name} → {New}", entry.Name, newName);
            bool confirmed = await AlertDialogControl.ShowConfirmAsync($"Rename '{entry.Name}' to '{newName}'?");
            if (!confirmed)
            {
                Log.Verb("HandlePortalRenameAsync: confirmation cancelled");
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

            // Build file list for confirmation dialog
            var (files, folderCount) = await FileOperations.ListRecursiveAsync(entry.FullPath);
            bool confirmed = await FileOperationConfirmDialogControl.ShowAsync(
                entry.Name, entry.IsDirectory, files, folderCount);
            if (!confirmed)
            {
                Log.Verb("HandleDeleteAsync: confirmation cancelled");
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

        private async Task HandlePortalDeleteAsync(FileEntry entry)
        {
            Log.Info("HandlePortalDeleteAsync: {Name}", entry.Name);
            bool confirmed = await FileOperationConfirmDialogControl.ShowAsync(
                entry.Name, entry.IsDirectory, null, entry.IsDirectory ? 1 : 0);
            if (!confirmed)
            {
                Log.Verb("HandlePortalDeleteAsync: confirmation cancelled");
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
                    Log.Verb("HandleExtractAsync: choice cancelled");
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
                Log.Verb("HandleExtractAsync: cancelled — insufficient free space");
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
                Log.Verb("HandleExtractFileAsync: cancelled — insufficient free space");
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
                Log.Verb("HandleCreateFolderAsync: name cancelled");
                return;
            }

            if (_navigator.Current?.IsPortal == true)
            {
                await HandlePortalCreateFolderAsync(entry, folderName);
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

        private async Task HandleCreateZipAsync(FileEntry entry)
        {
            Log.Info("HandleCreateZipAsync: {File}", entry.FullPath);
            var zipName = await InputDialogControl.ShowAsync("Create ZIP", entry.Name + ".zip");
            if (string.IsNullOrEmpty(zipName))
            {
                Log.Verb("HandleCreateZipAsync: cancelled");
                return;
            }

            var currentPath = _navigator.Current?.Path;
            if (string.IsNullOrEmpty(currentPath)) return;
            var zipPath = System.IO.Path.Combine(currentPath, zipName);
            Log.Info("HandleCreateZipAsync: zipPath={Zip}", zipPath);

            var scanZip = await FileOperations.ScanEntriesAsync(new List<FileEntry> { entry });
            if (!await EnsureDiskSpaceAsync(currentPath, scanZip.TotalBytes))
            {
                Log.Verb("HandleCreateZipAsync: cancelled — insufficient free space");
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
            Log.Verb("HandleDiskSpaceAsync: modal Show called");
            await Task.Delay(1);
        }

        private List<DiskVolumeInfo> ResolveCurrentFolderVolumes()
        {
            var volumes = new List<DiskVolumeInfo>();
            var current = _navigator.Current;
            Log.Verb("ResolveCurrentFolderVolumes: current={Path} isPortal={IsPortal} knownFolder={Known}",
                current?.Path ?? "<null>", current?.IsPortal ?? false, current?.PortalKnownFolder ?? "<none>");
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

            Log.Verb("ResolveCurrentFolderVolumes: resolved {Count} volume(s) — {Volumes}",
                volumes.Count,
                volumes.Count == 0 ? "<none>" : string.Join(", ", volumes.Select(v => v.Label == null ? v.Root : $"{v.Root} ({v.Label})")));
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

            Log.Verb("ResolveDiskSpaceVolumes: name={Name} path={Path} isRootContainer={IsRoot} isDrive={IsDrive} isPortal={IsPortal}",
                entry.Name, entry.FullPath ?? "<null>", entry.IsRootContainer, entry.IsDrive, entry.IsPortal);

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

            Log.Verb("ResolveDiskSpaceVolumes: resolved {Count} volume(s) — {Volumes}",
                volumes.Count,
                volumes.Count == 0 ? "<none>" : string.Join(", ", volumes.Select(v => v.Label == null ? v.Root : $"{v.Root} ({v.Label})")));
            return volumes.Where(v => !string.IsNullOrEmpty(v.Root)).ToList();
        }
    }
}
