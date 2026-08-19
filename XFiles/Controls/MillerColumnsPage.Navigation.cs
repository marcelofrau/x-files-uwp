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
using XFiles.Network;
using XFiles.Services;
using XFiles.Visualizers;


namespace XFiles.Controls
{
    public sealed partial class MillerColumnsPage
    {
        private void RegisterInputHandlers()
        {
            _router = new InputRouter();

            _router.Add(new OverlayHandler(100,
                () => TextEditorOverlayControl.IsOpen,
                (k, r) =>
                {
                    if (k == VirtualKey.GamepadDPadUp) TextEditorOverlayControl.HandleDPadUp();
                    else if (k == VirtualKey.GamepadDPadDown) TextEditorOverlayControl.HandleDPadDown();
                    else if (k == VirtualKey.GamepadDPadLeft) TextEditorOverlayControl.HandleDPadLeft();
                    else if (k == VirtualKey.GamepadDPadRight) TextEditorOverlayControl.HandleDPadRight();
                    return true;
                },
                (k) => { TextEditorOverlayControl.HandleButton(k); return true; }));

            _router.Add(new OverlayHandler(95,
                () => VideoTrackMenuControl.IsOpen,
                (k, r) => { VideoTrackMenuControl.HandleButton(k); return true; },
                (k) => { VideoTrackMenuControl.HandleButton(k); return true; }));

            _router.Add(new OverlayHandler(90,
                () => FolderBrowserDialogControl.IsOpen,
                (k, r) => { FolderBrowserDialogControl.HandleDPad(k); return true; },
                (k) => { FolderBrowserDialogControl.HandleButton(k); return true; }));

            _router.Add(new OverlayHandler(85,
                () => LetterGridControl.IsOpen,
                (k, r) => { LetterGridControl.HandleDPad(k); return true; },
                (k) => { LetterGridControl.HandleDPad(k); return true; }));

            _router.Add(new OverlayHandler(80,
                () => InputDialogControl.Visibility == Visibility.Visible,
                (k, r) => true,
                (k) => { InputDialogControl.HandleButton(k); return true; }));

            _router.Add(new OverlayHandler(79,
                () => PortalCredentialsDialogControl.Visibility == Visibility.Visible,
                (k, r) => { PortalCredentialsDialogControl.HandleDPad(k); return true; },
                (k) => { PortalCredentialsDialogControl.HandleButton(k); return true; }));

            _router.Add(new OverlayHandler(77,
                () => NetworkLocationDialogControl.Visibility == Visibility.Visible,
                (k, r) => { NetworkLocationDialogControl.HandleDPad(k); return true; },
                (k) => { NetworkLocationDialogControl.HandleButton(k); return true; }));

            _router.Add(new OverlayHandler(82,
                () => HostKeyDialogControl.IsDialogVisible,
                (k, r) => true,
                (k) => { HostKeyDialogControl.HandleButton(k); return true; }));

            _router.Add(new OverlayHandler(78,
                () => PortalSetupDialogControl.IsVisible,
                (k, r) => { PortalSetupDialogControl.HandleDPad(k); return true; },
                (k) => { PortalSetupDialogControl.HandleButton(k); return true; }));

            _router.Add(new OverlayHandler(75,
                () => AlertDialogControl.Visibility == Visibility.Visible,
                (k, r) => true,
                (k) => { AlertDialogControl.HandleButton(k); return true; }));

            _router.Add(new OverlayHandler(72,
                () => OverwriteDialogControl.IsDialogVisible,
                (k, r) => true,
                (k) => { OverwriteDialogControl.HandleButton(k); return true; }));

            _router.Add(new OverlayHandler(71,
                () => FileConflictDialogControl.IsDialogVisible,
                (k, r) => true,
                (k) => { FileConflictDialogControl.HandleButton(k); return true; }));

            _router.Add(new OverlayHandler(70,
                () => FileOperationConfirmDialogControl.IsDialogVisible,
                (k, r) => true,
                (k) => { FileOperationConfirmDialogControl.HandleButton(k); return true; }));

            _router.Add(new OverlayHandler(68,
                () => OpProgressDialog.IsOpen,
                (k, r) => true,
                (k) => { if (k == VirtualKey.GamepadB) OpProgressDialog.Cancel(); return true; }));

            _router.Add(new OverlayHandler(67,
                () => DiskUsageDialogControl.IsOpen,
                (k, r) => true,
                (k) =>
                {
                    if (k == VirtualKey.GamepadB)
                    {
                        DiskUsageDialogControl.Close();
                        UpdateFooterALabelFromSelection();
                    }
                    return true;
                }));

            _router.Add(new OverlayHandler(65,
                () => SettingsPageControl.IsVisible,
                (k, r) =>
                {
                    VirtualKey mapped = k;
                    if (k == VirtualKey.GamepadDPadUp) mapped = VirtualKey.Up;
                    else if (k == VirtualKey.GamepadDPadDown) mapped = VirtualKey.Down;
                    SettingsPageControl.HandleDPad(mapped);
                    return true;
                },
                (k) => { SettingsPageControl.HandleDPad(k); return true; }));

            _router.Add(new OverlayHandler(63,
                () => ControlsGuideControl.IsVisible,
                (k, r) => true,
                (k) => { if (k == VirtualKey.GamepadB) ControlsGuideControl.Hide(); return true; }));

            _router.Add(new OverlayHandler(62,
                () => AboutOverlay.Visibility == Visibility.Visible,
                (k, r) => true,
                (k) =>
                {
                    if (k == VirtualKey.GamepadB) HideAbout();
                    else if (k == VirtualKey.GamepadY) ReRunPortalProbe();
                    return true;
                }));

            _router.Add(new OverlayHandler(60,
                () => StartMenuControl.IsOpen,
                (k, r) =>
                {
                    VirtualKey mapped = k;
                    if (k == VirtualKey.GamepadDPadUp) mapped = VirtualKey.Up;
                    else if (k == VirtualKey.GamepadDPadDown) mapped = VirtualKey.Down;
                    else if (k == VirtualKey.GamepadDPadLeft) mapped = VirtualKey.Left;
                    else if (k == VirtualKey.GamepadDPadRight) mapped = VirtualKey.Right;
                    StartMenuControl.ForwardDPad(mapped);
                    return true;
                },
                (k) => { StartMenuControl.ForwardDPad(k); return true; }));

            _router.Add(new OverlayHandler(55,
                () => FileActionSheetControl.IsOpen,
                (k, r) =>
                {
                    VirtualKey mapped = k;
                    if (k == VirtualKey.GamepadDPadUp) mapped = VirtualKey.Up;
                    else if (k == VirtualKey.GamepadDPadDown) mapped = VirtualKey.Down;
                    else if (k == VirtualKey.GamepadDPadLeft) mapped = VirtualKey.Left;
                    else if (k == VirtualKey.GamepadDPadRight) mapped = VirtualKey.Right;
                    FileActionSheetControl.ForwardDPad(mapped);
                    return true;
                },
                (k) => { FileActionSheetControl.ForwardDPad(k); return true; }));

            _router.Add(new OverlayHandler(57,
                () => UrlDownloadOverlayControl.IsOpen,
                (k, r) => { UrlDownloadOverlayControl.HandleDPad(k, r); return true; },
                (k) => { UrlDownloadOverlayControl.HandleButton(k); return true; }));

            _router.Add(new OverlayHandler(45,
                () => LogsPageControl.IsVisible,
                (k, r) => { LogsPageControl.HandleDPad(k); return true; },
                (k) => { LogsPageControl.HandleDPad(k); return true; }));

            _router.Add(new OverlayHandler(40,
                () => ShareDialogControl.IsVisible,
                (k, r) => { ShareDialogControl.HandleDPad(k); return true; },
                (k) => { ShareDialogControl.HandleDPad(k); return true; }));

            _router.Add(new OverlayHandler(36,
                () => PlaceholderOverlay.Visibility == Visibility.Visible,
                (k, r) => true,
                (k) => { if (k == VirtualKey.GamepadB) HidePlaceholder(); return true; }));

            _router.Add(new OverlayHandler(35,
                () => ImageFullScreen.IsOpen,
                (k, r) => true,
                (k) =>
                {
                    if (k == VirtualKey.GamepadB) { ImageFullScreen.HandleButton(k); UpdateFooterALabelFromSelection(); }
                    else if (k == VirtualKey.GamepadA) { ImageFullScreen.HandleButton(k); }
                    return true;
                }));

            _router.Add(new OverlayHandler(34,
                () => PdfFullScreen.IsOpen,
                (k, r) => true,
                (k) =>
                {
                    if (k == VirtualKey.GamepadB) { PdfFullScreen.HandleButton(k); UpdateFooterALabelFromSelection(); }
                    return true;
                }));

            _router.Add(new OverlayHandler(33,
                () => AudioFullScreenPanel.Visibility == Visibility.Visible,
                (k, r) => _fsPickerVisible ? false : true,
                (k) =>
                {
                    if (_fsPickerVisible) return false;
                    if (k == VirtualKey.GamepadB) { CloseAudioFullscreen(); UpdateMediaPlayerFocusUI(); }
                    else if (k == VirtualKey.GamepadA) { ToggleAudioFullscreenPlayPause(); }
                    return true;
                }));

            _router.Add(new OverlayHandler(32,
                () => VideoFullScreenPanel.Visibility == Visibility.Visible,
                (k, r) =>
                {
                    if (k == VirtualKey.GamepadDPadLeft) HandleContinuousSeek(-5);
                    else if (k == VirtualKey.GamepadDPadRight) HandleContinuousSeek(5);
                    return true;
                },
                (k) =>
                {
                    if (k == VirtualKey.GamepadA) OnFsVideoInput();
                    else if (k == VirtualKey.GamepadB)
                    {
                        if (FSControlsBar.Opacity > 0.0 || FSLegendText.Opacity > 0.0)
                        {
                            HideFsControls();
                            _fsHideTimer.Stop();
                        }
                        else
                        {
                            CloseVideoFullScreen();
                            UpdateFooterALabelFromSelection();
                        }
                    }
                    return true;
                }));
        }

