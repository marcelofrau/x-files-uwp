using System;
using System.Collections.Generic;
using System.ComponentModel;
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
            if (selected == null || selected.IsDotDot || selected.IsDrive) return;

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
                    await HandleExtractAsync(entry);
                    break;
                case FileAction.ExtractFile:
                    await HandleExtractFileAsync(entry);
                    break;
                case FileAction.CreateFolder:
                    await HandleCreateFolderAsync(entry);
                    break;
                case FileAction.CreateZip:
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

            if ((action == FileAction.CreateZip || action == FileAction.Share) &&
                entries.Any(e => e.IsPortal))
            {
                Log.Warn("ShowFileActionSheetBatchAsync: ZIP/Share unsupported for portal items");
                _ = AlertDialogControl.ShowAsync("ZIP/Share are not supported for portal items yet.", AlertType.Info);
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
            var scan = await FileOperations.ScanPathsAsync(sourcePaths);

            OpProgressDialog.Show("Moving", $"{entries.Count} items", destDir,
                0, scan.FileCount);

            int completedFiles = 0;
            long completedBytes = 0;
            int success = 0, failed = 0;

            foreach (var entry in entries)
            {
                var progress = new Progress<FileOperations.OperationProgress>(p =>
                {
                    Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                    {
                        OpProgressDialog.UpdateProgress(new FileOperations.OperationProgress
                        {
                            FileName = p.FileName,
                            PercentComplete = scan.FileCount > 0
                                ? (double)completedFiles / scan.FileCount * 100.0
                                : -1,
                            FileIndex = completedFiles,
                            FileTotal = scan.FileCount,
                            BytesCopied = completedBytes + p.BytesCopied,
                            TotalBytes = scan.TotalBytes
                        });
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

                var entryScan = await FileOperations.ScanPathsAsync(
                    new List<string> { entry.FullPath });
                completedFiles += entryScan.FileCount;
                completedBytes += entryScan.TotalBytes;

                if (result == FileOperations.OperationResult.Success)
                    success++;
                else
                    failed++;
            }

            OpProgressDialog.Complete();
            await Task.Delay(400);
            OpProgressDialog.Close();

            if (failed > 0)
                _ = AlertDialogControl.ShowAsync($"Moved {success} items, failed to move {failed}.", AlertType.Error);
            else
                Log.Info("HandleBatchMoveAsync: {Count} items moved", success);

            ExitBatchMode();
            await _navigator.RefreshCurrentAsync();
        }

        private async Task ExecuteBatchMovePortalAsync(List<FileEntry> entries, string destDir)
        {
            Log.Info("ExecuteBatchMovePortalAsync: {Count} items → {Dest}", entries.Count, destDir);

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
                        Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                            OpProgressDialog.UpdateProgress(p));
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
                _ = AlertDialogControl.ShowAsync($"Moved {entries.Count - failed} items, failed to move {failed}.", AlertType.Error);
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
                _ = AlertDialogControl.ShowAsync($"Deleted {success} items, failed to delete {failed}.", AlertType.Error);
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

            OpProgressDialog.Show("Creating ZIP", $"{entries.Count} items", zipPath);
            var result = await FileOperations.CreateZipAsync(entries.Select(e => e.FullPath).ToList(), zipPath, null, OpProgressDialog.CancelToken);

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
                _ = AlertDialogControl.ShowAsync($"Failed to create ZIP \"{zipName}\".", AlertType.Error);
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
                _ = AlertDialogControl.ShowAsync("Failed to create ZIP for sharing.", AlertType.Error);
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
            var scan = await FileOperations.ScanPathsAsync(sourcePaths);

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

            foreach (var entry in entries)
            {
                bool sameDir = string.Equals(
                    System.IO.Path.GetDirectoryName(entry.FullPath)?.TrimEnd('\\'),
                    destDir.TrimEnd('\\'),
                    StringComparison.OrdinalIgnoreCase);

                // Progress updates overall completed bytes across all entries
                var progress = new Progress<FileOperations.OperationProgress>(p =>
                {
                    Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                    {
                        // Overlay this entry's progress onto overall tracking
                        var overall = new FileOperations.OperationProgress
                        {
                            FileName = p.FileName,
                            PercentComplete = scan.FileCount > 0
                                ? (double)completedFiles / scan.FileCount * 100.0
                                : -1,
                            FileIndex = completedFiles,
                            FileTotal = scan.FileCount,
                            BytesCopied = completedBytes + p.BytesCopied,
                            TotalBytes = scan.TotalBytes
                        };
                        OpProgressDialog.UpdateProgress(overall);
                    });
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
                var entryScan = await FileOperations.ScanPathsAsync(
                    new List<string> { entry.FullPath });
                completedFiles += entryScan.FileCount;
                completedBytes += entryScan.TotalBytes;

                OpProgressDialog.TrackCompleted(entry.Name, entryScan.TotalBytes);

                if (result != FileOperations.OperationResult.Success)
                {
                    Log.Warn("HandlePasteAsync: {File} failed", entry.Name);
                    _ = AlertDialogControl.ShowAsync($"Copy failed: \"{entry.Name}\".", AlertType.Error);
                }
            }

            OpProgressDialog.Complete();
            await Task.Delay(400);
            OpProgressDialog.Close();

            UpdateClipboardIndicator();
            await _navigator.RefreshCurrentAsync();
        }
        catch (Exception ex) { Log.Err("HandlePasteAsync: {Ex}", ex); _ = AlertDialogControl.ShowAsync("Paste failed.", AlertType.Error); }
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
            if (entries.Any(e => e.IsPortal))
            {
                Log.Warn("HandlePasteToPortalAsync: portal-to-portal paste unsupported");
                _ = AlertDialogControl.ShowAsync("Portal-to-portal paste is not supported yet.", AlertType.Info);
                return;
            }

            Log.Info("HandlePasteToPortalAsync: {Count} local items → portal {Known}/{Pkg}{Path}",
                entries.Count, knownFolder, packageFullName, portalPath);

            OpProgressDialog.Show("Copying", $"{entries.Count} items", knownFolder + portalPath, 0, entries.Count);

            int failed = 0;
            foreach (var entry in entries)
            {
                if (string.IsNullOrEmpty(entry.FullPath)) continue;

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
                    Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                        OpProgressDialog.UpdateProgress(p));
                });

                try
                {
                    await XFiles.FileSystem.PortalBrowser.UploadLocalToPortalAsync(
                        entry.FullPath, knownFolder, packageFullName, portalPath,
                        progress, OpProgressDialog.CancelToken);
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
                _ = AlertDialogControl.ShowAsync($"Upload failed for {failed} item(s).", AlertType.Error);

            UpdateClipboardIndicator();
            await _navigator.RefreshCurrentAsync();
        }

        /// <summary>
        /// Pastes portal clipboard entries into a local directory via REST download.
        /// </summary>
        private async Task HandlePastePortalToLocalAsync(string destDir)
        {
            var entries = ClipboardState.Entries;
            Log.Info("HandlePastePortalToLocalAsync: {Count} portal items → {Dest}", entries.Count, destDir);

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
                    Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                        OpProgressDialog.UpdateProgress(p));
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
                _ = AlertDialogControl.ShowAsync($"Copy failed for {failed} item(s).", AlertType.Error);

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
            var scan = await FileOperations.ScanPathsAsync(
                new List<string> { entry.FullPath });

            var progress = new Progress<FileOperations.OperationProgress>(p =>
            {
                Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                    OpProgressDialog.UpdateProgress(p));
            });

            OpProgressDialog.Show("Moving", entry.Name, destDir,
                0, scan.FileCount);
            var result = await FileOperations.MoveAsync(entry.FullPath, destDir, progress, OpProgressDialog.CancelToken);

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

            if (result == FileOperations.OperationResult.Success)
            {
                Log.Info("HandleMoveAsync: success");
                await _navigator.RefreshCurrentAsync();
            }
            else
            {
                Log.Warn("HandleMoveAsync: failed");
                _ = AlertDialogControl.ShowAsync($"Failed to move \"{entry.Name}\".", AlertType.Error);
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

            // 3. Copy to local, then delete on the portal
            var portalEntry = XFiles.FileSystem.PortalBrowser.ToPortalEntry(entry);
            var progress = new Progress<FileOperations.OperationProgress>(p =>
            {
                Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                    OpProgressDialog.UpdateProgress(p));
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
                _ = AlertDialogControl.ShowAsync($"Failed to rename \"{entry.Name}\".", AlertType.Error);
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
                _ = AlertDialogControl.ShowAsync($"Failed to rename \"{entry.Name}\".", AlertType.Error);
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
                _ = AlertDialogControl.ShowAsync($"Failed to delete \"{entry.Name}\".", AlertType.Error);
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
                _ = AlertDialogControl.ShowAsync($"Failed to delete \"{entry.Name}\".", AlertType.Error);
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

            var progress = new Progress<FileOperations.OperationProgress>(p =>
            {
                Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                    OpProgressDialog.UpdateProgress(p));
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

            OpProgressDialog.Show("Extracting", entry.Name, destDir);
            var result = await FileOperations.ExtractAsync(entry.FullPath, destDir, progress, conflictCallback, OpProgressDialog.CancelToken);

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
                Log.Warn("HandleExtractAsync: failed");
                _ = AlertDialogControl.ShowAsync($"Failed to extract \"{entry.Name}\".", AlertType.Error);
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

            OpProgressDialog.Show("Extracting", fileName, destDir);
            var result = await FileOperations.ExtractFileAsync(
                entry.ArchiveRootPath, entry.ArchiveInternalPath, destDir, conflictCallback, OpProgressDialog.CancelToken);

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
                _ = AlertDialogControl.ShowAsync($"Failed to extract \"{fileName}\".", AlertType.Error);
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
                _ = AlertDialogControl.ShowAsync($"Failed to create folder \"{folderName}\".", AlertType.Error);
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
                _ = AlertDialogControl.ShowAsync($"Failed to create folder \"{folderName}\".", AlertType.Error);
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

            OpProgressDialog.Show("Creating ZIP", entry.Name, zipPath);
            var result = await FileOperations.CreateZipAsync(entry.FullPath, zipPath, null, OpProgressDialog.CancelToken);

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
                _ = AlertDialogControl.ShowAsync($"Failed to create ZIP \"{zipName}\".", AlertType.Error);
            }
        }
    }
}
