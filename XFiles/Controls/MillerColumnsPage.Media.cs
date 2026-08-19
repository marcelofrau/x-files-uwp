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
        // --- Fullscreen Video ---

        public async Task OpenFullscreenForFile(string filePath, TimeSpan position, int chiptuneTrack = -1)
        {
            OpenAudioFullscreen(filePath, position, chiptuneTrack);
            await System.Threading.Tasks.Task.CompletedTask;
        }

        public async Task ShowMediaFullscreenAsync(Uri source, bool isVideo, TimeSpan position)
        {
            if (!isVideo) return;

            // Always stop preview before fullscreen — idempotent, safe if already stopped
            if (_isMediaPlayerActive) { MediaPreview.StopPlayer(); UpdateMediaPlayerFocusUI(); }

            _fsVideoPath = source.LocalPath;
            _fsIsNetwork = false;
            _fsNetworkPath = null;

            // Detect external subtitles (VLC-style same-name matching)
            _fsSubtitles = SubtitleDetector.FindExternalSubtitles(_fsVideoPath);
            _fsSelectedSubtitleIndex = -1;
            _fsSelectedAudioIndex = 0;
            _fsAudioTracks = new List<AudioTrackInfo>();
            _fsSuppressTrackEvent = false;

            // Build MediaSource with external subtitle tracks
            var mediaSource = Windows.Media.Core.MediaSource.CreateFromUri(source);
            foreach (var sub in _fsSubtitles)
            {
                try
                {
                    var tts = TimedTextSource.CreateFromUri(new Uri(sub.FilePath));
                    tts.Resolved += OnExternalSubtitleResolved;
                    mediaSource.ExternalTimedTextSources.Add(tts);
                }
                catch (Exception ex)
                {
                    Log.Warn("ShowMediaFullscreenAsync: failed to add subtitle '{Path}'", sub.FilePath, ex);
                }
            }

            // Create MediaPlaybackItem so we can access TimedMetadataTracks and AudioTracks later
            _fsPlaybackItem = new Windows.Media.Playback.MediaPlaybackItem(mediaSource);
            FsVideoPlayer.Source = _fsPlaybackItem;

            // Subscribe to MediaOpened to enumerate tracks after source is ready
            FsVideoPlayer.MediaOpened += OnFsVideoMediaOpened;
            FsVideoPlayer.MediaEnded += OnFsVideoMediaEnded;

            FsVideoSession.Position = position;
            FsVideoPlayer.Volume = _fsVolume;
            FsVideoPlayer.Play();
            _fsVideoPlaying = true;
            FSPlayPauseIcon.Glyph = "\uE769";
            FSVolumeText.Text = $"Vol {(int)(_fsVolume * 100)}%";
            _fullscreenProgressTimer.Start();
            VideoFullScreenPanel.Visibility = Visibility.Visible;
            ShowFsControls();
            UpdateDisplayRequest();
            UpdateBgmDucking();
            ShowFsOsd("PLAY", "ms-appx:///Assets/Views/MillerColumnsPage/osd/osd-play-48.png");
            Log.Info("ShowMediaFullscreenAsync: started fullscreen video at {Position}, {SubCount} external subs", position, _fsSubtitles.Count);
            await System.Threading.Tasks.Task.CompletedTask;
        }

        /// <summary>
        /// Fullscreen video from a remote (network) stream. The stream reads on
        /// demand (RemoteStream over SMB), so playback starts without a local
        /// download. External subtitles are skipped (no local files to match).
        /// </summary>
        public async Task ShowMediaFullscreenStreamAsync(
            Windows.Storage.Streams.IRandomAccessStream stream, string mimeType, string title,
            long locationId = 0, string share = null, string path = null,
            TimeSpan position = default)
        {
            if (_isMediaPlayerActive) { MediaPreview.StopPlayer(); UpdateMediaPlayerFocusUI(); }

            _fsVideoPath = null;
            _fsIsNetwork = path != null;
            _fsNetworkLocationId = locationId;
            _fsNetworkShare = share;
            _fsNetworkPath = path;
            _fsSubtitles = new List<SubtitleTrack>();
            _fsSelectedSubtitleIndex = -1;
            _fsSelectedAudioIndex = 0;
            _fsAudioTracks = new List<AudioTrackInfo>();
            _fsSuppressTrackEvent = false;

            var mediaSource = Windows.Media.Core.MediaSource.CreateFromStream(stream, mimeType);

            _fsPlaybackItem = new Windows.Media.Playback.MediaPlaybackItem(mediaSource);
            FsVideoPlayer.Source = _fsPlaybackItem;

            FsVideoPlayer.MediaOpened += OnFsVideoMediaOpened;
            FsVideoPlayer.MediaEnded += OnFsVideoMediaEnded;

            _fsPendingPosition = position;
            FsVideoPlayer.Volume = _fsVolume;
            FsVideoPlayer.Play();
            _fsVideoPlaying = true;
            FSPlayPauseIcon.Glyph = "\uE769";
            FSVolumeText.Text = $"Vol {(int)(_fsVolume * 100)}%";
            _fullscreenProgressTimer.Start();
            VideoFullScreenPanel.Visibility = Visibility.Visible;
            ShowFsControls();
            UpdateDisplayRequest();
            UpdateBgmDucking();
            ShowFsOsd("PLAY", "ms-appx:///Assets/Views/MillerColumnsPage/osd/osd-play-48.png");
            Log.Info("ShowMediaFullscreenStreamAsync: started fullscreen video stream '{Title}' mime={Mime}", title, mimeType);
            await System.Threading.Tasks.Task.CompletedTask;
        }

        private void OnEmbeddedSubtitleCueEntered(TimedMetadataTrack sender, MediaCueEventArgs args)
        {
            if (args.Cue is TimedTextCue ttCue && ttCue.CueStyle == null)
            {
                ttCue.CueStyle = new TimedTextStyle
                {
                    FontFamily = "Arial",
                    FontSize = new TimedTextDouble { Unit = TimedTextUnit.Percentage, Value = 100 },
                    Foreground = Windows.UI.Colors.White,
                    OutlineColor = Windows.UI.Colors.Black,
                    OutlineThickness = new TimedTextDouble { Unit = TimedTextUnit.Percentage, Value = 4 },
                    OutlineRadius = new TimedTextDouble { Unit = TimedTextUnit.Percentage, Value = 2 }
                };
            }
        }

        private void OnExternalSubtitleResolved(TimedTextSource sender, TimedTextSourceResolveResultEventArgs args)
        {
            Log.Verb("OnExternalSubtitleResolved: external subtitle track resolved");
            if (args.Tracks == null) return;
            foreach (var track in args.Tracks)
            {
                if (track.TimedMetadataKind != TimedMetadataKind.Subtitle) continue;
                foreach (var cue in track.Cues)
                {
                    if (cue is TimedTextCue ttCue)
                    {
                        ttCue.CueStyle = new TimedTextStyle
                        {
                            FontFamily = "Arial",
                            FontSize = new TimedTextDouble { Unit = TimedTextUnit.Percentage, Value = 100 },
                            Foreground = Windows.UI.Colors.White,
                            OutlineColor = Windows.UI.Colors.Black,
                            OutlineThickness = new TimedTextDouble { Unit = TimedTextUnit.Percentage, Value = 4 },
                            OutlineRadius = new TimedTextDouble { Unit = TimedTextUnit.Percentage, Value = 2 }
                        };
                    }
                }
            }
        }

        private void OnFsVideoMediaOpened(MediaPlayer sender, object args)
        {
            _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                if (_fsPendingPosition > TimeSpan.Zero)
                {
                    FsVideoSession.Position = _fsPendingPosition;
                    _fsPendingPosition = TimeSpan.Zero;
                    Log.Verb("OnFsVideoMediaOpened: restored position {Pos}", FsVideoSession.Position);
                }
                EnumerateAllTracks();
            });
        }

        private async void OnFsVideoMediaEnded(MediaPlayer sender, object args)
        {
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                Log.Info("FsVideo: media ended — auto-advancing");
                NavigateFullscreenVideo(1);
            });
        }

        private void NavigateFullscreenVideo(int direction)
        {
            if (string.IsNullOrEmpty(_fsVideoPath) || _navigator.Current == null) return;

            // Remote (SMB) fullscreen video: navigate the network list instead of local paths.
            if (_fsIsNetwork)
            {
                NavigateFullscreenVideoNetwork(direction);
                return;
            }

            var videoFiles = _navigator.Current.Entries
                .Where(e => !e.IsDirectory && FilePreviewService.IsVideoFile(System.IO.Path.GetExtension(e.Name)))
                .ToList();

            if (videoFiles.Count == 0) { CloseVideoFullScreen(); return; }

            int currentIdx = videoFiles.FindIndex(e =>
                string.Equals(e.FullPath, _fsVideoPath, StringComparison.OrdinalIgnoreCase));

            int nextIdx = currentIdx + direction;
            if (nextIdx < 0) nextIdx = videoFiles.Count - 1;
            if (nextIdx >= videoFiles.Count) nextIdx = 0;

            var nextFile = videoFiles[nextIdx];
            Log.Info("NavigateFullscreenVideo: {Direction} to {Path}", direction > 0 ? "next" : "prev", nextFile.FullPath);

            // Update selection in main list
            int mainIdx = _navigator.Current.Entries.IndexOf(nextFile);
            if (mainIdx >= 0)
            {
                _updating = true;
                CurrentList.SelectedIndex = mainIdx;
                _updating = false;
            }

            // Play next video in fullscreen
            _ = ShowMediaFullscreenAsync(new Uri(nextFile.FullPath), true, TimeSpan.Zero);
        }

        private void EnumerateAllTracks()
        {
            try
            {
                if (_fsPlaybackItem == null) return;

                _fsSuppressTrackEvent = true;

                // Enumerate embedded subtitle tracks
                int embeddedSubCount = 0;
                for (int i = 0; i < _fsPlaybackItem.TimedMetadataTracks.Count; i++)
                {
                    var track = _fsPlaybackItem.TimedMetadataTracks[i];
                    if (track.TimedMetadataKind == Windows.Media.Core.TimedMetadataKind.Subtitle)
                    {
                        string lang = track.Language ?? "Unknown";
                        string title = track.Label ?? lang;
                        _fsSubtitles.Add(new SubtitleTrack
                        {
                            Language = lang,
                            Title = title,
                            IsExternal = false,
                            EmbeddedIndex = embeddedSubCount,
                            FilePath = null
                        });
                        track.CueEntered += OnEmbeddedSubtitleCueEntered;
                        embeddedSubCount++;
                    }
                }

                Log.Info("EnumerateAllTracks: found {EmbeddedSubs} embedded subtitle tracks", embeddedSubCount);

                // Enumerate audio tracks
                _fsAudioTracks.Clear();
                for (int i = 0; i < _fsPlaybackItem.AudioTracks.Count; i++)
                {
                    var track = _fsPlaybackItem.AudioTracks[i];
                    _fsAudioTracks.Add(new AudioTrackInfo
                    {
                        Language = track.Language ?? "Unknown",
                        Title = track.Label ?? track.Language ?? "Unknown",
                        Index = i
                    });
                }
                _fsSelectedAudioIndex = (int)_fsPlaybackItem.AudioTracks.SelectedIndex;

                Log.Info("EnumerateAllTracks: found {AudioCount} audio tracks", _fsAudioTracks.Count);
            }
            catch (Exception ex)
            {
                Log.Warn("EnumerateAllTracks failed", ex);
            }
            finally
            {
                _fsSuppressTrackEvent = false;
            }
        }

        private void OpenVideoTrackMenu()
        {
            if (VideoFullScreenPanel.Visibility != Visibility.Visible) return;
            if (VideoTrackMenuControl.IsOpen) return;

            // If tracks haven't been enumerated yet (source still opening), try now
            if (_fsAudioTracks.Count == 0 && _fsSubtitles.Count == 1 && _fsSubtitles[0].IsExternal)
            {
                EnumerateAllTracks();
            }

            VideoTrackMenuControl.Show(_fsSubtitles, _fsAudioTracks, _fsSelectedSubtitleIndex, _fsSelectedAudioIndex);
            _fsHideTimer.Stop();
        }

        private void OnVideoSubtitleSelected(object sender, SubtitleTrack track)
        {
            var playbackItem = _fsPlaybackItem ?? MediaPreview.CurrentPlaybackItem;
            if (playbackItem == null) return;

            try
            {
                for (uint i = 0; i < playbackItem.TimedMetadataTracks.Count; i++)
                {
                    playbackItem.TimedMetadataTracks.SetPresentationMode(i,
                        TimedMetadataTrackPresentationMode.Disabled);
                }

                if (track.IsExternal && _fsPlaybackItem != null)
                {
                    for (uint i = 0; i < _fsPlaybackItem.TimedMetadataTracks.Count; i++)
                    {
                        var ttTrack = _fsPlaybackItem.TimedMetadataTracks[(int)i];
                        if (ttTrack.TimedMetadataKind == TimedMetadataKind.Subtitle)
                        {
                            int externalIndex = _fsSubtitles.FindIndex(s => s.IsExternal && s.FilePath == track.FilePath);
                            int trackIndex = externalIndex >= 0 ? externalIndex : (int)i;

                            if (trackIndex == (int)i)
                            {
                                _fsPlaybackItem.TimedMetadataTracks.SetPresentationMode(i,
                                    TimedMetadataTrackPresentationMode.PlatformPresented);
                                _fsSelectedSubtitleIndex = _fsSubtitles.IndexOf(track);
                                ShowFsOsd($"Sub: {track.GetDisplayName()}");
                                Log.Info("OnVideoSubtitleSelected: enabled external subtitle '{Name}'", track.GetDisplayName());
                                return;
                            }
                        }
                    }
                }
                else if (track.EmbeddedIndex >= 0)
                {
                    int count = 0;
                    for (uint i = 0; i < playbackItem.TimedMetadataTracks.Count; i++)
                    {
                        var ttTrack = playbackItem.TimedMetadataTracks[(int)i];
                        if (ttTrack.TimedMetadataKind == TimedMetadataKind.Subtitle)
                        {
                            if (count == track.EmbeddedIndex)
                            {
                                playbackItem.TimedMetadataTracks.SetPresentationMode(i,
                                    TimedMetadataTrackPresentationMode.PlatformPresented);
                                if (_fsPlaybackItem != null)
                                    _fsSelectedSubtitleIndex = _fsSubtitles.IndexOf(track);
                                else
                                    MediaPreview.SelectSubtitleTrack(track.EmbeddedIndex);
                                Log.Info("OnVideoSubtitleSelected: enabled embedded subtitle '{Name}'", track.GetDisplayName());
                                return;
                            }
                            count++;
                        }
                    }
                }
                else
                {
                    if (_fsPlaybackItem != null)
                        _fsSelectedSubtitleIndex = -1;
                    Log.Info("OnVideoSubtitleSelected: subtitles disabled");
                }
            }
            catch (Exception ex)
            {
                Log.Warn("OnVideoSubtitleSelected failed", ex);
            }
        }

        private void OnVideoAudioTrackSelected(object sender, int trackIndex)
        {
            try
            {
                Log.Info("OnVideoAudioTrackSelected: trackIndex={Index} isFs={IsFs}",
                    trackIndex, _fsPlaybackItem != null);

                if (_fsPlaybackItem != null)
                {
                    if (trackIndex >= 0 && trackIndex < _fsPlaybackItem.AudioTracks.Count)
                    {
                        _fsPlaybackItem.AudioTracks.SelectedIndex = trackIndex;
                        _fsSelectedAudioIndex = trackIndex;

                        string name = trackIndex < _fsAudioTracks.Count
                            ? _fsAudioTracks[trackIndex].DisplayName
                            : $"Track {trackIndex + 1}";
                        ShowFsOsd($"Audio: {name}");
                        Log.Info("OnVideoAudioTrackSelected: selected audio track {Index} '{Name}'", trackIndex, name);

                        var pos = FsVideoSession.Position;
                        FsVideoPlayer.Pause();
                        FsVideoSession.Position = pos;
                        FsVideoPlayer.Play();
                    }
                }
                else
                {
                    MediaPreview.SwitchAudioTrack(trackIndex);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("OnVideoAudioTrackSelected failed", ex);
            }
        }

        private void CloseVideoFullScreen()
        {
            _fullscreenProgressTimer.Stop();
            _fsHideTimer.Stop();
            _fsOsdHideTimer.Stop();

            // Save playback state before clearing
            var restorePath = _fsVideoPath;
            var restorePos = FsVideoSession.Position;

            FsVideoPlayer.Pause();
            FsVideoPlayer.MediaEnded -= OnFsVideoMediaEnded;
            FsVideoPlayer.Source = null;
            _fsPlaybackItem = null;
            _fsVideoPlaying = false;
            _fsSubtitles?.Clear();
            _fsAudioTracks?.Clear();
            _fsSelectedSubtitleIndex = -1;
            _fsSelectedAudioIndex = -1;
            _fsIsNetwork = false;
            _fsNetworkPath = null;
            VideoTrackMenuControl.Close();
            VideoFullScreenPanel.Visibility = Visibility.Collapsed;
            Log.Info("CloseVideoFullScreen: stopped, track state cleared");

            // Restore inline preview at last position
            if (!string.IsNullOrEmpty(restorePath))
            {
                Log.Info("CloseVideoFullScreen: restoring inline preview at {Position}", restorePos);
                MediaPreview.LoadFile(restorePath);
                if (restorePos > TimeSpan.Zero)
                    MediaPreview.SeekTo(restorePos);
            }

            UpdateDisplayRequest();
            UpdateBgmDucking();
        }

        private async System.Threading.Tasks.Task HandleEditAsync(FileEntry entry)
        {
            if (entry == null)
            {
                Log.Warn("HandleEditAsync: null entry");
                return;
            }
            if (!entry.IsPortal && string.IsNullOrEmpty(entry.FullPath))
            {
                Log.Warn("HandleEditAsync: null/empty entry");
                return;
            }
            if (entry.IsPortal && string.IsNullOrEmpty(entry.PortalPackageFullName))
            {
                Log.Warn("HandleEditAsync: portal entry missing package full name");
                return;
            }
            Log.Info("HandleEditAsync: opening {Path} (ext={Ext})", entry.Name, System.IO.Path.GetExtension(entry.FullPath ?? entry.Name));

            if (entry.IsPortal)
            {
                // Cache the portal file first, then edit the cached copy. Save uploads back.
                OpProgressDialog.Show("Downloading for edit", entry.Name, "");
                string cachePath = await PortalCache.EnsureAsync(
                    XFiles.FileSystem.PortalBrowser.ToPortalEntry(entry), null);
                OpProgressDialog.Close();
                if (cachePath == null)
                {
                    Log.Warn("HandleEditAsync: portal download to cache failed for {Name}", entry.Name);
                    _ = AlertDialogControl.ShowAsync($"Failed to download \"{entry.Name}\".\n\nSee Log for details.", AlertType.Error);
                    return;
                }
                TextEditorOverlayControl.Show(cachePath, XFiles.FileSystem.PortalBrowser.ToPortalEntry(entry));
                return;
            }

            TextEditorOverlayControl.Show(entry.FullPath);
            Log.Dbg("HandleEditAsync: Show() returned, overlay visible={Vis}", TextEditorOverlayControl.IsOpen);
            await System.Threading.Tasks.Task.CompletedTask;
        }

        /// <summary>
        /// Opens a remote network text file in the editor from a temp cache copy;
        /// saving uploads the result back over SMB.
        /// </summary>
        private async System.Threading.Tasks.Task HandleNetworkTextEditAsync(FileEntry entry)
        {
            var current = _navigator.Current;
            if (current == null) return;
            string share = current.NetworkShareName ?? entry.NetworkShareName;
            string path = entry.NetworkPath;
            if (string.IsNullOrEmpty(share) || string.IsNullOrEmpty(path))
            {
                Log.Warn("HandleNetworkTextEdit: no share/path for {Name}", entry.Name);
                return;
            }

            long locationId = current.NetworkLocationId;
            OpProgressDialog.Show("Downloading for edit", entry.Name, "");
            string cachePath = await CacheRemoteFileAsync(locationId, share, path, entry.Name);
            OpProgressDialog.Close();
            if (cachePath == null)
            {
                Log.Warn("HandleNetworkTextEdit: cache download failed for {Name}", entry.Name);
                _ = AlertDialogControl.ShowAsync($"Failed to download \"{entry.Name}\".\n\nSee Log for details.", AlertType.Error);
                return;
            }

            TextEditorOverlayControl.NetworkUploadBack = (id, sh, np, local) =>
                _navigator.WriteNetworkFileAsync(id, sh, np, local);
            TextEditorOverlayControl.ShowNetwork(cachePath, locationId, share, path);
        }

        private async System.Threading.Tasks.Task HandleShareAsync(FileEntry entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry.FullPath))
            {
                Log.Warn("HandleShareAsync: null/empty entry");
                return;
            }
            Log.Info("HandleShareAsync: {File}", entry.FullPath);

            bool confirmed = await AlertDialogControl.ShowConfirmAsync(
                $"This file will be uploaded to gofile.io and remain available for a few days.\n\n" +
                $"Share \"{entry.Name}\"?");
            if (!confirmed)
            {
                Log.Verb("HandleShareAsync: user cancelled confirmation");
                return;
            }

            var cts = new System.Threading.CancellationTokenSource();

            OpProgressDialog.Show("Sharing", entry.Name, "");

            try
            {
                string url = await XFiles.Services.FileShareService.ShareAsync(
                    entry.FullPath,
                    statusText =>
                    {
                        _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                        {
                            if (OpProgressDialog.IsOpen)
                            {
                                OpProgressDialog.UpdateProgress(new FileOperations.OperationProgress
                                {
                                    FileName = statusText,
                                    PercentComplete = -1
                                });
                            }
                        });
                    },
                    (bytesUploaded, totalBytes) =>
                    {
                        _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                        {
                            if (OpProgressDialog.IsOpen)
                            {
                                OpProgressDialog.UpdateProgress(new FileOperations.OperationProgress
                                {
                                    FileName = $"Uploading {Formatting.FormatBytes(bytesUploaded)} / {Formatting.FormatBytes(totalBytes)}",
                                    PercentComplete = totalBytes > 0 ? (double)bytesUploaded / totalBytes * 100 : -1,
                                    BytesCopied = bytesUploaded,
                                    TotalBytes = totalBytes
                                });
                            }
                        });
                    },
                    cts.Token);

                OpProgressDialog.Complete();
                await System.Threading.Tasks.Task.Delay(400);
                OpProgressDialog.Close();

                if (!string.IsNullOrEmpty(url))
                {
                    ShareDialogControl.Show(url, "File Shared");
                }
                else
                {
                    Log.Warn("HandleShareAsync: upload returned null URL");
                    _ = AlertDialogControl.ShowAsync("Share failed: upload returned no URL.", AlertType.Error);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("HandleShareAsync: exception: {Error}", ex.Message);
                OpProgressDialog.Close();
                _ = AlertDialogControl.ShowAsync($"Share failed: {ex.Message}", AlertType.Error);
            }
        }

        private void OnFullscreenProgressTick(object sender, object e)
        {
            _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low, _fullscreenProgressHandler);
        }

        private void OnVideoFullScreenTapped(object sender, TappedRoutedEventArgs e)
        {
            if (FSControlsBar.Opacity > 0)
                CloseVideoFullScreen();
            else
                ShowFsControls();
        }

        private void ShowFsControls()
        {
            var sb = new Storyboard();
            var dur = new Duration(TimeSpan.FromMilliseconds(250));
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

            var animBar = new DoubleAnimation { To = 1.0, Duration = dur, EasingFunction = ease };
            Storyboard.SetTarget(animBar, FSControlsBar);
            Storyboard.SetTargetProperty(animBar, "Opacity");
            sb.Children.Add(animBar);

            var animLeg = new DoubleAnimation { To = 1.0, Duration = dur, EasingFunction = ease };
            Storyboard.SetTarget(animLeg, FSLegendText);
            Storyboard.SetTargetProperty(animLeg, "Opacity");
            sb.Children.Add(animLeg);

            sb.Begin();
            _fsHideTimer.Stop();
            _fsHideTimer.Start();
        }

        private void HideFsControls()
        {
            var sb = new Storyboard();
            var dur = new Duration(TimeSpan.FromMilliseconds(400));
            var ease = new CubicEase { EasingMode = EasingMode.EaseIn };

            var animBar = new DoubleAnimation { To = 0.0, Duration = dur, EasingFunction = ease };
            Storyboard.SetTarget(animBar, FSControlsBar);
            Storyboard.SetTargetProperty(animBar, "Opacity");
            sb.Children.Add(animBar);

            var animLeg = new DoubleAnimation { To = 0.0, Duration = dur, EasingFunction = ease };
            Storyboard.SetTarget(animLeg, FSLegendText);
            Storyboard.SetTargetProperty(animLeg, "Opacity");
            sb.Children.Add(animLeg);

            sb.Begin();
        }

        private void ShowFsOsd(string text, string iconSource = null, double hideDelayMs = 1500)
        {
            FsOsdText.Text = text;
            if (iconSource != null)
            {
                FsOsdIcon.Source = new Windows.UI.Xaml.Media.Imaging.BitmapImage(new Uri(iconSource));
                FsOsdIcon.Visibility = Visibility.Visible;
            }
            else
            {
                FsOsdIcon.Visibility = Visibility.Collapsed;
            }
            FsOsdBorder.Visibility = Visibility.Visible;
            var fadeIn = new Storyboard();
            var dur = new Duration(TimeSpan.FromMilliseconds(150));
            var anim = new DoubleAnimation { To = 1.0, Duration = dur, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(anim, FsOsdBorder);
            Storyboard.SetTargetProperty(anim, "Opacity");
            fadeIn.Children.Add(anim);
            fadeIn.Begin();
            _fsOsdHideTimer.Stop();
            _fsOsdHideTimer.Interval = TimeSpan.FromMilliseconds(hideDelayMs);
            _fsOsdHideTimer.Tick -= OnFsOsdHideTick;
            _fsOsdHideTimer.Tick += OnFsOsdHideTick;
            _fsOsdHideTimer.Start();
        }

        private void HideFsOsd()
        {
            var fadeOut = new Storyboard();
            var dur = new Duration(TimeSpan.FromMilliseconds(300));
            var anim = new DoubleAnimation { To = 0.0, Duration = dur, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            Storyboard.SetTarget(anim, FsOsdBorder);
            Storyboard.SetTargetProperty(anim, "Opacity");
            fadeOut.Children.Add(anim);
            fadeOut.Completed += (s, e) => FsOsdBorder.Visibility = Visibility.Collapsed;
            fadeOut.Begin();
        }

        private void OnFsOsdHideTick(object sender, object e)
        {
            _fsOsdHideTimer.Stop();
            HideFsOsd();
        }

        private DispatcherTimer _fsAudioOsdHideTimer = new DispatcherTimer();

        private void ShowAudioOsd(string text, string iconSource = null, double hideDelayMs = 1500)
        {
            FsAudioOsdText.Text = text;
            if (iconSource != null)
            {
                FsAudioOsdIcon.Source = new Windows.UI.Xaml.Media.Imaging.BitmapImage(new Uri(iconSource));
                FsAudioOsdIcon.Visibility = Visibility.Visible;
            }
            else
            {
                FsAudioOsdIcon.Visibility = Visibility.Collapsed;
            }
            FsAudioOsdBorder.Visibility = Visibility.Visible;
            var fadeIn = new Storyboard();
            var dur = new Duration(TimeSpan.FromMilliseconds(150));
            var anim = new DoubleAnimation { To = 1.0, Duration = dur, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(anim, FsAudioOsdBorder);
            Storyboard.SetTargetProperty(anim, "Opacity");
            fadeIn.Children.Add(anim);
            fadeIn.Begin();
            _fsAudioOsdHideTimer.Stop();
            _fsAudioOsdHideTimer.Interval = TimeSpan.FromMilliseconds(hideDelayMs);
            _fsAudioOsdHideTimer.Tick -= OnFsAudioOsdHideTick;
            _fsAudioOsdHideTimer.Tick += OnFsAudioOsdHideTick;
            _fsAudioOsdHideTimer.Start();
        }

        private void HideAudioOsd()
        {
            var fadeOut = new Storyboard();
            var dur = new Duration(TimeSpan.FromMilliseconds(300));
            var anim = new DoubleAnimation { To = 0.0, Duration = dur, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            Storyboard.SetTarget(anim, FsAudioOsdBorder);
            Storyboard.SetTargetProperty(anim, "Opacity");
            fadeOut.Children.Add(anim);
            fadeOut.Completed += (s, e) => FsAudioOsdBorder.Visibility = Visibility.Collapsed;
            fadeOut.Begin();
        }

        private void OnFsAudioOsdHideTick(object sender, object e)
        {
            _fsAudioOsdHideTimer.Stop();
            HideAudioOsd();
        }

        private static readonly (AudioFullscreenMode Mode, string Label)[] _fsModeOrder = new[]
        {
            (AudioFullscreenMode.Default, "Default"),
            (AudioFullscreenMode.RadialSpectrum, "Radial Spectrum"),
            (AudioFullscreenMode.Waveform, "Waveform"),
            (AudioFullscreenMode.Plasma, "Plasma"),
            (AudioFullscreenMode.Starfield, "Starfield"),
            (AudioFullscreenMode.SpiralSpectrum, "Spiral Spectrum"),
            (AudioFullscreenMode.MirrorTunnel, "Mirror Tunnel"),
            (AudioFullscreenMode.FireParticles, "Fire Particles"),
            (AudioFullscreenMode.Lissajous, "Lissajous"),
            (AudioFullscreenMode.TerrainGenerator, "Terrain Generator"),
            (AudioFullscreenMode.OrbitingCircles, "Orbiting Circles"),
            (AudioFullscreenMode.IsometricEqualizer, "Isometric Equalizer"),
            (AudioFullscreenMode.NeonGlare, "Neon Glare"),
            (AudioFullscreenMode.Kaleidoscope, "Kaleidoscope"),
            (AudioFullscreenMode.ParticleBurst, "Particle Burst"),
            (AudioFullscreenMode.RipplePulse, "Ripple Pulse"),
            (AudioFullscreenMode.FeedbackTrail, "Feedback Trail"),
            (AudioFullscreenMode.VoxelMatrix, "Voxel Matrix"),
            (AudioFullscreenMode.AnalogVUMeter, "Analog VU Meter"),
            (AudioFullscreenMode.CircularRadialSpectrum, "Circular Radial Spectrum"),
            (AudioFullscreenMode.RetroOscilloscope, "Retro Oscilloscope"),
            (AudioFullscreenMode.InfernoCore, "Inferno Core"),
            (AudioFullscreenMode.WaveformTunnel, "Waveform Tunnel"),
            (AudioFullscreenMode.GeissFluid, "Geiss Fluid"),
            (AudioFullscreenMode.Xbox360Boot, "Xbox 360 Boot"),
            (AudioFullscreenMode.InvertedBars, "Inverted Bars"),
            (AudioFullscreenMode.ThreeDO, "3DO Interactive Music Player"),
            (AudioFullscreenMode.ThreeDWave, "3D Wave"),
            (AudioFullscreenMode.ComancheTerrain, "Comanche Terrain"),
            (AudioFullscreenMode.SynthwaveVuMeter, "Synthwave VU Meter"),
            (AudioFullscreenMode.ClassicVUMeter, "Classic VU Meter"),
            (AudioFullscreenMode.NightCity, "Night City")
        };

        private DispatcherTimer _fsModeOsdTimer;

        public void CycleAudioVisualizer()
        {
            int count = Enum.GetValues(typeof(AudioFullscreenMode)).Length;
            int next = (int)_fsVisualizerMode;
            for (int i = 0; i < count; i++)
            {
                next = (next + 1) % count;
                var candidate = (AudioFullscreenMode)next;
                if (candidate == AudioFullscreenMode.Default || VisualizerRegistry.Resolve(candidate) != null)
                {
                    _fsVisualizerMode = candidate;
                    ApplyAudioVisualizerMode();
                    var modeEntry = _fsModeOrder.FirstOrDefault(m => m.Mode == candidate);
                    ShowModeOsd(modeEntry.Label ?? candidate.ToString());
                    // OSD removed: FsTrackInfoBorder
                    return;
                }
            }
        }

        private void ApplyAudioVisualizerMode()
        {
            bool showDefault = _fsVisualizerMode == AudioFullscreenMode.Default;
            FsAlbumArtBorder.Visibility = showDefault && _fsHasAlbumArt
                ? Visibility.Visible : Visibility.Collapsed;
            FsDefaultArtPanel.Visibility = showDefault && !_fsHasAlbumArt
                ? Visibility.Visible : Visibility.Collapsed;
            FsTitleText.Visibility = showDefault ? Visibility.Visible : Visibility.Collapsed;
            FsArtistText.Visibility = showDefault ? Visibility.Visible : Visibility.Collapsed;
            FsAlbumText.Visibility = showDefault ? Visibility.Visible : Visibility.Collapsed;
            FsVuMeter.Visibility = showDefault ? Visibility.Visible : Visibility.Collapsed;

            if (showDefault)
            {
                Log.Dbg("ApplyAudioVisualizerMode: DEFAULT (album art + VU)");
                AudioLevelService.Instance.QuantumSkipN = 1;
                FsVisualizerCanvas.Deactivate();
                FsVisualizerCanvas.DetachService();
                FsVisualizerCanvas.Visibility = Visibility.Collapsed;
            }
            else
            {
                var viz = VisualizerRegistry.Resolve(_fsVisualizerMode);
                Log.Info("ApplyAudioVisualizerMode: mode={Mode} viz={Viz}",
                    _fsVisualizerMode, viz?.GetType().Name ?? "null");
                if (viz != null)
                {
                    AudioLevelService.Instance.QuantumSkipN = 1;
                    FsVisualizerCanvas.AttachService(AudioLevelService.Instance);
                    FsVisualizerCanvas.Activate(viz);
                    FsVisualizerCanvas.Visibility = Visibility.Visible;
                }
            }
        }

        public void OnSelectVisualizerMenu()
        {
            if (!_isAudioFullscreen)
            {
                Log.Dbg("OnSelectVisualizerMenu: ignoring — picker only available in audio fullscreen");
                return;
            }
            Log.Info("OnSelectVisualizerMenu: opening picker");
            _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                _fsPickerItems = new List<VisualizerPickerItem>(_fsModeOrder.Length - 1);
                for (int i = 1; i < _fsModeOrder.Length; i++)
                {
                    _fsPickerItems.Add(new VisualizerPickerItem(
                        _fsModeOrder[i].Mode, _fsModeOrder[i].Label,
                        _fsModeOrder[i].Mode == _fsVisualizerMode));
                }
                FsVisualizerList.ItemsSource = _fsPickerItems;
                int currentIdx = _fsPickerItems.FindIndex(e => e.Mode == _fsVisualizerMode);
                FsVisualizerList.SelectedIndex = currentIdx >= 0 ? currentIdx : 0;
                var currentModeEntry = _fsModeOrder.FirstOrDefault(m => m.Mode == _fsVisualizerMode);
                FsPickerCurrentLabel.Text = "Current: " + (currentModeEntry.Label ?? _fsVisualizerMode.ToString());
                FsPickerCurrentLabel.Visibility = Visibility.Visible;
                FsVisualizerList.ScrollIntoView(FsVisualizerList.SelectedItem);
                FsVisualizerPicker.Visibility = Visibility.Visible;
                _fsPickerVisible = true;
            });
        }

        private void CloseFsPicker()
        {
            Log.Info("CloseFsPicker");
            FsVisualizerPicker.Visibility = Visibility.Collapsed;
            FsVisualizerList.ItemsSource = null;
            FsPickerCurrentLabel.Visibility = Visibility.Collapsed;
            FsPickerCurrentLabel.Text = "";
            _fsPickerItems = null;
            _fsPickerVisible = false;
        }

        private void ApplyFsPickerSelection()
        {
            if (_fsPickerItems == null || FsVisualizerList.SelectedIndex < 0) { CloseFsPicker(); return; }
            var selected = _fsPickerItems[FsVisualizerList.SelectedIndex];
            Log.Info("ApplyFsPickerSelection: mode={Mode} label={Label}", selected.Mode, selected.Label);
            CloseFsPicker();
            _fsVisualizerMode = selected.Mode;
            ApplyAudioVisualizerMode();
            ShowModeOsd(selected.Label);
        }

        private void ShowModeOsd(string label)
        {
            FsModeText.Text = label;
            FsModeText.Visibility = Visibility.Visible;
            FsModeText.Opacity = 1.0;

            if (_fsModeOsdTimer == null)
            {
                _fsModeOsdTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
                _fsModeOsdTimer.Tick += (s, e) =>
                {
                    _fsModeOsdTimer.Stop();
                    var fade = new Storyboard();
                    var dur = new Duration(TimeSpan.FromMilliseconds(300));
                    var anim = new DoubleAnimation { To = 0.0, Duration = dur, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
                    Storyboard.SetTarget(anim, FsModeText);
                    Storyboard.SetTargetProperty(anim, "Opacity");
                    fade.Children.Add(anim);
                    fade.Completed += (s2, e2) => FsModeText.Visibility = Visibility.Collapsed;
                    fade.Begin();
                };
            }
            _fsModeOsdTimer.Stop();
            _fsModeOsdTimer.Start();
        }

        // OSD removed: _fsTrackInfoTimer + ShowTrackInfoOsd

        private void OnFsHideTimerTick(object sender, object e)
        {
            _fsHideTimer.Stop();
            HideFsControls();
        }

        private void OnFsControlsAnyInput()
        {
            if (VideoFullScreenPanel.Visibility == Visibility.Visible)
                ShowFsControls();
        }

        private void OnFsVideoInput()
        {
            if (VideoFullScreenPanel.Visibility != Visibility.Visible) return;
            ShowFsControls();
            if (_fsVideoPlaying)
            {
                FsVideoPlayer.Pause();
                FSPlayPauseIcon.Glyph = "\uE768";
                _fsVideoPlaying = false;
                ShowFsOsd("PAUSE", "ms-appx:///Assets/Views/MillerColumnsPage/osd/osd-pause-48.png");
            }
            else
            {
                FsVideoPlayer.Play();
                FSPlayPauseIcon.Glyph = "\uE769";
                _fsVideoPlaying = true;
                ShowFsOsd("PLAY", "ms-appx:///Assets/Views/MillerColumnsPage/osd/osd-play-48.png");
            }
        }

        private void UpdateFsVolume(float stickY)
        {
            const double Deadzone = 0.12;
            if (Math.Abs(stickY) < Deadzone) return;

            double magnitude = Math.Abs(stickY);
            double curved = magnitude * magnitude;
            double direction = stickY > 0 ? 1.0 : -1.0;
            double delta = direction * curved * 0.02;
            if (AudioFullScreenPanel.Visibility == Visibility.Visible)
            {
                _audioVolume = Math.Max(0.0, Math.Min(1.0, _audioVolume + delta));
                AudioLevelService.Instance?.SetVolume(_audioVolume);
                FsVolumeText.Text = $"Vol {(int)(_audioVolume * 100)}%";
                ShowAudioOsd($"Vol {(int)(_audioVolume * 100)}%", null, 1200);
            }
            else if (VideoFullScreenPanel.Visibility == Visibility.Visible)
            {
                _fsVolume = Math.Max(0.0, Math.Min(1.0, _fsVolume + delta));
                ShowFsControls();
                FsVideoPlayer.Volume = _fsVolume;
                FSVolumeText.Text = $"Vol {(int)(_fsVolume * 100)}%";
                ShowFsOsd($"Vol {(int)(_fsVolume * 100)}%", null, 1200);
                _ = Settings.XFilesSettings.SetMediaVolumeAsync((int)(_fsVolume * 100));
            }
            else if (_isMediaPlayerActive)
            {
                _fsVolume = Math.Max(0.0, Math.Min(1.0, _fsVolume + delta));
                MediaPreview.SetVolume(_fsVolume);
                _ = Settings.XFilesSettings.SetMediaVolumeAsync((int)(_fsVolume * 100));
            }
        }

        private bool _fsVideoPlaying = false;
        private double _fsVolume = 0.75;
        private string _fsVideoPath;
        private TimeSpan _fsPendingPosition;
        private List<SubtitleTrack> _fsSubtitles;
        private List<AudioTrackInfo> _fsAudioTracks;
        private int _fsSelectedSubtitleIndex = -1;
        private int _fsSelectedAudioIndex = 0;
        
        private bool _fsSuppressTrackEvent;
        private bool _fsAudioEnded;
        private Windows.Media.Playback.MediaPlaybackItem _fsPlaybackItem;

        private double _seekCooldown;
        private double _ltHoldMs;
        private double _rtHoldMs;

        /// <summary>
        /// Load persisted media volume into _fsVolume/_audioVolume.
        /// Called from OnLoaded (fire-and-forget).
        /// </summary>
        internal async Task LoadMediaVolumeAsync()
        {
            try
            {
                int vol = await Settings.XFilesSettings.GetMediaVolumeAsync();
                _fsVolume = vol / 100.0;
                _audioVolume = vol / 100.0;
            }
            catch { }
        }
        private bool _ltWasDown;
        private bool _rtWasDown;

        // Single shared timer for all fullscreen progress updates (video + audio)
        private DispatcherTimer _fullscreenProgressTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };

        private DispatcherTimer _fsHideTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };

        private readonly DispatchedHandler _fullscreenProgressHandler;

        private DispatcherTimer _fsOsdHideTimer = new DispatcherTimer();

        // Media load debounce — avoids loading video/audio on every scroll tick
        private DispatcherTimer _mediaLoadTimer;
        private string _pendingMediaPath;
        // Network (SMB) inline preview state: set when the inline player is
        // streaming from a remote share, so LB/RB / auto-advance navigate the
        // network list instead of the local FullPath list.
        private long _previewNetworkLocationId;
        private string _previewNetworkShare;
        private string _previewNetworkPath;

        public void StopAllTimers()
        {
            _fullscreenProgressTimer.Stop();
            _fsHideTimer.Stop();
            _fsOsdHideTimer.Stop();
            _mediaLoadTimer.Stop();
            ImageFullScreen?.Close();
            MediaPreview?.StopPlayer();
            if (_isAudioFullscreen) CloseAudioFullscreen();
            StopFsAudioAnalysis();
        }

        private void OnMediaLoadTimerTick(object sender, object e)
        {
            _mediaLoadTimer.Stop();
            if (!string.IsNullOrEmpty(_pendingMediaPath))
            {
                if (!MediaPreview.IsFileLoaded(_pendingMediaPath) && !MediaPreview.IsPlayerActive)
                {
                    Log.Info("OnMediaLoadTimerTick: loading {Path}", _pendingMediaPath);
                    MediaPreview.LoadFile(_pendingMediaPath);
                }
                _pendingMediaPath = null;
            }
        }

        // --- Fullscreen Audio ---

        private bool _isAudioFullscreen;
        private string _audioFullscreenPath;
        // Network (SMB) fullscreen state: when set, the fullscreen player is
        // streaming from a remote share and LB/RB must navigate the network list
        // instead of the local FullPath list.
        private bool _fsIsNetwork;
        private long _fsNetworkLocationId;
        private string _fsNetworkShare;
        private string _fsNetworkPath;
        private double _audioVolume = 0.75;
        private AudioFullscreenMode _fsVisualizerMode;
        private DispatcherTimer _fsVisualizerTimer = new DispatcherTimer();
        private bool _fsHasAlbumArt;
        private Windows.UI.Xaml.Media.Imaging.BitmapImage _fsAlbumArtBitmap;
        private MetadataGuesser _fsMetadataGuesser = new MetadataGuesser();
        private int _fsGeneration;
        private int _prefetchGeneration;
        private bool _fsPickerVisible;
        private List<VisualizerPickerItem> _fsPickerItems;

        // Chiptune subsong state for the fullscreen audio player. Track navigation
        // advances subsongs within the same source when _fsChiptuneTrackCount > 1,
        // otherwise it advances to the next audio file in the list.
        private string _fsChiptuneSource;
        private int _fsChiptuneTrack;
        private int _fsChiptuneTrackCount = 1;

        // MediaPlayer/Session helpers for fullscreen video + audio (migrated from MediaElement)
        private Windows.Media.Playback.MediaPlayer FsVideoPlayer => VideoFullScreenPlayer.MediaPlayer;
        private Windows.Media.Playback.MediaPlaybackSession FsVideoSession => FsVideoPlayer.PlaybackSession;
        private Windows.Media.Playback.MediaPlayer FsAudioPlayer2 => FsAudioPlayer.MediaPlayer;
        private Windows.Media.Playback.MediaPlaybackSession FsAudioSession => FsAudioPlayer2.PlaybackSession;

        public async void OpenAudioFullscreen(string filePath, TimeSpan position, int chiptuneTrack = -1)
        {
            Log.Info("OpenAudioFullscreen: {Path}", filePath);
            int gen = ++_fsGeneration;
            bool wasAlreadyFullscreen = _isAudioFullscreen;
            _audioFullscreenPath = filePath;
            _fsIsNetwork = false;
            _fsNetworkPath = null;
            _isAudioFullscreen = true;
            _fsAudioEnded = false;

            MediaPreview.Stop();

            bool fsChiptune = false;
            string fsDisplayPath = filePath;

            StopFsAudioAnalysis();
            if (!wasAlreadyFullscreen)
                _fsVisualizerMode = AudioFullscreenMode.Default;
            AudioLevelService.Instance.MediaOpened += OnFsAudioOpened;
            AudioLevelService.Instance.MediaEnded += OnFsAudioEnded;
            AudioLevelService.Instance.MediaFailed += OnFsAudioFailed;

            // Show the fullscreen surface immediately — a chiptune decode can take
            // seconds on first render, and the VU meter must be visible up front.
            FsTitleText.Text = System.IO.Path.GetFileNameWithoutExtension(fsDisplayPath);
            FsArtistText.Text = "";
            FsArtistText.Visibility = Visibility.Collapsed;
            FsAlbumText.Text = "";
            FsAlbumText.Visibility = Visibility.Collapsed;
            FsAlbumArtBorder.Visibility = Visibility.Collapsed;
            FsDefaultArtPanel.Visibility = Visibility.Visible;
            _fsHasAlbumArt = false;
            AudioFullScreenPanel.Visibility = Visibility.Visible;
            SetFsLoading(true);
            _fsHideTimer.Stop();
            FsAudioProgress.Value = 0;
            FsCurrentTimeText.Text = "0:00";
            FsTotalTimeText.Text = "0:00";
#if AUDIO_ANALYSIS
            FsVuMeter.AttachService(AudioLevelService.Instance);
#endif
            FsVuMeter.EnsureInitialized();
            UpdateMediaPlayerFocusUI();
            UpdateDisplayRequest();
            UpdateBgmDucking();

            // Chiptune sources have no playable path — decode to cached WAV first.
            if (RetroAudioPlayer.IsChiptuneFile(filePath))
            {
                fsChiptune = true;
                if (chiptuneTrack >= 0)
                    MediaPreview.SetChiptuneTrack(filePath, chiptuneTrack);
                string wav = await MediaPreview.GetChiptuneStreamingWavPathAsync(filePath);
                if (wav == null)
                {
                    Log.Warn("OpenAudioFullscreen: chiptune decode failed for {Path}", filePath);
                    CloseAudioFullscreen();
                    return;
                }
                if (gen != _fsGeneration)
                {
                    Log.Dbg("OpenAudioFullscreen: stale generation after decode, aborting");
                    SetFsLoading(false);
                    return;
                }
                _fsChiptuneSource = filePath;
                _fsChiptuneTrack = MediaPreview.CurrentChiptuneTrack;
                _fsChiptuneTrackCount = MediaPreview.CurrentChiptuneTrackCount;
                fsDisplayPath = filePath;
                filePath = wav;
            }
            else
            {
                _fsChiptuneSource = null;
                _fsChiptuneTrack = 0;
                _fsChiptuneTrackCount = 1;
            }

            await AudioLevelService.Instance.LoadAndPlay(filePath, forceStream: fsChiptune);

            if (gen != _fsGeneration)
            {
                Log.Dbg("OpenAudioFullscreen: stale generation, aborting");
                SetFsLoading(false);
                return;
            }

            if (position > TimeSpan.Zero)
                AudioLevelService.Instance.Seek(position);

            FsPlayPauseIcon.Glyph = "\uE769";
            FsVolumeText.Text = $"Vol {(int)(_audioVolume * 100)}%";

            if (_fullscreenProgressTimer.IsEnabled == false)
                _fullscreenProgressTimer.Start();

            _ = LoadAudioFullscreenMetadataAsync(fsDisplayPath);

            if (_fsVisualizerMode != AudioFullscreenMode.Default)
                ApplyAudioVisualizerMode();

            PrefetchNextChiptuneTrack();
        }

        /// <summary>
        /// Fullscreen audio from a remote (network) stream. Playback starts as soon
        /// as the first bytes arrive — no full download, no growing-file cache.
        /// Metadata/album-art enrichment is skipped (no local path for the guesser).
        /// </summary>
        public async Task OpenRemoteAudioFullscreenAsync(
            string title, Windows.Storage.Streams.IRandomAccessStream stream, string mimeType,
            long locationId = 0, string share = null, string path = null,
            TimeSpan position = default)
        {
            Log.Info("OpenRemoteAudioFullscreen: '{Title}' mime={Mime}", title, mimeType);
            int gen = ++_fsGeneration;
            bool wasAlreadyFullscreen = _isAudioFullscreen;
            _audioFullscreenPath = "(network stream)";
            _fsIsNetwork = path != null;
            _fsNetworkLocationId = locationId;
            _fsNetworkShare = share;
            _fsNetworkPath = path;
            _isAudioFullscreen = true;
            _fsAudioEnded = false;

            MediaPreview.Stop();

            StopFsAudioAnalysis();
            if (!wasAlreadyFullscreen)
                _fsVisualizerMode = AudioFullscreenMode.Default;
            AudioLevelService.Instance.MediaOpened += OnFsAudioOpened;
            AudioLevelService.Instance.MediaEnded += OnFsAudioEnded;
            AudioLevelService.Instance.MediaFailed += OnFsAudioFailed;

            FsTitleText.Text = title;
            FsArtistText.Text = "";
            FsArtistText.Visibility = Visibility.Collapsed;
            FsAlbumText.Text = "";
            FsAlbumText.Visibility = Visibility.Collapsed;
            FsAlbumArtBorder.Visibility = Visibility.Collapsed;
            FsDefaultArtPanel.Visibility = Visibility.Visible;
            _fsHasAlbumArt = false;
            _fsChiptuneSource = null;
            _fsChiptuneTrack = 0;
            _fsChiptuneTrackCount = 1;
            AudioFullScreenPanel.Visibility = Visibility.Visible;
            SetFsLoading(true);
            _fsHideTimer.Stop();
            FsAudioProgress.Value = 0;
            FsCurrentTimeText.Text = "0:00";
            FsTotalTimeText.Text = "0:00";
#if AUDIO_ANALYSIS
            FsVuMeter.AttachService(AudioLevelService.Instance);
#endif
            FsVuMeter.EnsureInitialized();
            UpdateMediaPlayerFocusUI();
            UpdateDisplayRequest();
            UpdateBgmDucking();

            await AudioLevelService.Instance.PlayRemoteStreamAsync(stream, mimeType);

            if (gen != _fsGeneration)
            {
                Log.Dbg("OpenRemoteAudioFullscreen: stale generation, aborting");
                SetFsLoading(false);
                return;
            }

            if (position > TimeSpan.Zero)
                AudioLevelService.Instance.Seek(position);

            FsPlayPauseIcon.Glyph = "\uE769";
            FsVolumeText.Text = $"Vol {(int)(_audioVolume * 100)}%";

            if (_fullscreenProgressTimer.IsEnabled == false)
                _fullscreenProgressTimer.Start();

            if (_fsVisualizerMode != AudioFullscreenMode.Default)
                ApplyAudioVisualizerMode();

            if (!string.IsNullOrEmpty(path) && gen == _fsGeneration)
            {
                Func<System.IO.Stream> reopenMeta = () =>
                    Task.Run(() => _navigator.OpenNetworkStreamAsync(locationId, share, path))
                        .GetAwaiter().GetResult();
                _ = LoadAudioFullscreenMetadataAsync(path, reopenMeta, path);
            }
        }

        private async Task LoadAudioFullscreenMetadataAsync(string filePath)
        {
            await LoadAudioFullscreenMetadataAsync(filePath, null, null);
        }

        /// <summary>
        /// Fullscreen metadata for a remote (network) audio stream. The ID3 tag is
        /// read from the leading bytes of a freshly opened SMB stream supplied by
        /// <paramref name="openStream"/>; filename parsing uses the remote display
        /// path. Stale-check key is the network path (LB/RB bumps _fsGeneration).
        /// </summary>
        private async Task LoadAudioFullscreenMetadataAsync(string filePath, Func<System.IO.Stream> openStream, string networkPath)
        {
            int gen = _fsGeneration;
            try
            {
                Log.Dbg("FsMetadata: starting async load for {Path}", filePath);
                _fsMetadataGuesser.SetInternetAvailable(true);
                bool skipOnline = openStream == null && RetroAudioPlayer.IsChiptuneFile(filePath);
                var match = openStream != null
                    ? await _fsMetadataGuesser.ResolveStreamAsync(filePath, openStream)
                    : await _fsMetadataGuesser.ResolveAsync(filePath, skipOnline: skipOnline);
                var tag = match?.Metadata;

                Log.Info("FsMetadata: source={Source} score={Score:F2} title='{Title}' artist='{Artist}' album='{Album}' art={HasArt}",
                    match?.Source, match?.Confidence, tag?.Title, tag?.Artist, tag?.Album, tag?.HasAlbumArt);

                bool stale = networkPath != null
                    ? gen != _fsGeneration || _fsNetworkPath != networkPath
                    : gen != _fsGeneration || _audioFullscreenPath != filePath;
                if (stale)
                {
                    Log.Dbg("FsMetadata: stale result for {Path}, discarding", filePath);
                    return;
                }

                bool hasArt = tag?.HasAlbumArt == true;
                _fsHasAlbumArt = hasArt;

                if (hasArt)
                {
                    var bitmap = new Windows.UI.Xaml.Media.Imaging.BitmapImage();
                    using (var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream())
                    {
                        await stream.WriteAsync(tag.AlbumArt.AsBuffer());
                        stream.Seek(0);
                        await bitmap.SetSourceAsync(stream);
                    }
                    _fsAlbumArtBitmap = bitmap;
                    FsAlbumArtBorder.Visibility = Visibility.Visible;
                    FsDefaultArtPanel.Visibility = Visibility.Collapsed;
                    FsAlbumArtImage.Source = bitmap;
                }

                if (tag?.HasTitle == true)
                    FsTitleText.Text = tag.Title;
                if (tag?.HasArtist == true)
                {
                    FsArtistText.Text = tag.Artist;
                    FsArtistText.Visibility = Visibility.Visible;
                }
                if (tag?.HasAlbum == true)
                {
                    FsAlbumText.Text = tag.Album;
                    FsAlbumText.Visibility = Visibility.Visible;
                }

                Log.Info("FsMetadata: applied title={Title} artist={Artist} album={Album} art={HasArt}",
                    tag?.Title, tag?.Artist, tag?.Album, hasArt);

                ApplyAudioVisualizerMode();
                // OSD removed: ShowTrackInfoOsd
            }
            catch (Exception ex)
            {
                Log.Warn("FsMetadata: failed for {Path}", filePath, ex);
            }
        }

        public void CloseAudioFullscreen()
        {
            Log.Info("CloseAudioFullscreen");
            ++_fsGeneration; // abort in-flight chiptune decodes
            ++_prefetchGeneration; // abort in-flight next-track prefetches
            _fsIsNetwork = false;
            _fsNetworkPath = null;
            StopFsAudioAnalysis();
            FsVisualizerCanvas.Deactivate();
            FsVisualizerCanvas.DetachService();
            FsVisualizerCanvas.Visibility = Visibility.Collapsed;
            // OSD removed: FsTrackInfoBorder
            _fsVisualizerMode = AudioFullscreenMode.Default;
            _isAudioFullscreen = false;
            _audioFullscreenPath = null;
            _fsChiptuneSource = null;
            _fsChiptuneTrack = 0;
            _fsChiptuneTrackCount = 1;
            AudioFullScreenPanel.Visibility = Visibility.Collapsed;
            SetFsLoading(false);
            // Stop shared progress timer only if no video fullscreen is active
            if (VideoFullScreenPanel.Visibility != Visibility.Visible)
                _fullscreenProgressTimer.Stop();
            UpdateDisplayRequest();
            UpdateBgmDucking();
        }

        public void ToggleAudioFullscreenPlayPause()
        {
            if (!AudioLevelService.Instance.IsFileLoaded) return;

            AudioLevelService.Instance.TogglePlayPause();

            if (AudioLevelService.Instance.IsPlaying)
            {
                FsPlayPauseIcon.Glyph = "\uE769";
                ShowAudioOsd("Play", "ms-appx:///Assets/Views/MillerColumnsPage/osd/osd-play-48.png", 1200);
            }
            else
            {
                FsPlayPauseIcon.Glyph = "\uE768";
                ShowAudioOsd("Pause", "ms-appx:///Assets/Views/MillerColumnsPage/osd/osd-pause-48.png", 1200);
            }
        }

        private bool NavigatePreviewTrack(int direction)
        {
            // Remote (SMB) inline track: navigate the network list, not local paths.
            if (_previewNetworkPath != null && _navigator.Current != null)
                return NavigatePreviewTrackNetwork(direction);

            if (string.IsNullOrEmpty(MediaPreview.CurrentFilePath) || _navigator.Current == null)
            {
                Log.Warn("NavigatePreviewTrack: early exit — filePath={FilePath} current={Current}", MediaPreview.CurrentFilePath ?? "(null)", _navigator.Current != null);
                return false;
            }

            var audioFiles = _navigator.Current.Entries
                .Where(e => !e.IsDirectory && (e.IsChiptune
                    || FilePreviewService.IsAudioFile(System.IO.Path.GetExtension(e.Name))
                    || FilePreviewService.IsChiptuneFile(System.IO.Path.GetExtension(e.Name))))
                .ToList();

            if (audioFiles.Count == 0)
            {
                Log.Warn("NavigatePreviewTrack: no audio files in current directory ({Total} entries total)", _navigator.Current.Entries.Count);
                return false;
            }

            int currentIdx = -1;

            // Multi-track chiptune: match the current subsong (all track entries
            // share the same FullPath, so the source alone is ambiguous).
            if (MediaPreview.CurrentChiptuneSource != null && MediaPreview.CurrentChiptuneTrackCount > 1 &&
                string.Equals(MediaPreview.CurrentChiptuneSource, MediaPreview.CurrentFilePath, StringComparison.OrdinalIgnoreCase))
            {
                currentIdx = audioFiles.FindIndex(e =>
                    e.IsChiptune && e.ChiptuneTrackIndex == MediaPreview.CurrentChiptuneTrack &&
                    string.Equals(e.ChiptuneSourcePath ?? e.FullPath, MediaPreview.CurrentChiptuneSource, StringComparison.OrdinalIgnoreCase));
            }

            if (currentIdx < 0)
            {
                currentIdx = audioFiles.FindIndex(e =>
                    string.Equals(e.FullPath, MediaPreview.CurrentFilePath, StringComparison.OrdinalIgnoreCase));
            }

            Log.Info("NavigatePreviewTrack: {Count} audio files, currentIdx={Idx}, direction={Dir}", audioFiles.Count, currentIdx, direction > 0 ? "next" : "prev");

            if (currentIdx < 0)
            {
                Log.Warn("NavigatePreviewTrack: current {Path} not in audio list — aborting", MediaPreview.CurrentFilePath);
                return false;
            }

            int nextIdx = currentIdx + direction;
            if (nextIdx < 0) nextIdx = audioFiles.Count - 1;
            if (nextIdx >= audioFiles.Count) nextIdx = 0;

            var nextFile = audioFiles[nextIdx];
            Log.Info("NavigatePreviewTrack: {Direction} to {Path}", direction > 0 ? "next" : "prev", nextFile.FullPath);

            int mainIdx = _navigator.Current.Entries.IndexOf(nextFile);
            if (mainIdx >= 0)
            {
                _updating = true;
                CurrentList.SelectedIndex = mainIdx;
                _updating = false;

                if (_navigator.Preview != null)
                    _navigator.Preview.PreviewFilePath = nextFile.FullPath;

                int totalCount = _navigator.Current?.Entries.Count ?? 0;
                FooterItemCount.Text = totalCount > 0 ? $"{mainIdx + 1}/{totalCount}" : "";
            }

            // Chiptune track entry: load the specific subsong (source + track).
            if (nextFile.IsChiptune)
            {
                MediaPreview.LoadChiptuneTrack(nextFile.ChiptuneSourcePath ?? nextFile.FullPath, nextFile.ChiptuneTrackIndex);
                MediaPreview.TogglePlayPause();
                return true;
            }

            MediaPreview.Stop();
            MediaPreview.LoadFile(nextFile.FullPath);
            MediaPreview.TogglePlayPause();
            return true;
        }

        private void NavigatePreviewVideoTrack(int direction)
        {
            // Remote (SMB) inline video: navigate the network list, not local paths.
            if (_previewNetworkPath != null && _navigator.Current != null)
            {
                NavigatePreviewVideoTrackNetwork(direction);
                return;
            }

            if (string.IsNullOrEmpty(MediaPreview.CurrentFilePath) || _navigator.Current == null)
            {
                Log.Warn("NavigatePreviewVideoTrack: early exit — filePath={FilePath} current={Current}", MediaPreview.CurrentFilePath ?? "(null)", _navigator.Current != null);
                return;
            }

            var videoFiles = _navigator.Current.Entries
                .Where(e => !e.IsDirectory && FilePreviewService.IsVideoFile(System.IO.Path.GetExtension(e.Name)))
                .ToList();

            if (videoFiles.Count == 0)
            {
                Log.Warn("NavigatePreviewVideoTrack: no video files in current directory ({Total} entries total)", _navigator.Current.Entries.Count);
                return;
            }

            int currentIdx = videoFiles.FindIndex(e =>
                string.Equals(e.FullPath, MediaPreview.CurrentFilePath, StringComparison.OrdinalIgnoreCase));

            Log.Info("NavigatePreviewVideoTrack: {Count} video files, currentIdx={Idx}, direction={Dir}", videoFiles.Count, currentIdx, direction > 0 ? "next" : "prev");

            int nextIdx = currentIdx + direction;
            if (nextIdx < 0) nextIdx = videoFiles.Count - 1;
            if (nextIdx >= videoFiles.Count) nextIdx = 0;

            var nextFile = videoFiles[nextIdx];
            Log.Info("NavigatePreviewVideoTrack: {Direction} to {Path}", direction > 0 ? "next" : "prev", nextFile.FullPath);

            int mainIdx = _navigator.Current.Entries.IndexOf(nextFile);
            if (mainIdx >= 0)
            {
                _updating = true;
                CurrentList.SelectedIndex = mainIdx;
                _updating = false;

                if (_navigator.Preview != null)
                    _navigator.Preview.PreviewFilePath = nextFile.FullPath;

                int totalCount = _navigator.Current?.Entries.Count ?? 0;
                FooterItemCount.Text = totalCount > 0 ? $"{mainIdx + 1}/{totalCount}" : "";
            }

            MediaPreview.Stop();
            MediaPreview.LoadFile(nextFile.FullPath);
            MediaPreview.TogglePlayPause();
        }

        // --- Network (SMB) remote navigation parity ---

        /// <summary>LB/RB in the inline player while a remote network track plays.</summary>
        private bool NavigatePreviewTrackNetwork(int direction)
        {
            var current = _navigator.Current;
            if (current == null) return false;

            // Drilled-in remote chiptune: the current column IS the track list of one
            // chip (entries = local-cache subsongs). Navigate the tracks, not the files.
            if (current.IsChiptune)
                return NavigatePreviewChiptuneTracks(direction);

            if (string.IsNullOrEmpty(_previewNetworkPath)) return false;

            var audioFiles = current.Entries
                .Where(e => !e.IsDirectory && e.IsNetwork &&
                    (FilePreviewService.IsAudioFile(System.IO.Path.GetExtension(e.Name))
                     || FilePreviewService.IsChiptuneFile(System.IO.Path.GetExtension(e.Name))))
                .ToList();

            if (audioFiles.Count == 0)
            {
                Log.Warn("NavigatePreviewTrackNetwork: no network audio files in current list");
                return false;
            }

            int currentIdx = audioFiles.FindIndex(e =>
                string.Equals(e.NetworkPath, _previewNetworkPath, StringComparison.OrdinalIgnoreCase));
            if (currentIdx < 0)
            {
                Log.Warn("NavigatePreviewTrackNetwork: current {Path} not in network list — aborting", _previewNetworkPath);
                return false;
            }

            int nextIdx = currentIdx + direction;
            if (nextIdx < 0) nextIdx = audioFiles.Count - 1;
            if (nextIdx >= audioFiles.Count) nextIdx = 0;

            var nextFile = audioFiles[nextIdx];
            Log.Info("NavigatePreviewTrackNetwork: {Direction} to {Path}", direction > 0 ? "next" : "prev", nextFile.NetworkPath);

            int mainIdx = current.Entries.IndexOf(nextFile);
            if (mainIdx >= 0)
            {
                _updating = true;
                CurrentList.SelectedIndex = mainIdx;
                _updating = false;
                int totalCount = current.Entries.Count;
                FooterItemCount.Text = totalCount > 0 ? $"{mainIdx + 1}/{totalCount}" : "";
            }

            _previewNetworkPath = nextFile.NetworkPath;
            _ = OpenPreviewNetworkTrackAsync(nextFile);
            return true;
        }

        /// <summary>LB/RB inside a drilled-in remote chiptune track list: move to the
        /// next/prev subsong of the SAME cached chip (mirrors the local path).</summary>
        private bool NavigatePreviewChiptuneTracks(int direction)
        {
            var current = _navigator.Current;
            var tracks = current.Entries.Where(e => e.IsChiptune && e.ChiptuneTrackIndex >= 0).ToList();
            if (tracks.Count == 0)
            {
                Log.Warn("NavigatePreviewChiptuneTracks: no track entries in chiptune column");
                return false;
            }

            int currentIdx = tracks.FindIndex(e =>
                e.ChiptuneTrackIndex == MediaPreview.CurrentChiptuneTrack &&
                string.Equals(e.ChiptuneSourcePath, MediaPreview.CurrentChiptuneSource,
                    StringComparison.OrdinalIgnoreCase));
            if (currentIdx < 0)
            {
                var selected = current.SelectedIndex >= 0 && current.SelectedIndex < current.Entries.Count
                    ? current.Entries[current.SelectedIndex]
                    : null;
                currentIdx = selected != null && selected.IsChiptune
                    ? tracks.IndexOf(selected)
                    : 0;
                if (currentIdx < 0) currentIdx = 0;
            }

            int nextIdx = currentIdx + direction;
            if (nextIdx < 0) nextIdx = tracks.Count - 1;
            if (nextIdx >= tracks.Count) nextIdx = 0;

            var nextTrack = tracks[nextIdx];
            Log.Info("NavigatePreviewChiptuneTracks: {Direction} to {Source} track {Track}",
                direction > 0 ? "next" : "prev", nextTrack.ChiptuneSourcePath, nextTrack.ChiptuneTrackIndex);

            int mainIdx = current.Entries.IndexOf(nextTrack);
            if (mainIdx >= 0)
            {
                _updating = true;
                CurrentList.SelectedIndex = mainIdx;
                _updating = false;
                int totalCount = current.Entries.Count;
                FooterItemCount.Text = totalCount > 0 ? $"{mainIdx + 1}/{totalCount}" : "";
            }

            MediaPreview.LoadChiptuneTrack(nextTrack.ChiptuneSourcePath, nextTrack.ChiptuneTrackIndex);
            MediaPreview.TogglePlayPause();
            return true;
        }

        private async Task OpenPreviewNetworkTrackAsync(FileEntry file)
        {
            try
            {
                string ext = System.IO.Path.GetExtension(file.Name);

                if (RetroAudioPlayer.IsChiptuneFile(ext))
                {
                    string tempPath = await CacheRemoteFileAsync(_previewNetworkLocationId, _previewNetworkShare, file.NetworkPath, file.Name);
                    if (tempPath == null)
                    {
                        Log.Warn("OpenPreviewNetworkTrack: chiptune cache failed for {Name}", file.Name);
                        return;
                    }
                    _mediaLoadTimer.Stop();
                    _pendingMediaPath = null;
                    MediaPreview.SetNetworkContext(_previewNetworkShare, file.NetworkPath);
                    MediaPreview.Stop();
                    MediaPreview.LoadChiptuneTrack(tempPath, 0);
                    MediaPreview.TogglePlayPause();
                    return;
                }

                System.IO.Stream stream = await _navigator.OpenNetworkStreamAsync(
                    _previewNetworkLocationId, _previewNetworkShare, file.NetworkPath);
                if (stream == null) return;

                Func<System.IO.Stream> reopen = () =>
                    Task.Run(() => _navigator.OpenNetworkStreamAsync(
                        _previewNetworkLocationId, _previewNetworkShare, file.NetworkPath))
                        .GetAwaiter().GetResult();

                MediaPreview.SetNetworkContext(_previewNetworkShare, file.NetworkPath);

                if (FilePreviewService.IsAudioFile(ext))
                {
                    await MediaPreview.LoadRemoteAudio(
                        new RemoteStream(stream, reopen),
                        MimeForRemoteFile(ext),
                        System.IO.Path.GetFileNameWithoutExtension(file.Name),
                        autoPlay: true,
                        id3StreamFactory: reopen);
                }
                else
                {
                    MediaPreview.LoadRemoteStream(new RemoteStream(stream, reopen), MimeForRemoteFile(ext));
                    MediaPreview.TogglePlayPause();
                }
            }
            catch (Exception ex)
            {
                Log.Err("OpenPreviewNetworkTrack: {Ex}", ex);
            }
        }

        private void NavigatePreviewVideoTrackNetwork(int direction)
        {
            var current = _navigator.Current;
            if (current == null || string.IsNullOrEmpty(_previewNetworkPath)) return;

            var videoFiles = current.Entries
                .Where(e => !e.IsDirectory && e.IsNetwork &&
                    FilePreviewService.IsVideoFile(System.IO.Path.GetExtension(e.Name)))
                .ToList();

            if (videoFiles.Count == 0)
            {
                Log.Warn("NavigatePreviewVideoTrackNetwork: no network video files in current list");
                return;
            }

            int currentIdx = videoFiles.FindIndex(e =>
                string.Equals(e.NetworkPath, _previewNetworkPath, StringComparison.OrdinalIgnoreCase));
            if (currentIdx < 0)
            {
                Log.Warn("NavigatePreviewVideoTrackNetwork: current {Path} not in network list — aborting", _previewNetworkPath);
                return;
            }

            int nextIdx = currentIdx + direction;
            if (nextIdx < 0) nextIdx = videoFiles.Count - 1;
            if (nextIdx >= videoFiles.Count) nextIdx = 0;

            var nextFile = videoFiles[nextIdx];
            Log.Info("NavigatePreviewVideoTrackNetwork: {Direction} to {Path}", direction > 0 ? "next" : "prev", nextFile.NetworkPath);

            int mainIdx = current.Entries.IndexOf(nextFile);
            if (mainIdx >= 0)
            {
                _updating = true;
                CurrentList.SelectedIndex = mainIdx;
                _updating = false;
                int totalCount = current.Entries.Count;
                FooterItemCount.Text = totalCount > 0 ? $"{mainIdx + 1}/{totalCount}" : "";
            }

            _previewNetworkPath = nextFile.NetworkPath;
            _ = OpenPreviewVideoNetworkTrackAsync(nextFile);
        }

        private async Task OpenPreviewVideoNetworkTrackAsync(FileEntry file)
        {
            try
            {
                System.IO.Stream stream = await _navigator.OpenNetworkStreamAsync(
                    _previewNetworkLocationId, _previewNetworkShare, file.NetworkPath);
                if (stream == null) return;

                Func<System.IO.Stream> reopen = () =>
                    Task.Run(() => _navigator.OpenNetworkStreamAsync(
                        _previewNetworkLocationId, _previewNetworkShare, file.NetworkPath))
                        .GetAwaiter().GetResult();

                MediaPreview.LoadRemoteStream(
                    new RemoteStream(stream, reopen),
                    MimeForRemoteFile(System.IO.Path.GetExtension(file.Name)));
                MediaPreview.TogglePlayPause();
            }
            catch (Exception ex)
            {
                Log.Err("OpenPreviewVideoNetworkTrack: {Ex}", ex);
            }
        }

        /// <summary>LB/RB in the fullscreen audio player while a remote track streams.</summary>
        private void NavigateAudioTrackNetwork(int direction)
        {
            var current = _navigator.Current;
            if (current == null || string.IsNullOrEmpty(_fsNetworkPath)) return;

            int gen = ++_fsGeneration;
            ++_prefetchGeneration;

            var audioFiles = current.Entries
                .Where(e => !e.IsDirectory && e.IsNetwork &&
                    (FilePreviewService.IsAudioFile(System.IO.Path.GetExtension(e.Name))
                     || FilePreviewService.IsChiptuneFile(System.IO.Path.GetExtension(e.Name))))
                .ToList();

            if (audioFiles.Count == 0)
            {
                Log.Warn("NavigateAudioTrackNetwork: no network audio files in current list");
                return;
            }

            int currentIdx = audioFiles.FindIndex(e =>
                string.Equals(e.NetworkPath, _fsNetworkPath, StringComparison.OrdinalIgnoreCase));
            if (currentIdx < 0)
            {
                Log.Warn("NavigateAudioTrackNetwork: current {Path} not in network list — aborting", _fsNetworkPath);
                return;
            }

            int nextIdx = currentIdx + direction;
            if (nextIdx < 0) nextIdx = audioFiles.Count - 1;
            if (nextIdx >= audioFiles.Count) nextIdx = 0;

            var nextFile = audioFiles[nextIdx];
            Log.Info("NavigateAudioTrackNetwork: {Direction} to {Path}", direction > 0 ? "next" : "prev", nextFile.NetworkPath);
            _fsAudioEnded = false;

            int mainIdx = current.Entries.IndexOf(nextFile);
            if (mainIdx >= 0)
            {
                _updating = true;
                CurrentList.SelectedIndex = mainIdx;
                _updating = false;
            }

            ShowAudioOsd(direction > 0 ? "Next" : "Prev",
                direction > 0 ? "ms-appx:///Assets/Views/MillerColumnsPage/osd/osd-next-48.png" : "ms-appx:///Assets/Views/MillerColumnsPage/osd/osd-prev-48.png", 1200);

            _ = OpenFullscreenNetworkTrackAsync(gen, nextFile);
        }

        private async Task OpenFullscreenNetworkTrackAsync(int gen, FileEntry file)
        {
            try
            {
                string ext = System.IO.Path.GetExtension(file.Name);
                _fsNetworkPath = file.NetworkPath;

                if (RetroAudioPlayer.IsChiptuneFile(ext))
                {
                    // Chiptune needs a local decoded WAV — cache the remote file first.
                    string tempPath = await CacheRemoteFileAsync(_fsNetworkLocationId, _fsNetworkShare, file.NetworkPath, file.Name);
                    if (tempPath == null)
                    {
                        Log.Warn("OpenFullscreenNetworkTrack: chiptune cache failed for {Name}", file.Name);
                        return;
                    }
                    if (gen != _fsGeneration) return;
                    OpenAudioFullscreen(tempPath, TimeSpan.Zero, Math.Max(0, file.ChiptuneTrackIndex));
                    // OpenAudioFullscreen resets the network flag — restore it so
                    // LB/RB keeps navigating the network list for remote chiptunes.
                    _fsIsNetwork = true;
                    _fsNetworkPath = file.NetworkPath;
                    return;
                }

                System.IO.Stream stream = await _navigator.OpenNetworkStreamAsync(
                    _fsNetworkLocationId, _fsNetworkShare, file.NetworkPath);
                if (stream == null) return;
                if (gen != _fsGeneration) return;

                Func<System.IO.Stream> reopen = () =>
                    Task.Run(() => _navigator.OpenNetworkStreamAsync(
                        _fsNetworkLocationId, _fsNetworkShare, file.NetworkPath))
                        .GetAwaiter().GetResult();

                await OpenRemoteAudioFullscreenAsync(
                    System.IO.Path.GetFileNameWithoutExtension(file.Name),
                    new RemoteStream(stream, reopen),
                    MimeForRemoteFile(ext),
                    _fsNetworkLocationId, _fsNetworkShare, file.NetworkPath);
            }
            catch (Exception ex)
            {
                Log.Err("OpenFullscreenNetworkTrack: {Ex}", ex);
            }
        }

        /// <summary>LB/RB in the fullscreen video player while a remote stream plays.</summary>
        private void NavigateFullscreenVideoNetwork(int direction)
        {
            var current = _navigator.Current;
            if (current == null || string.IsNullOrEmpty(_fsNetworkPath)) return;

            var videoFiles = current.Entries
                .Where(e => !e.IsDirectory && e.IsNetwork &&
                    FilePreviewService.IsVideoFile(System.IO.Path.GetExtension(e.Name)))
                .ToList();

            if (videoFiles.Count == 0) { CloseVideoFullScreen(); return; }

            int currentIdx = videoFiles.FindIndex(e =>
                string.Equals(e.NetworkPath, _fsNetworkPath, StringComparison.OrdinalIgnoreCase));
            if (currentIdx < 0)
            {
                Log.Warn("NavigateFullscreenVideoNetwork: current {Path} not in network list — aborting", _fsNetworkPath);
                return;
            }

            int nextIdx = currentIdx + direction;
            if (nextIdx < 0) nextIdx = videoFiles.Count - 1;
            if (nextIdx >= videoFiles.Count) nextIdx = 0;

            var nextFile = videoFiles[nextIdx];
            Log.Info("NavigateFullscreenVideoNetwork: {Direction} to {Path}", direction > 0 ? "next" : "prev", nextFile.NetworkPath);
            _fsNetworkPath = nextFile.NetworkPath;

            int mainIdx = current.Entries.IndexOf(nextFile);
            if (mainIdx >= 0)
            {
                _updating = true;
                CurrentList.SelectedIndex = mainIdx;
                _updating = false;
            }

            _ = OpenFullscreenVideoNetworkTrackAsync(nextFile);
        }

        private async Task OpenFullscreenVideoNetworkTrackAsync(FileEntry file)
        {
            try
            {
                System.IO.Stream stream = await _navigator.OpenNetworkStreamAsync(
                    _fsNetworkLocationId, _fsNetworkShare, file.NetworkPath);
                if (stream == null) return;

                Func<System.IO.Stream> reopen = () =>
                    Task.Run(() => _navigator.OpenNetworkStreamAsync(
                        _fsNetworkLocationId, _fsNetworkShare, file.NetworkPath))
                        .GetAwaiter().GetResult();

                await ShowMediaFullscreenStreamAsync(
                    new RemoteStream(stream, reopen),
                    MimeForRemoteFile(System.IO.Path.GetExtension(file.Name)),
                    System.IO.Path.GetFileNameWithoutExtension(file.Name),
                    _fsNetworkLocationId, _fsNetworkShare, file.NetworkPath);
            }
            catch (Exception ex)
            {
                Log.Err("OpenFullscreenVideoNetworkTrack: {Ex}", ex);
            }
        }

        public void NavigateAudioTrack(int direction)
        {
            if (string.IsNullOrEmpty(_audioFullscreenPath) || _navigator.Current == null) return;

            // Remote (SMB) fullscreen track: navigate the network list instead of local paths.
            if (_fsIsNetwork)
            {
                NavigateAudioTrackNetwork(direction);
                return;
            }

            int gen = ++_fsGeneration;
            ++_prefetchGeneration; // abort in-flight next-track prefetches

            // Multi-track chiptune: advance subsongs within the same source.
            if (!string.IsNullOrEmpty(_fsChiptuneSource) && _fsChiptuneTrackCount > 1 &&
                string.Equals(_fsChiptuneSource, _audioFullscreenPath, StringComparison.OrdinalIgnoreCase))
            {
                int nextTrack = (_fsChiptuneTrack + direction + _fsChiptuneTrackCount) % _fsChiptuneTrackCount;
                Log.Info("NavigateAudioTrack: chiptune subsong {Dir} -> {Next}/{Count} of {Path}",
                    direction > 0 ? "next" : "prev", nextTrack + 1, _fsChiptuneTrackCount, _fsChiptuneSource);
                _fsChiptuneTrack = nextTrack;
                _fsAudioEnded = false;
                UpdateFsSelectionToChiptuneTrack(nextTrack);
                ShowAudioOsd(direction > 0 ? "Next" : "Prev",
                    direction > 0 ? "ms-appx:///Assets/Views/MillerColumnsPage/osd/osd-next-48.png" : "ms-appx:///Assets/Views/MillerColumnsPage/osd/osd-prev-48.png", 1200);
                SetFsLoading(true);
                _ = LoadFsChiptuneAsync(gen, _fsChiptuneSource, nextTrack);
                PrefetchNextChiptuneTrack();
                return;
            }

            // File-list navigation. Includes chiptune track entries (drilled-in
            // virtual list) and plain audio/chiptune files.
            var audioFiles = _navigator.Current.Entries
                .Where(e => !e.IsDirectory && (e.IsChiptune
                    || FilePreviewService.IsAudioFile(System.IO.Path.GetExtension(e.Name))
                    || FilePreviewService.IsChiptuneFile(System.IO.Path.GetExtension(e.Name))))
                .ToList();

            if (audioFiles.Count == 0)
            {
                Log.Warn("NavigateAudioTrack: no audio files in current list ({Total} entries total)", _navigator.Current.Entries.Count);
                return;
            }

            int currentIdx = audioFiles.FindIndex(e =>
                string.Equals(e.FullPath, _audioFullscreenPath, StringComparison.OrdinalIgnoreCase));
            if (currentIdx < 0)
            {
                Log.Warn("NavigateAudioTrack: current {Path} not in audio list — aborting", _audioFullscreenPath);
                return;
            }

            int nextIdx = currentIdx + direction;
            if (nextIdx < 0) nextIdx = audioFiles.Count - 1;
            if (nextIdx >= audioFiles.Count) nextIdx = 0;

            var nextFile = audioFiles[nextIdx];
            _audioFullscreenPath = nextFile.FullPath;
            _fsAudioEnded = false;

            bool isChip = nextFile.IsChiptune || RetroAudioPlayer.IsChiptuneFile(nextFile.FullPath);
            string chipSource = nextFile.ChiptuneSourcePath ?? nextFile.FullPath;
            int chipTrack = Math.Max(0, nextFile.ChiptuneTrackIndex);

            // Free the native session lock held by the render of the fullscreen track
            // being left, so the new decode does not wait for the orphaned render.
            if (_fsChiptuneSource != null &&
                !string.Equals(_fsChiptuneSource, chipSource, StringComparison.OrdinalIgnoreCase))
            {
                RetroAudioPlayer.CancelChiptuneRender(_fsChiptuneSource, _fsChiptuneTrack);
            }

            if (isChip)
            {
                _fsChiptuneSource = chipSource;
                _fsChiptuneTrack = chipTrack;
                _fsChiptuneTrackCount = 1; // updated by the decode probe
            }
            else
            {
                _fsChiptuneSource = null;
                _fsChiptuneTrack = 0;
                _fsChiptuneTrackCount = 1;
            }

            // Show placeholder immediately
            string displayName = nextFile.IsChiptune ? nextFile.Name : System.IO.Path.GetFileNameWithoutExtension(nextFile.FullPath);
            FsTitleText.Text = displayName;
            FsArtistText.Text = "";
            FsArtistText.Visibility = Visibility.Collapsed;
            FsAlbumText.Text = "";
            FsAlbumText.Visibility = Visibility.Collapsed;
            FsAlbumArtBorder.Visibility = Visibility.Collapsed;
            FsDefaultArtPanel.Visibility = Visibility.Visible;
            _fsHasAlbumArt = false;

            // Load next track. Chiptunes are decoded to a cached WAV first —
            // AudioGraph cannot read .spc/.nsf/.psf etc directly.
            if (isChip)
            {
                SetFsLoading(true);
                _ = LoadFsChiptuneAsync(gen, chipSource, chipTrack);
                PrefetchNextChiptuneTrack();
            }
            else if (AudioLevelService.Instance.IsFileLoaded)
            {
                Log.Info("NavigateAudioTrack: reusing AudioLevelService, SwapSource to {Path}", nextFile.FullPath);
                SetFsLoading(true);
                _ = AudioLevelService.Instance.SwapSourceAsync(nextFile.FullPath);
            }
            else
            {
                StopFsAudioAnalysis();
                AudioLevelService.Instance.MediaOpened += OnFsAudioOpened;
                AudioLevelService.Instance.MediaEnded += OnFsAudioEnded;
                AudioLevelService.Instance.MediaFailed += OnFsAudioFailed;
#if AUDIO_ANALYSIS
                FsVuMeter.AttachService(AudioLevelService.Instance);
#endif
                SetFsLoading(true);
                _ = AudioLevelService.Instance.LoadAndPlay(nextFile.FullPath);
            }

            // Re-apply current visualizer mode with new audio service
            if (_fsVisualizerMode != AudioFullscreenMode.Default)
                ApplyAudioVisualizerMode();

            FsPlayPauseIcon.Glyph = "\uE769";
            ShowAudioOsd(direction > 0 ? "Next" : "Prev",
                direction > 0 ? "ms-appx:///Assets/Views/MillerColumnsPage/osd/osd-next-48.png" : "ms-appx:///Assets/Views/MillerColumnsPage/osd/osd-prev-48.png", 1200);

            _ = LoadAudioFullscreenMetadataAsync(nextFile.FullPath);

            // Update selection in main list
            int mainIdx = _navigator.Current.Entries.IndexOf(nextFile);
            if (mainIdx >= 0)
            {
                _updating = true;
                CurrentList.SelectedIndex = mainIdx;
                _updating = false;
            }
        }

        /// <summary>
        /// Prefetch the next chiptune track's WAV into the cache while the current
        /// track plays, so next/prev becomes near-instant (the decode is the slow
        /// part for PSF/USF/SPC etc). Guarded by _prefetchGeneration — a navigation
        /// or player close aborts the pending prefetch.
        /// </summary>
        private void PrefetchNextChiptuneTrack()
        {
            try
            {
                if (_audioFullscreenPath == null || _navigator.Current == null) return;

                string nextSource;
                int nextTrack;

                // Multi-track chiptune: prefetch the next subsong within the same source.
                if (!string.IsNullOrEmpty(_fsChiptuneSource) && _fsChiptuneTrackCount > 1 &&
                    string.Equals(_fsChiptuneSource, _audioFullscreenPath, StringComparison.OrdinalIgnoreCase))
                {
                    nextSource = _fsChiptuneSource;
                    nextTrack = (_fsChiptuneTrack + 1) % _fsChiptuneTrackCount;
                }
                else
                {
                    var audioFiles = _navigator.Current.Entries
                        .Where(e => !e.IsDirectory && (e.IsChiptune
                            || FilePreviewService.IsAudioFile(System.IO.Path.GetExtension(e.Name))
                            || FilePreviewService.IsChiptuneFile(System.IO.Path.GetExtension(e.Name))))
                        .ToList();
                    if (audioFiles.Count == 0) return;

                    int currentIdx = audioFiles.FindIndex(e =>
                        string.Equals(e.FullPath, _audioFullscreenPath, StringComparison.OrdinalIgnoreCase));
                    if (currentIdx < 0) return;

                    var nextFile = audioFiles[(currentIdx + 1) % audioFiles.Count];
                    if (!nextFile.IsChiptune && !RetroAudioPlayer.IsChiptuneFile(nextFile.FullPath))
                        return; // only chiptune decodes benefit from a WAV prefetch
                    nextSource = nextFile.ChiptuneSourcePath ?? nextFile.FullPath;
                    nextTrack = Math.Max(0, nextFile.ChiptuneTrackIndex);
                }

                WarmChiptuneCache(nextSource, nextTrack);
            }
            catch (Exception ex)
            {
                Log.Dbg("PrefetchNextChiptuneTrack: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Decode a chiptune source/track to its cached WAV in the background. No-op
        /// for already-cached renders. Uses RetroAudioPlayer directly (not the
        /// MediaPreview state), so it is safe to run while the current track plays.
        /// </summary>
        private void WarmChiptuneCache(string source, int track)
        {
            if (RetroAudioPlayer.IsArchiveEntryPath(source)) return; // lib resolution needs a real path
            int gen = ++_prefetchGeneration;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(1500);
                    if (gen != _prefetchGeneration) return;

                    var handle = RetroAudioPlayer.StartChiptuneStream(
                        source, null, System.IO.Path.GetExtension(source), track);
                    string wav = await RetroAudioPlayer.WaitForStreamableWavAsync(handle);
                    if (gen != _prefetchGeneration)
                    {
                        Log.Dbg("Chiptune prefetch: stale result for {Path} track {Track} — discarded", source, track);
                        return;
                    }
                    Log.Dbg("Chiptune prefetch: streamable for {Path} track {Track}: {Wav}",
                        source, track, wav ?? "(null)");
                }
                catch (Exception ex)
                {
                    Log.Dbg("Chiptune prefetch: skipped {Path} track {Track}: {Error}", source, track, ex.Message);
                }
            });
        }

        /// <summary>
        /// Decode a chiptune subsong to WAV and play it in the fullscreen audio
        /// player. Guarded by the fullscreen generation so a decode superseded by
        /// another navigation is discarded instead of hijacking playback.
        /// </summary>
        private async Task LoadFsChiptuneAsync(int gen, string source, int track)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            string wav = await MediaPreview.GetChiptuneStreamingWavPathAsync(source, track);
            if (gen != _fsGeneration)
            {
                Log.Dbg("NavigateAudioTrack: stale chiptune decode (gen {Gen} != {Current}) — aborting", gen, _fsGeneration);
                SetFsLoading(false);
                return;
            }
            if (string.IsNullOrEmpty(wav))
            {
                Log.Warn("NavigateAudioTrack: chiptune decode failed for {Path}", source);
                FsPlayPauseIcon.Glyph = "\uE768";
                SetFsLoading(false);
                return;
            }
            Log.Info("CHIPTUNE-NAV: decode of {Path} track={Track} took {Elapsed}ms", source, track, sw.ElapsedMilliseconds);

            _fsChiptuneSource = source;
            _fsChiptuneTrack = track;
            _fsChiptuneTrackCount = MediaPreview.CurrentChiptuneTrackCount;
            if (_fsChiptuneTrackCount < 1) _fsChiptuneTrackCount = 1;

            string title = MediaPreview.CurrentChiptuneTitle;
            if (!string.IsNullOrEmpty(title))
                FsTitleText.Text = title;

            if (AudioLevelService.Instance.IsFileLoaded)
            {
                Log.Info("NavigateAudioTrack: reusing AudioLevelService, SwapSource to chiptune WAV {Path}", wav);
                await AudioLevelService.Instance.SwapSourceAsync(wav, forceStream: true);
            }
            else
            {
                StopFsAudioAnalysis();
                AudioLevelService.Instance.MediaOpened += OnFsAudioOpened;
                AudioLevelService.Instance.MediaEnded += OnFsAudioEnded;
                AudioLevelService.Instance.MediaFailed += OnFsAudioFailed;
#if AUDIO_ANALYSIS
                FsVuMeter.AttachService(AudioLevelService.Instance);
#endif
                await AudioLevelService.Instance.LoadAndPlay(wav, forceStream: true);
            }

            if (gen != _fsGeneration)
            {
                Log.Dbg("NavigateAudioTrack: playback superseded — aborting");
                SetFsLoading(false);
                return;
            }
            FsPlayPauseIcon.Glyph = "\uE769";
        }

        private void UpdateFsSelectionToChiptuneTrack(int track)
        {
            if (_navigator.Current == null) return;
            int mainIdx = _navigator.Current.Entries.FindIndex(e =>
                e.IsChiptune && e.ChiptuneTrackIndex == track &&
                string.Equals(e.ChiptuneSourcePath ?? e.FullPath, _fsChiptuneSource, StringComparison.OrdinalIgnoreCase));
            if (mainIdx >= 0)
            {
                _updating = true;
                CurrentList.SelectedIndex = mainIdx;
                _updating = false;
            }
        }

        private void StopFsAudioAnalysis()
        {
#if AUDIO_ANALYSIS
            FsVuMeter.DetachService();
#endif
            AudioLevelService.Instance.MediaOpened -= OnFsAudioOpened;
            AudioLevelService.Instance.MediaEnded -= OnFsAudioEnded;
            AudioLevelService.Instance.MediaFailed -= OnFsAudioFailed;
            AudioLevelService.Instance.Stop();
        }

        private void SetFsLoading(bool value)
        {
            FsLoadingSpinner.IsActive = value;
            FsLoadingSpinner.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            // While a track loads, the seekbar reads indeterminate instead of
            // freezing on the stale position of the previous track.
            FsAudioProgress.IsIndeterminate = value;
        }

        private async void OnFsAudioOpened(object sender, EventArgs e)
        {
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                FsPlayPauseIcon.Glyph = "\uE769";
                SetFsLoading(false);
                Log.Info("FsAudio: opened");
            });
        }

        private async void OnFsAudioEnded(object sender, EventArgs e)
        {
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                if (FsLoadingSpinner.Visibility == Visibility.Visible)
                {
                    // The old track hit EOF because its render was cancelled when we
                    // navigated; a new track is about to swap in. Ignoring prevents
                    // the premature double-advance.
                    Log.Dbg("FsAudio: ended while a load is pending — ignoring");
                    return;
                }
                if (_fsAudioEnded)
                {
                    Log.Dbg("FsAudio: ended already handled — skipping");
                    return;
                }
                _fsAudioEnded = true;
                Log.Info("FsAudio: ended — auto-advancing");
                NavigateAudioTrack(1);
            });
        }

        private async void OnFsAudioFailed(object sender, EventArgs e)
        {
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                Log.Warn("FsAudio: failed");
                FsPlayPauseIcon.Glyph = "\uE768";
                SetFsLoading(false);
            });
        }
    }

    public sealed class VisualizerPickerItem
    {
        public AudioFullscreenMode Mode { get; }
        public string Label { get; }
        public bool IsCurrent { get; }

        public VisualizerPickerItem(AudioFullscreenMode mode, string label, bool isCurrent)
        {
            Mode = mode;
            Label = label;
            IsCurrent = isCurrent;
        }
    }
}