        // --- INavigable ---

        private void SkipSeparatorRow(bool up, IReadOnlyList<FileEntry> entries, int count)
        {
            if (count < 2 || CurrentList.SelectedIndex < 0 || CurrentList.SelectedIndex >= count)
                return;
            while (entries != null && entries[CurrentList.SelectedIndex].IsSeparator)
            {
                CurrentList.SelectedIndex = up
                    ? (CurrentList.SelectedIndex == 0 ? count - 1 : CurrentList.SelectedIndex - 1)
                    : (CurrentList.SelectedIndex >= count - 1 ? 0 : CurrentList.SelectedIndex + 1);
            }
        }

        public void OnDPadUp(bool isRepeat = false)
        {
            if (_router.RouteDPad(VirtualKey.GamepadDPadUp, isRepeat)) return;

            if (_fsPickerVisible)
            {
                int pickerCount = FsVisualizerList.Items.Count;
                if (pickerCount > 0)
                {
                    FsVisualizerList.SelectedIndex = FsVisualizerList.SelectedIndex <= 0
                        ? pickerCount - 1
                        : FsVisualizerList.SelectedIndex - 1;
                    FsVisualizerList.ScrollIntoView(FsVisualizerList.SelectedItem);
                }
                return;
            }

            if (_isMediaPlayerActive) { MediaPreview.StopPlayer(); UpdateMediaPlayerFocusUI(); }

            var before = CurrentList.SelectedIndex;
            var entries = _navigator.Current?.Entries;
            int count = entries?.Count ?? 0;
            string beforeName = (entries != null && before >= 0 && before < count) ? entries[before].Name : "(none)";

            if (count > 0 && CurrentList.SelectedIndex <= 0)
                CurrentList.SelectedIndex = count - 1;
            else if (count > 0)
                CurrentList.SelectedIndex--;

            SkipSeparatorRow(true, entries, count);

            CurrentList.ScrollIntoView(CurrentList.SelectedItem);
            string afterName = (entries != null && CurrentList.SelectedIndex >= 0 && CurrentList.SelectedIndex < count) ? entries[CurrentList.SelectedIndex].Name : "(none)";
#if GAMEPAD_INPUT_DEBUG
            Log.Verb("OnDPadUp: {Before}→{After} \"{BeforeName}\"→\"{AfterName}\" repeat={R}", before, CurrentList.SelectedIndex, beforeName, afterName, isRepeat);
#endif
        }

        public void OnDPadDown(bool isRepeat = false)
        {
            if (_router.RouteDPad(VirtualKey.GamepadDPadDown, isRepeat)) return;

            if (_fsPickerVisible)
            {
                int pickerCount = FsVisualizerList.Items.Count;
                if (pickerCount > 0)
                {
                    FsVisualizerList.SelectedIndex = FsVisualizerList.SelectedIndex >= pickerCount - 1
                        ? 0
                        : FsVisualizerList.SelectedIndex + 1;
                    FsVisualizerList.ScrollIntoView(FsVisualizerList.SelectedItem);
                }
                return;
            }

            if (_isMediaPlayerActive) { MediaPreview.StopPlayer(); UpdateMediaPlayerFocusUI(); }

            var before = CurrentList.SelectedIndex;
            var entries = _navigator.Current?.Entries;
            int count = entries?.Count ?? 0;
            string beforeName = (entries != null && before >= 0 && before < count) ? entries[before].Name : "(none)";

            if (count > 0 && CurrentList.SelectedIndex >= count - 1)
                CurrentList.SelectedIndex = 0;
            else if (count > 0)
                CurrentList.SelectedIndex++;

            SkipSeparatorRow(false, entries, count);

            CurrentList.ScrollIntoView(CurrentList.SelectedItem);
            string afterName = (entries != null && CurrentList.SelectedIndex >= 0 && CurrentList.SelectedIndex < count) ? entries[CurrentList.SelectedIndex].Name : "(none)";
#if GAMEPAD_INPUT_DEBUG
            Log.Verb("OnDPadDown: {Before}→{After} \"{BeforeName}\"→\"{AfterName}\" repeat={R}", before, CurrentList.SelectedIndex, beforeName, afterName, isRepeat);
#endif
        }

        public void OnDPadLeft()
        {
            if (_router.RouteDPad(VirtualKey.GamepadDPadLeft, false)) return;

            if (_isMediaPlayerActive) return;
            if (_isBatchMode) return;
            _slideFromRight = false;
            _ = _navigator.DrillOutAsync();
        }

        public void OnDPadRight()
        {
            if (_router.RouteDPad(VirtualKey.GamepadDPadRight, false)) return;

            if (_isMediaPlayerActive) return;
            if (_isBatchMode) return;
            // ".." always means drill-out (go up)
            var sel = CurrentList.SelectedItem as EntryViewModel;
            if (sel != null && sel.Name == "..")
            {
                Log.Info("OnDPadRight: '..' selected → DrillOutAsync");
                _ = _navigator.DrillOutAsync();
                return;
            }
            _slideFromRight = true;
            _ = _navigator.DrillInAsync();
        }

        public void OnConfirm()
        {
            if (_router.RouteButton(VirtualKey.GamepadA)) return;

            if (_fsPickerVisible) { ApplyFsPickerSelection(); return; }

            if (ErrorOverlay.Visibility == Visibility.Visible) { Log.Dbg("OnConfirm: → HideError"); HideError(); return; }
            if (_isMediaPlayerActive)
            {
                Log.Info("OnConfirm: → media player button");
                MediaPreview.HandleButton(Windows.System.VirtualKey.GamepadA);
                UpdateMediaPlayerFocusUI();
                return;
            }

            // Batch mode: A toggles item selection
            if (_isBatchMode)
            {
                ToggleBatchItem();
                return;
            }

            if (_navigator.Current == null) return;

            var selected = CurrentList.SelectedItem as EntryViewModel;
            if (selected == null || selected.IsSeparator)
            {
                _slideFromRight = true;
                _ = _navigator.DrillInAsync();
                return;
            }

            // ".." always means drill-out (go up)
            if (selected.Name == "..")
            {
                Log.Info("OnConfirm: '..' selected → DrillOutAsync");
                _ = _navigator.DrillOutAsync();
                return;
            }

            if (selected.IsDirectory || selected.IsArchive)
            {
                _slideFromRight = true;
                _ = _navigator.DrillInAsync();
            }
            else
            {
                string ext = System.IO.Path.GetExtension(selected.Name);
                if (selected.IsNetwork)
                {
                    if (selected.IsChiptune && selected.ChiptuneTrackIndex < 0)
                    {
                        Log.Verb("OnConfirm: network chiptune '{Name}' — probing track count", selected.Name);
                        _ = OnRemoteChiptuneConfirmAsync(selected);
                    }
                    else
                    {
                        Log.Verb("OnConfirm: network file '{Name}' — streaming open", selected.Name);
                        _ = OpenRemoteFileAsync(selected);
                    }
                }
                else if (FilePreviewService.IsImageFile(ext) && !FilePreviewService.IsSvgFile(ext))
                {
                    Log.Verb("OnConfirm: image selected — opening fullscreen");
                    ImageFullScreen.Show(_navigator.Preview?.PreviewImageSource);
                }
                else if (FilePreviewService.IsPdfFile(ext))
                {
                    Log.Verb("OnConfirm: PDF selected — opening fullscreen");
                    var preview = _navigator.Preview;
                    if (preview != null)
                    {
                        _ = OpenPdfFullscreenAsync(selected, preview.PreviewPdfPageCount);
                    }
                }
                else if (FilePreviewService.IsAudioFile(ext))
                {
                    _ = PlayAudioAsync(selected);
                }
                else if (selected.IsChiptune && selected.ChiptuneTrackIndex >= 0)
                {
                    // Chiptune subsong selected from a drilled-in track list.
                    PlayChiptuneTrack(selected);
                }
                else if (selected.IsChiptune)
                {
                    // Chiptune file: single-track plays directly, multi-track drills in.
                    _ = OnChiptuneFileConfirmAsync(selected);
                }
                else if (FilePreviewService.IsVideoFile(ext))
                {
                    _ = PlayVideoAsync(selected);
                }
                else
                {
                    Log.Verb("OnConfirm: file selected — showing FileActionSheet");
                    _ = ShowFileActionSheetAsync();
                }
            }
        }

        /// <summary>
        /// Resolves the local path to open for a selected entry. Portal entries have
        /// no local FullPath — use the already-cached preview copy, or cache on demand.
        /// </summary>
        private async Task<string> ResolveOpenPathAsync(EntryViewModel selected)
        {
            if (!selected.IsPortal)
                return selected.FullPath;

            var preview = _navigator.Preview;
            if (preview != null && !string.IsNullOrEmpty(preview.PreviewFilePath))
                return preview.PreviewFilePath;

            var entry = new FileEntry
            {
                Name = selected.Name,
                IsDirectory = selected.IsDirectory,
                SizeBytes = selected.SizeBytes,
                IsPortal = true,
                PortalKnownFolder = selected.PortalKnownFolder,
                PortalPackageFullName = selected.PortalPackageFullName,
                PortalPath = selected.PortalPath
            };
            return await PortalCache.EnsureAsync(PortalBrowser.ToPortalEntry(entry), null);
        }

        private async Task OpenPdfFullscreenAsync(EntryViewModel selected, int pageCount)
        {
            string path = await ResolveOpenPathAsync(selected);
            if (path == null)
            {
                Log.Warn("OnConfirm: no local path for PDF {Name}", selected.Name);
                return;
            }
            await PdfFullScreen.ShowAsync(path, pageCount, 0);
        }

        private async Task PlayAudioAsync(EntryViewModel selected)
        {
            if (selected.SizeBytes == 0)
            {
                Log.Warn("OnConfirm: empty audio file, blocking play");
                _ = AlertDialogControl.ShowAsync($"\"{selected.Name}\" is empty (0 bytes).", AlertType.Error);
                return;
            }
            string path = await ResolveOpenPathAsync(selected);
            if (path == null)
            {
                Log.Warn("OnConfirm: no local path for audio {Name}", selected.Name);
                return;
            }
            Log.Verb("OnConfirm: audio file — toggling play/pause");
            _mediaLoadTimer.Stop();
            _pendingMediaPath = null;
            if (_isMediaPlayerActive)
            {
                MediaPreview.TogglePlayPause();
            }
            else if (MediaPreview.IsFileLoaded(path))
            {
                MediaPreview.TogglePlayPause();
                UpdateMediaPlayerFocusUI();
            }
            else
            {
                Log.Info("OnConfirm: loading+playing audio {Path}", path);
                MediaPreview.Stop();
                MediaPreview.LoadFile(path);
                MediaPreview.TogglePlayPause();
                UpdateMediaPlayerFocusUI();
            }
        }

        /// <summary>
        /// Play a specific chiptune subsong (selected from a drilled-in track list).
        /// </summary>
        private void PlayChiptuneTrack(EntryViewModel selected)
        {
            string source = selected.ChiptuneSourcePath ?? selected.FullPath;
            int track = selected.ChiptuneTrackIndex;
            Log.Info("OnConfirm: playing chiptune track {Track} of {Path}", track + 1, source);
            _mediaLoadTimer.Stop();
            _pendingMediaPath = null;
            MediaPreview.LoadChiptuneTrack(source, track);
            MediaPreview.TogglePlayPause();
            UpdateMediaPlayerFocusUI();
        }

        /// <summary>
        /// Chiptune file confirm: probe track count — single-track plays directly,
        /// multi-track drills into the track list.
        /// </summary>
        private async Task OnChiptuneFileConfirmAsync(EntryViewModel selected)
        {
            int trackCount = await MediaPreview.GetChiptuneTrackCountAsync(selected.FullPath);
            if (trackCount <= 1)
            {
                Log.Info("OnConfirm: single-track chiptune — playing {Path}", selected.FullPath);
                await PlayAudioAsync(selected);
            }
            else
            {
                Log.Info("OnConfirm: multi-track chiptune ({Count} tracks) — drilling into {Path}", trackCount, selected.FullPath);
                _slideFromRight = true;
                await _navigator.DrillInAsync();
            }
        }

        /// <summary>
        /// Remote (network) chiptune confirm — mirrors OnChiptuneFileConfirmAsync.
        /// Multi-track (GBS/NSFE/RSN...) drills into the track list; single-track plays.
        /// The remote file must be cached locally before probing the track count.
        /// </summary>
        private async Task OnRemoteChiptuneConfirmAsync(EntryViewModel selected)
        {
            var current = _navigator.Current;
            if (current == null) return;

            string share = current.NetworkShareName;
            string path = selected.NetworkPath;
            string name = selected.Name;
            if (string.IsNullOrEmpty(path))
            {
                Log.Warn("OnRemoteChiptuneConfirm: no path context for {Name}", name);
                await OpenRemoteFileAsync(selected);
                return;
            }

            string tempPath = await CacheRemoteFileAsync(current.NetworkLocationId, share, path, name);
            if (tempPath == null)
            {
                _ = AlertDialogControl.ShowAsync("Failed to download the chiptune file.", AlertType.Error);
                return;
            }

            int trackCount = await MediaPreview.GetChiptuneTrackCountAsync(tempPath);
            if (trackCount <= 1)
            {
                Log.Info("OnConfirm: single-track remote chiptune — playing {Name}", name);
                _mediaLoadTimer.Stop();
                _pendingMediaPath = null;
                MediaPreview.Stop();
                MediaPreview.LoadChiptuneTrack(tempPath, 0);
                MediaPreview.SetNetworkContext(share, path);
                MediaPreview.TogglePlayPause();
                UpdateMediaPlayerFocusUI();
            }
            else
            {
                Log.Info("OnConfirm: multi-track remote chiptune ({Count} tracks) — drilling into {Name}", trackCount, name);
                _slideFromRight = true;
                await _navigator.DrillInAsync();
            }
        }

        private async Task PlayVideoAsync(EntryViewModel selected)
        {
            if (selected.SizeBytes == 0)
            {
                Log.Warn("OnConfirm: empty video file, blocking play");
                _ = AlertDialogControl.ShowAsync($"\"{selected.Name}\" is empty (0 bytes).", AlertType.Error);
                return;
            }
            string path = await ResolveOpenPathAsync(selected);
            if (path == null)
            {
                Log.Warn("OnConfirm: no local path for video {Name}", selected.Name);
                return;
            }
            Log.Verb("OnConfirm: video file — toggling play/pause");
            _mediaLoadTimer.Stop();
            _pendingMediaPath = null;
            if (_isMediaPlayerActive)
            {
                MediaPreview.TogglePlayPause();
            }
            else if (MediaPreview.IsFileLoaded(path))
            {
                MediaPreview.TogglePlayPause();
                UpdateMediaPlayerFocusUI();
            }
            else
            {
                Log.Info("OnConfirm: loading+playing video {Path}", path);
                MediaPreview.Stop();
                MediaPreview.LoadFile(path);
                MediaPreview.TogglePlayPause();
                UpdateMediaPlayerFocusUI();
            }
        }

        /// <summary>
        /// Opens a remote (network) file: fullscreen image/PDF from the cached copy,
        /// chiptune from a full small download, audio/video as a true stream.
        /// </summary>
        private async Task OpenRemoteFileAsync(EntryViewModel selected)
        {
            var current = _navigator.Current;
            if (current == null) return;

            string share = current.NetworkShareName;
            string path = selected.NetworkPath;
            string name = selected.Name;
            if (string.IsNullOrEmpty(path))
            {
                Log.Warn("OpenRemoteFile: no path context for {Name}", name);
                return;
            }

            string ext = Path.GetExtension(name);

            // Image: the preview pane already decoded it — show fullscreen.
            if (FilePreviewService.IsImageFile(ext) && !FilePreviewService.IsSvgFile(ext))
            {
                var preview = _navigator.Preview;
                if (preview?.PreviewImageSource != null)
                {
                    ImageFullScreen.Show(preview.PreviewImageSource);
                }
                else
                {
                    Log.Warn("OpenRemoteFile: no decoded image for {Name}", name);
                    _ = AlertDialogControl.ShowAsync("The image preview could not be decoded.", AlertType.Error);
                }
                return;
            }

            if (FilePreviewService.IsSvgFile(ext))
            {
                _ = AlertDialogControl.ShowAsync("SVG preview is shown in the preview pane.", AlertType.Info);
                return;
            }

            // PDF: cache locally first — the fullscreen viewer is path-based.
            if (FilePreviewService.IsPdfFile(ext))
            {
                string tempPath = await CacheRemoteFileAsync(current.NetworkLocationId, share, path, name);
                if (tempPath == null)
                {
                    _ = AlertDialogControl.ShowAsync("Failed to download the PDF.", AlertType.Error);
                    return;
                }
                var preview = _navigator.Preview;
                int pageCount = preview?.PreviewPdfPageCount ?? 0;
                await PdfFullScreen.ShowAsync(tempPath, pageCount, 0);
                return;
            }

            // Chiptune: files are small — cache fully, then the local chiptune path.
            // A plays inline in the preview pane (same as local), not fullscreen.
            if (RetroAudioPlayer.IsChiptuneFile(ext))
            {
                if (_isMediaPlayerActive || MediaPreview.IsNetworkFileLoaded(share, path))
                {
                    Log.Info("OpenRemoteFile: toggling play/pause on loaded remote chiptune '{Name}'", name);
                    MediaPreview.TogglePlayPause();
                    UpdateMediaPlayerFocusUI();
                    return;
                }
                string tempPath = await CacheRemoteFileAsync(current.NetworkLocationId, share, path, name);
                if (tempPath == null)
                {
                    _ = AlertDialogControl.ShowAsync("Failed to download the chiptune file.", AlertType.Error);
                    return;
                }
                Log.Info("OpenRemoteFile: playing remote chiptune inline from cache {Temp}", tempPath);
                _mediaLoadTimer.Stop();
                _pendingMediaPath = null;
                MediaPreview.Stop();
                MediaPreview.LoadChiptuneTrack(tempPath, 0);
                MediaPreview.SetNetworkContext(share, path);
                MediaPreview.TogglePlayPause();
                UpdateMediaPlayerFocusUI();
                return;
            }

            // Unknown format: A opens the same action sheet as the Y menu
            // (mirrors the local else-branch in OnConfirm).
            if (!FilePreviewService.IsAudioFile(ext) && !FilePreviewService.IsVideoFile(ext))
            {
                Log.Verb("OpenRemoteFile: unknown format '{Name}' — showing FileActionSheet", name);
                await ShowFileActionSheetAsync();
                return;
            }

            // Mirror the local behavior: if this file is already loaded in the
            // inline player, A just toggles play/pause — never re-opens the stream.
            bool alreadyLoaded = MediaPreview.IsNetworkFileLoaded(share, path);
            if (_isMediaPlayerActive || alreadyLoaded)
            {
                Log.Info("OpenRemoteFile: toggling play/pause on loaded remote media '{Name}'", name);
                MediaPreview.TogglePlayPause();
                UpdateMediaPlayerFocusUI();
                return;
            }

            System.IO.Stream remoteStream = await _navigator.OpenNetworkStreamAsync(
                current.NetworkLocationId, share, path);
            if (remoteStream == null)
            {
                _ = AlertDialogControl.ShowAsync("Could not open the remote file.", AlertType.Error);
                return;
            }

            Func<System.IO.Stream> reopen = () =>
                Task.Run(() => _navigator.OpenNetworkStreamAsync(current.NetworkLocationId, share, path))
                    .GetAwaiter().GetResult();

            if (FilePreviewService.IsAudioFile(ext))
            {
                // A on remote audio → inline preview player (same as local).
                Log.Info("OpenRemoteFile: loading remote audio inline '{Name}'", name);
                _previewNetworkLocationId = current.NetworkLocationId;
                _previewNetworkShare = share;
                _previewNetworkPath = path;
                await MediaPreview.LoadRemoteAudio(
                    new RemoteStream(remoteStream, reopen),
                    MimeForRemoteFile(ext),
                    Path.GetFileNameWithoutExtension(name),
                    id3StreamFactory: reopen);
                MediaPreview.SetNetworkContext(share, path);
                MediaPreview.TogglePlayPause();
                UpdateMediaPlayerFocusUI();
            }
            else
            {
                // A on remote video → inline preview player (same as local).
                if (IsFtpProtocol(selected.NetworkProtocol))
                {
                    Log.Info("OpenRemoteFile: FTP video not supported '{Name}' proto={Proto}", name, selected.NetworkProtocol);
                    _ = AlertDialogControl.ShowAsync(
                        "FTP video playback is not supported.\n\n" +
                        "X-Files can't stream video over FTP because the protocol requires " +
                        "re-opening a data connection for every seek, which makes playback impractical.\n\n" +
                        "To watch this video, copy it to a local folder first (Y → Copy, navigate to local folder, Y → Paste).",
                        AlertType.Info);
                    return;
                }
                Log.Info("OpenRemoteFile: loading remote video inline '{Name}'", name);
                _previewNetworkLocationId = current.NetworkLocationId;
                _previewNetworkShare = share;
                _previewNetworkPath = path;
                MediaPreview.LoadRemoteStream(
                    new RemoteStream(remoteStream, reopen), MimeForRemoteFile(ext));
                MediaPreview.SetNetworkContext(share, path);
                MediaPreview.TogglePlayPause();
                UpdateMediaPlayerFocusUI();
            }
        }

        /// <summary>Downloads a remote file in full to a temp cache path.</summary>
        private async Task<string> CacheRemoteFileAsync(long locationId, string share, string path, string name)
        {
            try
            {
                System.IO.Stream stream = await _navigator.OpenNetworkStreamAsync(locationId, share, path);
                if (stream == null) return null;
                string dir = Path.Combine(ApplicationData.Current.LocalCacheFolder.Path, "NetworkCache");
                Directory.CreateDirectory(dir);
                string tempPath = Path.Combine(dir, $"{Guid.NewGuid():N}_{name}");
                using (stream)
                using (var fs = File.Create(tempPath))
                {
                    long totalBytes = 0;
                    byte[] buffer = new byte[81920];
                    int read;
                    while ((read = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await fs.WriteAsync(buffer, 0, read);
                        totalBytes += read;
                    }
                }
                Log.Info("CacheRemoteFile: cached '{Name}' ({Bytes} bytes) → {Temp}", name, new System.IO.FileInfo(tempPath).Length, tempPath);
                return tempPath;
            }
            catch (Exception ex)
            {
                Log.Warn("CacheRemoteFile: failed for '{Name}'", name, ex);
                return null;
            }
        }

        /// <summary>X on a remote file → fullscreen player over the SMB stream.</summary>
        private async Task OpenRemoteFullscreenAsync(EntryViewModel selected)
        {
            try
            {
                var current = _navigator.Current;
                if (current == null) return;

                string share = current.NetworkShareName;
                string path = selected.NetworkPath;
                string name = selected.Name;
                string ext = System.IO.Path.GetExtension(name);
                if (string.IsNullOrEmpty(path))
                {
                    Log.Warn("OpenRemoteFullscreen: no path context for {Name}", name);
                    return;
                }

                if (FilePreviewService.IsVideoFile(ext) && IsFtpProtocol(selected.NetworkProtocol))
                {
                    Log.Info("OpenRemoteFullscreen: FTP video not supported '{Name}' proto={Proto}", name, selected.NetworkProtocol);
                    _ = AlertDialogControl.ShowAsync(
                        "FTP video playback is not supported.\n\n" +
                        "X-Files can't stream video over FTP because the protocol requires " +
                        "re-opening a data connection for every seek, which makes playback impractical.\n\n" +
                        "To watch this video, copy it to a local folder first (Y → Copy, navigate to local folder, Y → Paste).",
                        AlertType.Info);
                    return;
                }

                if (RetroAudioPlayer.IsChiptuneFile(ext))
                {
                    string tempPath = await CacheRemoteFileAsync(current.NetworkLocationId, share, path, name);
                    if (tempPath == null)
                    {
                        _ = AlertDialogControl.ShowAsync("Failed to download the chiptune file.", AlertType.Error);
                        return;
                    }
                    var chipPos = (_isMediaPlayerActive && MediaPreview.IsAudioMode)
                        ? MediaPreview.CurrentPosition
                        : TimeSpan.Zero;
                    OpenAudioFullscreen(tempPath, chipPos, Math.Max(0, selected.ChiptuneTrackIndex));
                    // OpenAudioFullscreen resets the network flag — restore it so
                    // LB/RB keeps navigating the network list for remote chiptunes.
                    _fsIsNetwork = true;
                    _fsNetworkPath = path;
                    return;
                }

                System.IO.Stream stream = await _navigator.OpenNetworkStreamAsync(
                    current.NetworkLocationId, share, path);
                if (stream == null)
                {
                    _ = AlertDialogControl.ShowAsync("Could not open the remote file.", AlertType.Error);
                    return;
                }

                Func<System.IO.Stream> reopen = () =>
                    Task.Run(() => _navigator.OpenNetworkStreamAsync(current.NetworkLocationId, share, path))
                        .GetAwaiter().GetResult();

                string title = System.IO.Path.GetFileNameWithoutExtension(name);
                if (FilePreviewService.IsVideoFile(ext))
                {
                    var pos = (_isMediaPlayerActive && !MediaPreview.IsAudioMode)
                        ? MediaPreview.CurrentPosition
                        : TimeSpan.Zero;
                    await ShowMediaFullscreenStreamAsync(
                        new RemoteStream(stream, reopen), MimeForRemoteFile(ext), title,
                        current.NetworkLocationId, share, path, position: pos);
                }
                else
                {
                    var pos = (_isMediaPlayerActive && MediaPreview.IsAudioMode)
                        ? MediaPreview.CurrentPosition
                        : TimeSpan.Zero;
                    await OpenRemoteAudioFullscreenAsync(
                        title, new RemoteStream(stream, reopen), MimeForRemoteFile(ext),
                        current.NetworkLocationId, share, path, position: pos);
                }
            }
            catch (Exception ex)
            {
                Log.Err("OpenRemoteFullscreen: {Ex}", ex);
            }
        }

        /// <summary>
        /// FTP/FTPS video playback is unsupported: the media engine needs random
        /// access (seeking) and FTP re-opens a data connection per seek, which is
        /// too slow. SMB/SFTP streams fine.
        /// </summary>
        private static bool IsFtpProtocol(NetworkProtocol protocol) =>
            protocol == NetworkProtocol.Ftp || protocol == NetworkProtocol.Ftps;

        private static string MimeForRemoteFile(string ext)
        {
            switch ((ext ?? "").ToLowerInvariant())
            {
                case ".mp3": return "audio/mpeg";
                case ".wav": return "audio/wav";
                case ".flac": return "audio/flac";
                case ".ogg": case ".oga": return "audio/ogg";
                case ".m4a": case ".aac": return "audio/mp4";
                case ".opus": return "audio/opus";
                case ".wma": return "audio/x-ms-wma";
                case ".mp4": case ".m4v": return "video/mp4";
                case ".mkv": return "video/x-matroska";
                case ".avi": return "video/x-msvideo";
                case ".mov": return "video/quicktime";
                case ".wmv": return "video/x-ms-wmv";
                case ".webm": return "video/webm";
                case ".m2ts": case ".ts": return "video/mp2t";
                default: return "application/octet-stream";
            }
        }

        private async Task OpenVideoFullscreenAsync(EntryViewModel selected)
        {
            string path = await ResolveOpenPathAsync(selected);
            if (path == null)
            {
                Log.Warn("OnRefresh: no local path for video {Name}", selected.Name);
                return;
            }
            var pos = (_isMediaPlayerActive && !MediaPreview.IsAudioMode)
                ? MediaPreview.CurrentPosition
                : TimeSpan.Zero;
            if (_isMediaPlayerActive) { MediaPreview.StopPlayer(); UpdateMediaPlayerFocusUI(); }
            await ShowMediaFullscreenAsync(new Uri(path), true, pos);
        }

        private async Task OpenAudioFullscreenAsync(EntryViewModel selected)
        {
            string path = await ResolveOpenPathAsync(selected);
            if (path == null)
            {
                Log.Warn("OnRefresh: no local path for audio {Name}", selected.Name);
                return;
            }
            var pos = (_isMediaPlayerActive && MediaPreview.IsAudioMode)
                ? MediaPreview.CurrentPosition
                : TimeSpan.Zero;
            if (_isMediaPlayerActive) { MediaPreview.StopPlayer(); UpdateMediaPlayerFocusUI(); }
            OpenAudioFullscreen(path, pos);
        }

        public void OnBack()
        {
            // Skip if an overlay just closed this tick (XAML Escape closed dialog, same B press arrives here)
            if (Environment.TickCount - _overlayClosedTick < 100)
            {
                Log.Info("OnBack: skipped — overlay just closed");
                return;
            }

            if (_router.RouteButton(VirtualKey.GamepadB)) return;

            if (_fsPickerVisible) { CloseFsPicker(); return; }

            if (ErrorOverlay.Visibility == Visibility.Visible) { Log.Dbg("OnBack: → HideError"); HideError(); return; }
            if (_isMediaPlayerActive)
            {
                Log.Info("OnBack: → StopPlayer");
                MediaPreview.StopPlayer();
                UpdateMediaPlayerFocusUI();
                return;
            }

            // Batch mode: B exits batch (after all overlay/dialog checks)
            if (_isBatchMode) { Log.Info("OnBack: → ExitBatchMode"); ExitBatchMode(); return; }

            // B button → go to parent directory
            Log.Info("OnBack: → DrillOutAsync");
            _slideFromRight = false;
            _ = _navigator.DrillOutAsync();
        }

        public void OnContextMenu()
        {
            if (_router.RouteButton(VirtualKey.GamepadY)) return;

            if (ErrorOverlay.Visibility == Visibility.Visible) { Log.Dbg("OnContextMenu: → ErrorShare"); OnErrorShareClick(null, null); return; }
            if (_isMediaPlayerActive) return;

            if (CurrentList.SelectedItem is EntryViewModel vm && vm.IsVirtual && !vm.IsPortal) return;

            Log.Verb("MillerColumnsPage.OnContextMenu — showing FileActionSheet");
            if (_isBatchMode)
                _ = ShowFileActionSheetBatchAsync();
            else
                _ = ShowFileActionSheetAsync();
        }

        public void OnContextMenuLongPress()
        {
            if (_router.RouteButton(VirtualKey.GamepadY)) return;
            if (IsAnyFullscreen) return;
            if (IsAnyOverlayVisible) return;
            if (StartMenuControl.IsOpen) return;
            if (FileActionSheetControl.IsOpen) return;
            if (_isMediaPlayerActive) return;

            bool inFavorites = _navigator.Current?.IsFavorite == true;
            var sel = CurrentList.SelectedItem as EntryViewModel;
            if (sel == null || sel.IsPortal) return;

            if (inFavorites)
            {
                Log.Info("OnContextMenuLongPress: in favorites column — removing favorite");
                _ = RemoveFavoriteAsync(sel.FullPath);
            }
            else if (sel.IsFavorite)
            {
                Log.Info("OnContextMenuLongPress: already favorited — removing favorite");
                _ = RemoveFavoriteAsync(sel.FullPath);
            }
            else
            {
                Log.Info("OnContextMenuLongPress: adding to favorites");
                _ = AddFavoriteAsync(sel.Name, sel.FullPath, sel.IsDirectory);
            }
        }

        private async Task AddFavoriteAsync(string name, string path, bool isDir)
        {
            try
            {
                await FileSystem.FavoritesManager.AddAsync(path, name, isDir);
                Log.Info("Added to favorites: {Path}", path);

                if (CurrentList?.ItemsSource is IList<EntryViewModel> items)
                {
                    var vm = items.FirstOrDefault(i => i.FullPath == path);
                    if (vm != null)
                        vm.IsFavorite = true;
                }
            }
            catch (Exception ex) { Log.Err("AddFavoriteAsync: {Ex}", ex); }
        }

        private async Task RemoveFavoriteAsync(string path)
        {
            try
            {
                await FileSystem.FavoritesManager.RemoveAsync(path);
                Log.Info("Removed from favorites: {Path}", path);

                if (_navigator.Current?.IsFavorite == true)
                {
                    var favs = await FileSystem.FavoritesManager.GetAllAsync();
                    _navigator.Current.Entries = favs.Select(f => new FileSystem.FileEntry
                    {
                        Name = f.Name,
                        FullPath = f.Path,
                        IsDirectory = f.IsDirectory
                    }).ToList();
                    BindCurrentList(_navigator.Current);
                    CurrentStatus.Text = $"{favs.Count} items";
                }
                else
                {
                    if (CurrentList?.ItemsSource is IList<EntryViewModel> items)
                    {
                        var vm = items.FirstOrDefault(i => i.FullPath == path);
                        if (vm != null)
                            vm.IsFavorite = false;
                    }
                }
            }
            catch (Exception ex) { Log.Err("RemoveFavoriteAsync: {Ex}", ex); }
        }

        public void OnRefresh()
        {
            if (_router.RouteButton(VirtualKey.GamepadX)) return;

            if (ErrorOverlay.Visibility == Visibility.Visible) { Log.Dbg("OnRefresh: → ErrorCopy"); OnErrorCopyClick(null, null); return; }

            // Batch mode: X deselects all
            if (_isBatchMode)
            {
                Log.Info("OnRefresh: batch mode → DeselectAll");
                _batchSelectedPaths.Clear();
                foreach (var item in CurrentList.Items)
                {
                    if (item is EntryViewModel vm) vm.IsSelected = false;
                }
                UpdateBatchCheckboxes();
                UpdateBatchFooter();
                return;
            }

            var selected = CurrentList.SelectedItem as EntryViewModel;
            if (selected != null)
            {
                string ext = System.IO.Path.GetExtension(selected.Name);

                // Remote (SMB): fullscreen over the network stream (mirror of local X).
                if (selected.IsNetwork &&
                    (FilePreviewService.IsVideoFile(ext) || FilePreviewService.IsAudioFile(ext) ||
                     RetroAudioPlayer.IsChiptuneFile(ext)))
                {
                    Log.Info("OnRefresh: remote file → fullscreen");
                    _ = OpenRemoteFullscreenAsync(selected);
                    return;
                }

                if (FilePreviewService.IsVideoFile(ext))
                {
                    Log.Info("OnRefresh: video file → fullscreen");
                    _ = OpenVideoFullscreenAsync(selected);
                    return;
                }

                if (FilePreviewService.IsAudioFile(ext) || FilePreviewService.IsChiptuneFile(ext))
                {
                    Log.Info("OnRefresh: audio file → fullscreen");
                    _ = OpenAudioFullscreenAsync(selected);
                    return;
                }

                // Chiptune subsong selected from a drilled-in track list: open the
                // fullscreen player on that specific track instead of refreshing.
                if (selected.IsChiptune && selected.ChiptuneTrackIndex >= 0)
                {
                    string source = selected.ChiptuneSourcePath ?? selected.FullPath;
                    var pos = (_isMediaPlayerActive && MediaPreview.IsAudioMode)
                        ? MediaPreview.CurrentPosition
                        : TimeSpan.Zero;
                    Log.Info("OnRefresh: chiptune track → fullscreen ({Track} of {Source})",
                        selected.ChiptuneTrackIndex + 1, source);
                    _ = OpenFullscreenForFile(source, pos, selected.ChiptuneTrackIndex);
                    return;
                }
            }

            Log.Info("OnRefresh: refreshing current directory");
            FooterSpinner.IsActive = true;
            _ = _navigator.RefreshCurrentAsync().ContinueWith(t =>
            {
                Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal,
                    () => FooterSpinner.IsActive = false);
            });
        }

        public void OnPaste()
        {
            if (TextEditorOverlayControl.IsOpen) { TextEditorOverlayControl.HandleButton(Windows.System.VirtualKey.GamepadX); return; }
            if (IsAnyFullscreen) return;
            if (FileActionSheetControl.IsOpen) return;
            if (StartMenuControl.IsOpen) return;
            if (IsAnyOverlayVisible) return;
            if (LogsPageControl.IsVisible) return;
            if (ShareDialogControl.IsVisible) return;
            if (!FileSystem.ClipboardState.HasItems) return;

            _ = HandlePasteAsync();
        }

        public void OnSettings()
        {
            if (TextEditorOverlayControl.IsOpen) return;
            if (ErrorOverlay.Visibility == Visibility.Visible) return;
            if (IsAnyOverlayVisible) return;
            if (StartMenuControl.IsOpen) { StartMenuControl.ForwardDPad(Windows.System.VirtualKey.GamepadA); return; }
            if (FileActionSheetControl.IsOpen) return;
            if (_router.RouteButton(VirtualKey.GamepadMenu)) return;
            if (IsAnyFullscreen) return;
            if (_isMediaPlayerActive) return;
            _ = ShowStartMenuAsync();
        }

        private async System.Threading.Tasks.Task ShowStartMenuAsync()
        {
            Log.Info("OnSettings — showing start menu");
            var result = await StartMenuControl.ShowAsync();
            if (result == null) return;

            switch (result.Value)
            {
                case StartMenuItem.Settings:
                    Log.Dbg("Start menu: Settings selected");
                    var cacheCleared = await SettingsPageControl.ShowAsync();
                    if (cacheCleared)
                    {
                        Log.Dbg("Settings: cache cleared, navigating to root");
                        await _navigator.LoadRootAsync();
                        CurrentList.SelectedIndex = 0;
                        CurrentList.Focus(Windows.UI.Xaml.FocusState.Programmatic);
                    }
                    break;
                case StartMenuItem.ControlsGuide:
                    Log.Dbg("Start menu: Controls Guide selected");
                    ControlsGuideControl.Show();
                    break;
                case StartMenuItem.About:
                    Log.Dbg("Start menu: About selected");
                    ShowAbout();
                    break;
                case StartMenuItem.ViewLogs:
                    Log.Dbg("Start menu: View Logs selected");
                    ShowLogs();
                    break;
                case StartMenuItem.CloseApplication:
                    Log.Info("Start menu: Close Application selected");
                    Windows.UI.Xaml.Application.Current.Exit();
                    break;

                case StartMenuItem.JumpToLetter:
                    Log.Dbg("Start menu: Jump to Letter selected");
                    char? letter = await LetterGridControl.ShowAsync();
                    if (letter.HasValue)
                    {
                        Log.Info("Jump to Letter: {Letter}", letter.Value);
                        _navigator.JumpToLetter(letter.Value);
                    }
                    break;

                case StartMenuItem.SearchFiles:
                    Log.Dbg("Start menu: Search Files selected");
                    string query = await InputDialogControl.ShowAsync("Search", "");
                    if (!string.IsNullOrEmpty(query))
                    {
                        Log.Info("Search: query=\"{Query}\"", query);
                        _navigator.Current?.ApplySearch(query);
                        CurrentHeader.Text = $"{_navigator.Current?.Label ?? ""} 🔍 \"{query}\"";
                        BindCurrentList(_navigator.Current);
                        CurrentStatus.Text = Formatting.FormatCount(_navigator.Current?.Entries);
                    }
                    break;
            }
        }

        private void ShowLogs()
        {
            Log.Info("ShowLogs: configuring callbacks and calling Show()");
            LogsPageControl.OnClosed = () =>
            {
                Log.Dbg("ShowLogs: LogsPage closed");
                _overlayClosedTick = Environment.TickCount;
            };
            LogsPageControl.OnShareRequested = (url) =>
            {
                Log.Dbg("ShowLogs: share requested, opening ShareDialog");
                ShareDialogControl.Show(url, "Log Shared");
            };
            LogsPageControl.Show();
            Log.Info("ShowLogs: Show() returned, IsVisible={V}", LogsPageControl.IsVisible);
        }

        private void ShowPlaceholder(string title, string subtitle, string body)
        {
            PlaceholderTitleText.Text = title;
            PlaceholderSubtitleText.Text = subtitle;
            PlaceholderBodyText.Text = body;
            PlaceholderOverlay.Visibility = Visibility.Visible;
        }

        private void HidePlaceholder()
        {
            PlaceholderOverlay.Visibility = Visibility.Collapsed;
        }

        private void ShowAbout()
        {
            Log.Dbg("Showing About overlay");
            AboutOverlay.Visibility = Visibility.Visible;
            AboutPortalStatusText.Text = DevicePortalService.ProbeStatus;
            DevicePortalService.ProbeAsync();
        }

        private void HideAbout()
        {
            AboutOverlay.Visibility = Visibility.Collapsed;
        }

        private void ReRunPortalProbe()
        {
            Log.Info("About: re-running Device Portal probe (Y)");
            AboutPortalStatusText.Text = "Probing portal… (full results in Log viewer)";
            DevicePortalService.ProbeCompleted += OnPortalProbeCompleted;
            DevicePortalService.ProbeAsync(force: true);
        }

        private void OnPortalProbeCompleted()
        {
            DevicePortalService.ProbeCompleted -= OnPortalProbeCompleted;
            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal,
                () => AboutPortalStatusText.Text = DevicePortalService.ProbeStatus);
        }

        private bool IsAnyOverlayVisible =>
            PlaceholderOverlay.Visibility == Visibility.Visible
            || AboutOverlay.Visibility == Visibility.Visible
            || InputDialogControl.Visibility == Visibility.Visible
            || PortalCredentialsDialogControl.Visibility == Visibility.Visible
            || PortalSetupDialogControl.IsVisible
            || AlertDialogControl.Visibility == Visibility.Visible
            || OverwriteDialogControl.IsDialogVisible
            || FileConflictDialogControl.IsDialogVisible
            || FileOperationConfirmDialogControl.IsDialogVisible
            || FolderBrowserDialogControl.IsOpen
            || OpProgressDialog.IsOpen
            || _fsPickerVisible;

        private bool IsAnyFullscreen =>
            ImageFullScreen.IsOpen || PdfFullScreen.IsOpen
            || VideoFullScreenPanel.Visibility == Visibility.Visible
            || AudioFullScreenPanel.Visibility == Visibility.Visible;

        public void OnPageUp()
        {
            if (ImageFullScreen.IsOpen) { ImageFullScreen.HandleButton((Windows.System.VirtualKey)VK_LT); return; }
            if (PdfFullScreen.IsOpen) return;
            if (VideoFullScreenPanel.Visibility == Visibility.Visible) { HandleContinuousSeek(-5); return; }
            if (_isMediaPlayerActive) { MediaPreview.Seek(TimeSpan.FromSeconds(-5)); return; }
            var before = CurrentList.SelectedIndex;
            if (CurrentList.SelectedIndex > 0)
                CurrentList.SelectedIndex = Math.Max(0, CurrentList.SelectedIndex - 8);
            CurrentList.ScrollIntoView(CurrentList.SelectedItem);
            Log.Dbg("OnPageUp: before={Before} after={After}", before, CurrentList.SelectedIndex);
        }

        public void OnPageDown()
        {
            if (ImageFullScreen.IsOpen) { ImageFullScreen.HandleButton((Windows.System.VirtualKey)VK_RT); return; }
            if (PdfFullScreen.IsOpen) return;
            if (VideoFullScreenPanel.Visibility == Visibility.Visible) { HandleContinuousSeek(5); return; }
            if (_isMediaPlayerActive) { MediaPreview.Seek(TimeSpan.FromSeconds(5)); return; }
            var before = CurrentList.SelectedIndex;
            if (_navigator.Current != null && CurrentList.Items.Count > 0)
                CurrentList.SelectedIndex = Math.Min(CurrentList.Items.Count - 1, CurrentList.SelectedIndex + 8);
            CurrentList.ScrollIntoView(CurrentList.SelectedItem);
            Log.Dbg("OnPageDown: before={Before} after={After}", before, CurrentList.SelectedIndex);
        }

        public void OnSeekBack()
        {
            if (ImageFullScreen.IsOpen) return;
            if (PdfFullScreen.IsOpen) { PdfFullScreen.HandleBumper(true); return; }
            if (AudioFullScreenPanel.Visibility == Visibility.Visible) { NavigateAudioTrack(-1); return; }
            if (VideoFullScreenPanel.Visibility == Visibility.Visible) { HandleContinuousSeek(-5); return; }
            if (_isMediaPlayerActive) { NavigatePreviewTrack(-1); return; }
            JumpByLetter(-1);
        }

        public void OnSeekForward()
        {
            if (ImageFullScreen.IsOpen) return;
            if (PdfFullScreen.IsOpen) { PdfFullScreen.HandleBumper(false); return; }
            if (AudioFullScreenPanel.Visibility == Visibility.Visible) { NavigateAudioTrack(1); return; }
            if (VideoFullScreenPanel.Visibility == Visibility.Visible) { HandleContinuousSeek(5); return; }
            if (_isMediaPlayerActive) { NavigatePreviewTrack(1); return; }
            JumpByLetter(1);
        }

        /// <summary>
        /// Jump to next (direction=1) or previous (direction=-1) entry
        /// whose first letter differs from the current selection.
        /// </summary>
        private void JumpByLetter(int direction)
        {
            if (_navigator.Current == null) return;
            var entries = _navigator.Current.Entries;
            int idx = CurrentList.SelectedIndex;
            if (idx < 0 || entries.Count == 0) return;

            char currentLetter = GetFirstLetter(entries[idx].Name);
            int i = idx;

            while (true)
            {
                i += direction;
                if (i < 0 || i >= entries.Count) break;
                char letter = GetFirstLetter(entries[i].Name);
                if (letter != currentLetter)
                {
                    CurrentList.SelectedIndex = i;
                    CurrentList.ScrollIntoView(CurrentList.SelectedItem);
                    return;
                }
            }

            // If no different letter found, clamp to boundary
            if (direction > 0 && idx < entries.Count - 1)
                CurrentList.SelectedIndex = entries.Count - 1;
            else if (direction < 0 && idx > 0)
                CurrentList.SelectedIndex = 0;
            CurrentList.ScrollIntoView(CurrentList.SelectedItem);
        }

        private static char GetFirstLetter(string name)
        {
            if (string.IsNullOrEmpty(name)) return '\0';
            char c = char.ToUpperInvariant(name[0]);
            // Skip ".." and non-alpha — treat directories/special as '\0'
            if (!char.IsLetterOrDigit(c)) return '\0';
            return c;
        }

        public void OnSeekRepeat(int seconds)
        {
            if (VideoFullScreenPanel.Visibility == Visibility.Visible) { HandleContinuousSeek(seconds); return; }
            // Audio (inline or fullscreen): LB/RB are track next/previous only —
            // no seek on hold. Seeking in the inline player is D-pad left/right.
        }

        public void OnTriggerHeld(float leftTrigger, float rightTrigger)
        {
            if (ImageFullScreen.IsOpen)
            {
                ImageFullScreen.HandleTriggers(leftTrigger, rightTrigger);
                _ltWasDown = false;
                _rtWasDown = false;
                return;
            }
            if (PdfFullScreen.IsOpen)
            {
                PdfFullScreen.HandleTriggers(leftTrigger, rightTrigger);
                _ltWasDown = false;
                _rtWasDown = false;
                return;
            }
            bool isMedia = VideoFullScreenPanel.Visibility == Visibility.Visible || _isMediaPlayerActive || AudioFullScreenPanel.Visibility == Visibility.Visible;
            if (!isMedia) { _ltWasDown = false; _rtWasDown = false; return; }

            const float Threshold = 0.3f;

            bool ltDown = leftTrigger > Threshold;
            bool rtDown = rightTrigger > Threshold;

            if (ltDown)
            {
                if (!_ltWasDown) _ltHoldMs = 0;
                _ltHoldMs += 16;
                if (_seekCooldown <= 0)
                {
                    int seek = ComputeAcceleratedSeek(_ltHoldMs);
                    HandleContinuousSeek(-seek);
                    _seekCooldown = 60;
                }
            }
            else
            {
                _ltWasDown = false;
                _ltHoldMs = 0;
            }

            if (rtDown)
            {
                if (!_rtWasDown) _rtHoldMs = 0;
                _rtHoldMs += 16;
                if (_seekCooldown <= 0)
                {
                    int seek = ComputeAcceleratedSeek(_rtHoldMs);
                    HandleContinuousSeek(seek);
                    _seekCooldown = 60;
                }
            }
            else
            {
                _rtWasDown = false;
                _rtHoldMs = 0;
            }

            _ltWasDown = ltDown;
            _rtWasDown = rtDown;
            if (_seekCooldown > 0) _seekCooldown -= 16;
        }

        private static int ComputeAcceleratedSeek(double holdMs)
        {
            double t = holdMs / 1000.0;
            return Math.Max(1, (int)(Math.Pow(t, 1.8) * 8));
        }

        private void HandleContinuousSeek(int seconds)
        {
            if (VideoFullScreenPanel.Visibility == Visibility.Visible)
            {
                ShowFsControls();

                var pos = FsVideoSession.Position + TimeSpan.FromSeconds(seconds);
                if (pos < TimeSpan.Zero) pos = TimeSpan.Zero;
                var total = FsVideoSession.NaturalDuration;
                if (total.TotalSeconds > 0 && pos > total) pos = total;

                FsVideoSession.Position = pos;

                if (total.TotalSeconds > 0)
                {
                    FSProgress.Value = (pos.TotalSeconds / total.TotalSeconds) * 100;
                    FSTimeText.Text = $"{Formatting.FormatFsTime(pos)} / {Formatting.FormatFsTime(total)}";
                }

                string dir = seconds > 0 ? "\u25B6\u25B6" : "\u25C0\u25C0";
                ShowFsOsd($"{dir}  {(seconds > 0 ? "+" : "")}{seconds}s", null, 800);
            }
            else if (AudioFullScreenPanel.Visibility == Visibility.Visible && AudioLevelService.Instance.IsFileLoaded)
            {
                var pos = AudioLevelService.Instance.Position + TimeSpan.FromSeconds(seconds);
                if (pos < TimeSpan.Zero) pos = TimeSpan.Zero;
                var total = AudioLevelService.Instance.Duration;
                if (total.TotalSeconds > 0 && pos > total) pos = total;

                AudioLevelService.Instance.Seek(pos);

                string dir = seconds > 0 ? "\u25B6\u25B6" : "\u25C0\u25C0";
                ShowFsOsd($"{dir}  {(seconds > 0 ? "+" : "")}{seconds}s", null, 800);
            }
            else if (_isMediaPlayerActive)
            {
                MediaPreview.Seek(TimeSpan.FromSeconds(seconds));
                ShowFsOsd($"{(seconds > 0 ? "+" : "")}{seconds}s", null, 800);
            }
        }

        public void OnLeftStickMove(float x, float y)
        {
            if (ControlsGuideControl.IsVisible) { ControlsGuideControl.HandleStick(x, y); return; }
            if (TextEditorOverlayControl.IsOpen) { TextEditorOverlayControl.HandleLeftStick(x, y); return; }
            if (UrlDownloadOverlayControl.IsOpen) { UrlDownloadOverlayControl.HandleLeftStick(x, y); return; }
            if (LogsPageControl.IsVisible) { LogsPageControl.HandleLeftStick(x, y); return; }
            if (ShareDialogControl.IsVisible) return;
            if (FolderBrowserDialogControl.IsOpen)
            {
                FolderBrowserDialogControl.HandleStick(y);
                return;
            }
            if (FileOperationConfirmDialogControl.IsDialogVisible)
            {
                FileOperationConfirmDialogControl.HandleStick(x, y);
                return;
            }
            if (OpProgressDialog.IsOpen)
            {
                return;
            }
            if (ImageFullScreen.IsOpen)
            {
                ImageFullScreen.HandleRightStick(x, y);
                return;
            }
            if (PdfFullScreen.IsOpen)
            {
                PdfFullScreen.HandleRightStick(x, y);
                return;
            }
            if (AudioFullScreenPanel.Visibility == Visibility.Visible)
            {
                UpdateFsVolume(y);
            }
            else if (VideoFullScreenPanel.Visibility == Visibility.Visible)
            {
                UpdateFsVolume(y);
            }
            else if (_isMediaPlayerActive)
            {
                UpdateFsVolume(y);
            }
        }

        public void OnRightStickMove(float x, float y)
        {
            if (ControlsGuideControl.IsVisible) { ControlsGuideControl.HandleStick(x, y); return; }
            if (TextEditorOverlayControl.IsOpen) { TextEditorOverlayControl.HandleStick(x, y); return; }
            if (UrlDownloadOverlayControl.IsOpen) { UrlDownloadOverlayControl.HandleRightStick(x, y); return; }
            if (LogsPageControl.IsVisible) { LogsPageControl.HandleRightStick(x, y); return; }
            if (ShareDialogControl.IsVisible) return;
            if (FolderBrowserDialogControl.IsOpen)
            {
                FolderBrowserDialogControl.HandleStick(y);
                return;
            }
            if (FileOperationConfirmDialogControl.IsDialogVisible)
            {
                FileOperationConfirmDialogControl.HandleStick(x, y);
                return;
            }
            if (OpProgressDialog.IsOpen)
            {
                return;
            }
            if (ImageFullScreen.IsOpen)
            {
                ImageFullScreen.HandleRightStick(x, y);
                return;
            }
            if (PdfFullScreen.IsOpen)
            {
                PdfFullScreen.HandleRightStick(x, y);
                return;
            }
            if (AudioFullScreenPanel.Visibility == Visibility.Visible)
            {
                UpdateFsVolume(y);
            }
            else if (VideoFullScreenPanel.Visibility == Visibility.Visible)
            {
                UpdateFsVolume(y);
            }
            else if (_isMediaPlayerActive)
            {
                UpdateFsVolume(y);
            }
        }

        public void OnHome()
        {
            if (IsAnyFullscreen) return;
            var before = CurrentList.SelectedIndex;
            if (CurrentList.Items.Count > 0)
                CurrentList.SelectedIndex = 0;
            CurrentList.ScrollIntoView(CurrentList.SelectedItem);
            Log.Dbg("OnHome: before={Before} after={After}", before, CurrentList.SelectedIndex);
        }

        public void OnEnd()
        {
            if (IsAnyFullscreen) return;
            var before = CurrentList.SelectedIndex;
            if (_navigator.Current != null && CurrentList.Items.Count > 0)
                CurrentList.SelectedIndex = CurrentList.Items.Count - 1;
            CurrentList.ScrollIntoView(CurrentList.SelectedItem);
            Log.Dbg("OnEnd: before={Before} after={After}", before, CurrentList.SelectedIndex);
        }

        public void OnScrollVertical(double delta)
        {
            if (ImageFullScreen.IsOpen) return;
            if (PdfFullScreen.IsOpen) return;
            try
            {
                if (PreviewTextScroll.Visibility == Visibility.Visible)
                {
                    double newOffset = PreviewTextScroll.VerticalOffset + delta;
                    PreviewTextScroll.ScrollToVerticalOffset(Math.Max(0, newOffset));
                }
                else if (PreviewCodeView.Visibility == Visibility.Visible)
                {
                    string js = $"window.scrollBy(0, {delta:F1});";
                    _ = PreviewCodeView.InvokeScriptAsync("eval", new[] { js });
                }
            }
            catch (Exception ex)
            {
                Log.Warn("OnScrollVertical failed", ex);
            }
        }

        public void OnScrollHorizontal(double delta)
        {
            if (ImageFullScreen.IsOpen) return;
            if (PdfFullScreen.IsOpen) return;
            try
            {
                if (PreviewTextScroll.Visibility == Visibility.Visible)
                {
                    double newOffset = PreviewTextScroll.HorizontalOffset + delta;
                    PreviewTextScroll.ScrollToHorizontalOffset(Math.Max(0, newOffset));
                }
                else if (PreviewCodeView.Visibility == Visibility.Visible)
                {
                    string js = $"window.scrollBy({delta:F1}, 0);";
                    _ = PreviewCodeView.InvokeScriptAsync("eval", new[] { js });
                }
            }
            catch (Exception ex)
            {
                Log.Warn("OnScrollHorizontal failed", ex);
            }
        }

        public void OnSelectVisualizer()
        {
            if (VideoFullScreenPanel.Visibility == Visibility.Visible)
            {
                _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    if (VideoTrackMenuControl.IsOpen)
                    {
                        VideoTrackMenuControl.HandleButton(Windows.System.VirtualKey.GamepadA);
                    }
                    else
                    {
                        OpenVideoTrackMenu();
                    }
                });
            }
            else if (_isMediaPlayerActive && !MediaPreview.IsAudioMode)
            {
                _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    if (VideoTrackMenuControl.IsOpen)
                    {
                        VideoTrackMenuControl.HandleButton(Windows.System.VirtualKey.GamepadA);
                    }
                    else if (MediaPreview.CurrentSubtitleTracks.Count > 0 || MediaPreview.CurrentAudioTracks.Count > 1)
                    {
                        VideoTrackMenuControl.Show(
                            MediaPreview.CurrentSubtitleTracks,
                            MediaPreview.CurrentAudioTracks,
                            MediaPreview.CurrentSubtitleTrackIndex,
                            MediaPreview.CurrentAudioTrackIndex);
                    }
                });
            }
            else if (_isAudioFullscreen)
            {
                _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    CycleAudioVisualizer();
                });
            }
        }
    }
}
