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
        private async Task UpdatePreviewColumnAsync()
        {
            HideAllPreviewPanels();

            // If the incoming preview is NOT audio/video media, the inline player
            // must stop — otherwise the AudioGraph keeps playing with no UI to stop it
            // (music continues in the "background" after navigating to a folder/file).
            bool isMediaPreview = _navigator.Preview != null && _navigator.Preview.IsFilePreview &&
                (_navigator.Preview.PreviewType == FilePreviewType.Audio ||
                 _navigator.Preview.PreviewType == FilePreviewType.Video);

            if (!isMediaPreview && MediaPreview.IsPlayerActive)
            {
                Log.Info("UpdatePreviewColumn: non-media preview — stopping inline player");
                _mediaLoadTimer.Stop();
                _pendingMediaPath = null;
                MediaPreview.StopPlayer();
                _isMediaPlayerActive = false;
                UpdateMediaPlayerFocusUI();
                UpdateDisplayRequest();
            }

            // At root: QuickRefPanel is visible, skip preview update
            if (_navigator.Parent == null)
            {
                PreviewHeader.Text = "";
                PreviewStatus.Text = "";
                return;
            }

            // Favorites column (root level): show the how-to guide instead of a
            // folder preview. After drilling into an actual favorite, IsFavorite is
            // false and the normal preview takes over.
            if (_navigator.Current?.IsFavorite == true)
            {
                ShowFavoritesGuide();
                return;
            }

            if (_navigator.Preview == null)
            {
                PreviewHeader.Text = "";
                PreviewStatus.Text = "";
                Log.Verb("UpdatePreviewColumn: preview is null");
                return;
            }

            Log.Verb("UpdatePreviewColumn: label={Label}, isFile={IsFile}, type={Type}",
                _navigator.Preview.Label, _navigator.Preview.IsFilePreview, _navigator.Preview.PreviewType);

            PreviewHeader.Text = _navigator.Preview.Label ?? "";

            if (!_navigator.Preview.IsFilePreview)
            {
                BindList(PreviewList, _navigator.Preview);
                PreviewStatus.Text = Formatting.FormatCount(_navigator.Preview.Entries);
                PreviewList.Visibility = Visibility.Visible;
            }
            else
            {
                switch (_navigator.Preview.PreviewType)
                {
                    case FilePreviewType.Text:
                        PreviewStatus.Text = _navigator.Preview.PreviewIsTruncated
                            ? $"{_navigator.Preview.PreviewFileType} (truncated)"
                            : _navigator.Preview.PreviewFileType;

                        string ext = Path.GetExtension(_navigator.Preview.Label ?? "");
                        bool isPlainText = PlainTextExtensions.Contains(ext);
                        Log.Dbg("UpdatePreviewColumn: ext={Ext} isPlainText={IsPlainText} contentLen={Len}",
                            ext, isPlainText, _navigator.Preview.PreviewTextContent?.Length ?? 0);

                        if (isPlainText)
                        {
                            PreviewTextBlock.Text = _navigator.Preview.PreviewTextContent ?? "";
                            PreviewTextScroll.Visibility = Visibility.Visible;
                        }
                        else if (FilePreviewService.IsSvgFile(ext))
                        {
                            string svgHtml = HighlightRenderer.BuildSvgHtml(
                                _navigator.Preview.PreviewTextContent ?? "");
                            _ = LoadHighlightHtml(svgHtml);
                            PreviewCodeView.Visibility = Visibility.Visible;
                        }
                        else
                        {
                            string html = await BuildHighlightHtmlAsync(
                                _navigator.Preview.PreviewTextContent ?? "", ext);
                            _ = LoadHighlightHtml(html);
                            PreviewCodeView.Visibility = Visibility.Visible;
                        }
                        break;

                    case FilePreviewType.Image:
                        PreviewImage.Source = _navigator.Preview.PreviewImageSource;
                        int pw = _navigator.Preview.PreviewPixelWidth;
                        int ph = _navigator.Preview.PreviewPixelHeight;
                        int smallThreshold = 256;
                        int maxScale = 4;
                        if (pw > 0 && ph > 0 && (pw < smallThreshold || ph < smallThreshold))
                        {
                            PreviewImage.MaxWidth = Math.Min(pw * maxScale, 1024);
                            PreviewImage.MaxHeight = Math.Min(ph * maxScale, 1024);
                        }
                        else
                        {
                            PreviewImage.MaxWidth = double.PositiveInfinity;
                            PreviewImage.MaxHeight = double.PositiveInfinity;
                        }
                        PreviewStatus.Text = _navigator.Preview.PreviewFileType;
                        PreviewImagePanel.Visibility = Visibility.Visible;
                        break;

                    case FilePreviewType.Pdf:
                        PreviewImage.Source = _navigator.Preview.PreviewImageSource;
                        int pdfPw = _navigator.Preview.PreviewPixelWidth;
                        int pdfPh = _navigator.Preview.PreviewPixelHeight;
                        if (pdfPw > 0 && pdfPh > 0)
                        {
                            PreviewImage.MaxWidth = double.PositiveInfinity;
                            PreviewImage.MaxHeight = double.PositiveInfinity;
                        }
                        int pageCount = _navigator.Preview.PreviewPdfPageCount;
                        PreviewStatus.Text = pageCount > 1
                            ? $"{_navigator.Preview.PreviewFileType} — 1/{pageCount} pages"
                            : _navigator.Preview.PreviewFileType;
                        PreviewImagePanel.Visibility = Visibility.Visible;
                        break;

                    case FilePreviewType.Audio:
                        string audioPath = _navigator.Preview.PreviewFilePath;
                        Log.Dbg("UpdatePreviewColumn: media type={Type} path={Path}", _navigator.Preview.PreviewType, audioPath);

                        if (_navigator.Preview.PreviewChiptuneTrack >= 0)
                        {
                            // Chiptune subsong selected from a drilled-in track list:
                            // decode that specific track from the source.
                            PreviewStatus.Text = _navigator.Preview.PreviewFileType;
                            PreviewMediaPanel.Visibility = Visibility.Visible;
                            MediaPreview.LoadChiptuneTrack(
                                _navigator.Preview.PreviewChiptuneSource,
                                _navigator.Preview.PreviewChiptuneTrack);
                            break;
                        }

                        PreviewStatus.Text = _navigator.Preview.PreviewFileType;
                        PreviewMediaPanel.Visibility = Visibility.Visible;
                        MediaPreview.ShowPlaceholder(audioPath);
                        _pendingMediaPath = audioPath;
                        _mediaLoadTimer.Stop();
                        _mediaLoadTimer.Start();
                        break;

                    case FilePreviewType.Video:
                        string videoPath = _navigator.Preview.PreviewFilePath;
                        Log.Dbg("UpdatePreviewColumn: media type={Type} path={Path}", _navigator.Preview.PreviewType, videoPath);
                        PreviewStatus.Text = _navigator.Preview.PreviewFileType;
                        PreviewMediaPanel.Visibility = Visibility.Visible;
                        _pendingMediaPath = videoPath;
                        _mediaLoadTimer.Stop();
                        _mediaLoadTimer.Start();
                        break;

                    case FilePreviewType.Rom:
                        string romName = _navigator.Preview.PreviewTextContent ?? "";
                        string romSystem = _navigator.Preview.PreviewRomSystem ?? "ROM";
                        string romFileType = _navigator.Preview.PreviewFileType ?? "";
                        string romIcon = _navigator.Preview.PreviewRomIconPath ?? "";

                        RomTitleText.Text = romName;

                        // System icon
                        if (!string.IsNullOrEmpty(romIcon))
                        {
                            RomSystemIcon.Source = new Windows.UI.Xaml.Media.Imaging.BitmapImage(
                                new Uri(romIcon));
                        }

                        // Reset cover art and gamelist UI state
                        RomCoverImage.Visibility = Visibility.Collapsed;
                        RomMetaLine2.Visibility = Visibility.Collapsed;
                        RomMetaLine3.Visibility = Visibility.Collapsed;
                        RomDescriptionText.Visibility = Visibility.Collapsed;
                        RomDescSeparator.Visibility = Visibility.Collapsed;

                        // Gamelist enrichment
                        bool hasGamelist = _navigator.Preview.PreviewHasGamelistData;
                        if (hasGamelist)
                        {
                            string genre = _navigator.Preview.PreviewRomGenre ?? "";
                            int players = _navigator.Preview.PreviewRomPlayers;
                            string metaLine1 = romSystem;
                            if (!string.IsNullOrEmpty(genre))
                                metaLine1 += $" — {genre}";
                            if (players > 0)
                                metaLine1 += $", {players} player{(players == 1 ? "" : "s")}";
                            RomMetaLine1.Text = metaLine1;

                            string dev = _navigator.Preview.PreviewRomDeveloper ?? "";
                            string pub = _navigator.Preview.PreviewRomPublisher ?? "";
                            if (!string.IsNullOrEmpty(dev) || !string.IsNullOrEmpty(pub))
                            {
                                string devPub = "";
                                if (!string.IsNullOrEmpty(dev)) devPub = dev;
                                if (!string.IsNullOrEmpty(pub))
                                {
                                    if (devPub.Length > 0) devPub += " · ";
                                    devPub += pub;
                                }
                                RomMetaLine2.Text = devPub;
                                RomMetaLine2.Visibility = Visibility.Visible;
                            }

                            float rating = _navigator.Preview.PreviewRomRating;
                            int year = _navigator.Preview.PreviewRomReleaseYear;
                            if (rating > 0 || year > 0)
                            {
                                string ratingYear = "";
                                if (rating > 0)
                                {
                                    int stars = (int)Math.Round(rating * 5);
                                    ratingYear = new string('★', Math.Min(stars, 5)) +
                                                 new string('☆', Math.Max(5 - stars, 0));
                                }
                                if (year > 0)
                                {
                                    if (ratingYear.Length > 0) ratingYear += "  ";
                                    ratingYear += year.ToString();
                                }
                                RomMetaLine3.Text = ratingYear;
                                RomMetaLine3.Visibility = Visibility.Visible;
                            }

                            string desc = _navigator.Preview.PreviewRomDescription ?? "";
                            if (!string.IsNullOrEmpty(desc))
                            {
                                RomDescriptionText.Text = desc;
                                RomDescriptionText.Visibility = Visibility.Visible;
                                RomDescSeparator.Visibility = Visibility.Visible;
                            }
                        }
                        else
                        {
                            // No gamelist: simple "System ROM" line
                            RomMetaLine1.Text = $"{romSystem} ROM";
                        }

                        RomSizeText.Text = Formatting.FormatSize(_navigator.Preview.PreviewFileSize);
                        PreviewRomPanel.Visibility = Visibility.Visible;
                        PreviewStatus.Text = hasGamelist
                            ? $"{romSystem} — {romFileType}"
                            : $"{romSystem} — {romFileType}";

                        // Cover art: gamelist local → LibRetro → system icon (stays)
                        string localCover = _navigator.Preview.PreviewRomCoverLocalPath;
                        if (!string.IsNullOrEmpty(localCover) && DirectoryScanner.FileExists(localCover))
                        {
                            _ = LoadRomCoverFromLocalFileAsync(localCover);
                        }
                        else
                        {
                            // No local cover: try LibRetro fetch
                            _ = FetchRomCoverArtAsync(romSystem, romName);
                        }
                        break;

                    case FilePreviewType.Error:
                        {
                            string msg = _navigator.Preview.PreviewErrorMessage ?? "Unknown error";
                            bool devOnly = msg.IndexOf("only allowed for developer packages", StringComparison.OrdinalIgnoreCase) >= 0;
                            if (devOnly)
                            {
                                PreviewErrorIcon.Source = new Windows.UI.Xaml.Media.Imaging.BitmapImage(
                                    new Uri("ms-appx:///Assets/Views/MillerColumnsPage/millercolumnspage-access-denied-100.png"));
                                PreviewErrorTitle.Text = "Developer-only app";
                                PreviewErrorText.Text = "X-Files can open the files of developer (homebrew) apps, but not store apps like this one. Try a different app.";
                            }
                            else
                            {
                                PreviewErrorIcon.Source = new Windows.UI.Xaml.Media.Imaging.BitmapImage(
                                    new Uri("ms-appx:///Assets/Views/MillerColumnsPage/millercolumnspage-error-100.png"));
                                PreviewErrorTitle.Text = "Can't access this entry";
                                PreviewErrorText.Text = msg;
                            }
                            PreviewStatus.Text = "";
                            PreviewErrorPanel.Visibility = Visibility.Visible;
                            break;
                        }

                    case FilePreviewType.Unsupported:
                        {
                            string previewPath = _navigator.Preview.PreviewFilePath ?? "";
                            bool isInsideArchive = previewPath.Contains("|");
                            string fileExt = System.IO.Path.GetExtension(previewPath);
                            bool isMedia = FilePreviewService.IsAudioFile(fileExt) || FilePreviewService.IsVideoFile(fileExt)
                                || FilePreviewService.IsChiptuneFile(fileExt);

                            if (isInsideArchive && isMedia)
                            {
                                PreviewArchiveMediaPanel.Visibility = Visibility.Visible;
                            }
                            else
                            {
                                string iconFile = EntryViewModel.GetLargeFileIcon(previewPath);
                                PreviewUnsupportedIcon.Source = new Windows.UI.Xaml.Media.Imaging.BitmapImage(
                                    new Uri($"ms-appx:///Assets/FileTypes/{iconFile}"));

                                string fileName = _navigator.Preview.Label ?? Path.GetFileName(previewPath);
                                PreviewUnsupportedFileName.Text = string.IsNullOrEmpty(fileName) ? "No Preview" : fileName;
                                PreviewUnsupportedType.Text = _navigator.Preview.PreviewFileType ?? "";
                                PreviewUnsupportedSize.Text = Formatting.FormatSize(_navigator.Preview.PreviewFileSize);
                                PreviewStatus.Text = "";
                                PreviewUnsupportedPanel.Visibility = Visibility.Visible;
                            }
                        }
                        break;

                    default:
                        PreviewStatus.Text = _navigator.Preview.PreviewFileType ?? "";
                        break;
                }
            }
        }

        private void HideAllPreviewPanels()
        {
            _coverArtCts?.Cancel();
            PreviewList.Visibility = Visibility.Collapsed;
            PreviewTextScroll.Visibility = Visibility.Collapsed;
            PreviewCodeView.Visibility = Visibility.Collapsed;
            PreviewImagePanel.Visibility = Visibility.Collapsed;
            PreviewMediaPanel.Visibility = Visibility.Collapsed;
            _mediaLoadTimer.Stop();
            MediaPreview.Stop();
            PreviewRomPanel.Visibility = Visibility.Collapsed;
            PreviewErrorPanel.Visibility = Visibility.Collapsed;
            PreviewUnsupportedPanel.Visibility = Visibility.Collapsed;
            PreviewArchiveMediaPanel.Visibility = Visibility.Collapsed;
            FavoritesGuidePanel.Visibility = Visibility.Collapsed;
        }

        private void ShowFavoritesGuide()
        {
            HideAllPreviewPanels();
            PreviewHeader.Text = "";
            PreviewStatus.Text = "";
            FavoritesGuidePanel.Visibility = Visibility.Visible;
        }

        private async Task<string> BuildHighlightHtmlAsync(string code, string extension)
        {
            string lang = HighlightRenderer.GetHighlightLang(extension);
            string escaped = HighlightRenderer.HtmlEncode(code);

            await EnsureHighlightAssetsLoadedAsync();

            Log.Dbg("BuildHighlightHtmlAsync: ext={Ext} lang={Lang} cssLen={CssLen} codeLen={CodeLen} jsLen={JsLen}",
                extension, lang, _highlightCss?.Length ?? 0, code?.Length ?? 0, _highlightJs?.Length ?? 0);

            return HighlightRenderer.BuildHighlightHtml(escaped, lang, _highlightCss, _highlightJs);
        }

        private async Task LoadHighlightHtml(string html)
        {
            try
            {
                Log.Dbg("LoadHighlightHtml: NavigateToString ({Len} chars)", html.Length);
                PreviewCodeView.NavigateToString(html);
            }
            catch (Exception ex)
            {
                Log.Err("Failed NavigateToString", ex);
            }
        }

        private static async Task EnsureHighlightAssetsLoadedAsync()
        {
            if (_highlightJs != null && _highlightCss != null) return;

            try
            {
                Log.Dbg("EnsureHighlightAssetsLoadedAsync: loading JS...");
                var jsFile = await StorageFile.GetFileFromApplicationUriAsync(
                    new Uri("ms-appx:///Assets/highlight.min.js"));
                _highlightJs = await FileIO.ReadTextAsync(jsFile);
                Log.Dbg("EnsureHighlightAssetsLoadedAsync: JS loaded, {Len} chars", _highlightJs.Length);

                Log.Dbg("EnsureHighlightAssetsLoadedAsync: loading CSS...");
                var cssFile = await StorageFile.GetFileFromApplicationUriAsync(
                    new Uri("ms-appx:///Assets/highlight-aco.css"));
                _highlightCss = await FileIO.ReadTextAsync(cssFile);
                Log.Dbg("EnsureHighlightAssetsLoadedAsync: CSS loaded, {Len} chars", _highlightCss.Length);
            }
            catch (Exception ex)
            {
                Log.Err("Failed to load highlight.js assets", ex);
                _highlightJs = "";
                _highlightCss = "";
            }
        }

        private void BindList(ListView listView, ColumnState state)
        {
            var vms = state.Entries.Select(e => new EntryViewModel
            {
                Name = e.Name,
                FullPath = e.FullPath,
                IsDirectory = e.IsDirectory,
                IsDrive = e.IsDrive,
                IsArchive = e.IsArchive,
                IsVirtual = e.IsVirtual,
                IsPortal = e.IsPortal,
                PortalKnownFolder = e.PortalKnownFolder,
                PortalPackageFullName = e.PortalPackageFullName,
                PortalPath = e.PortalPath,
                IsSeparator = e.IsSeparator,
                IsFavorite = e.FullPath != null && FileSystem.FavoritesManager.IsFavorite(e.FullPath),
                SizeBytes = e.SizeBytes,
                ArchiveRootPath = e.ArchiveRootPath,
                ArchiveInternalPath = e.ArchiveInternalPath,
                IsChiptune = e.IsChiptune,
                ChiptuneTrackIndex = e.ChiptuneTrackIndex,
                ChiptuneSourcePath = e.ChiptuneSourcePath,
                IsDotDot = (e.Name == "..")
            }).ToList();

            listView.ItemsSource = vms;
        }

        private void BindParentList(ListView listView, ColumnState state, string highlightName)
        {
            var vms = state.Entries.Select(e => new EntryViewModel
            {
                Name = e.Name,
                FullPath = e.FullPath,
                IsDirectory = e.IsDirectory,
                IsDrive = e.IsDrive,
                IsArchive = e.IsArchive,
                IsVirtual = e.IsVirtual,
                IsPortal = e.IsPortal,
                PortalKnownFolder = e.PortalKnownFolder,
                PortalPackageFullName = e.PortalPackageFullName,
                PortalPath = e.PortalPath,
                IsSeparator = e.IsSeparator,
                IsFavorite = e.FullPath != null && FileSystem.FavoritesManager.IsFavorite(e.FullPath),
                SizeBytes = e.SizeBytes,
                ArchiveRootPath = e.ArchiveRootPath,
                ArchiveInternalPath = e.ArchiveInternalPath,
                IsChiptune = e.IsChiptune,
                ChiptuneTrackIndex = e.ChiptuneTrackIndex,
                ChiptuneSourcePath = e.ChiptuneSourcePath,
                IsHighlighted = (highlightName != null && e.Name == highlightName),
                IsDotDot = (e.Name == "..")
            }).ToList();

            listView.ItemsSource = vms;
        }

        private void BindCurrentList(ColumnState state)
        {
            var vms = state.Entries.Select(e => new EntryViewModel
            {
                Name = e.Name,
                FullPath = e.FullPath,
                IsDirectory = e.IsDirectory,
                IsDrive = e.IsDrive,
                IsArchive = e.IsArchive,
                IsVirtual = e.IsVirtual,
                IsPortal = e.IsPortal,
                PortalKnownFolder = e.PortalKnownFolder,
                PortalPackageFullName = e.PortalPackageFullName,
                PortalPath = e.PortalPath,
                IsSeparator = e.IsSeparator,
                IsFavorite = e.FullPath != null && FileSystem.FavoritesManager.IsFavorite(e.FullPath),
                SizeBytes = e.SizeBytes,
                ArchiveRootPath = e.ArchiveRootPath,
                ArchiveInternalPath = e.ArchiveInternalPath,
                IsChiptune = e.IsChiptune,
                ChiptuneTrackIndex = e.ChiptuneTrackIndex,
                ChiptuneSourcePath = e.ChiptuneSourcePath,
                IsDotDot = (e.Name == "..")
            }).ToList();

            SlideColumn(_slideFromRight);

            CurrentList.ItemsSource = vms;

            Log.Dbg("BindCurrentList: state.SelectedIndex={StateIndex}, itemCount={Count}", state.SelectedIndex, vms.Count);
            if (state.SelectedIndex >= 0 && state.SelectedIndex < CurrentList.Items.Count)
                CurrentList.SelectedIndex = state.SelectedIndex;

            // ItemsSource was just set — containers aren't realized yet, so an immediate
            // ScrollIntoView is a no-op. Defer to low priority so the restored selection
            // becomes visible after layout (drill out / search / refresh).
            if (CurrentList.SelectedIndex >= 0)
            {
                _ = Dispatcher.RunAsync(CoreDispatcherPriority.Low, () =>
                {
                    if (CurrentList.SelectedIndex >= 0)
                        CurrentList.ScrollIntoView(CurrentList.Items[CurrentList.SelectedIndex]);
                });
            }

            CurrentList.Focus(FocusState.Programmatic);
        }

        private void SlideColumn(bool fromRight)
        {
            double offset = 80;
            double startX = fromRight ? offset : -offset;
            ParentColumnSlide.X = startX;
            CurrentColumnSlide.X = startX;
            PreviewColumnSlide.X = startX;

            var sb = new Windows.UI.Xaml.Media.Animation.Storyboard();
            var dur = new Windows.UI.Xaml.Duration(TimeSpan.FromMilliseconds(180));
            var ease = new Windows.UI.Xaml.Media.Animation.CubicEase
            {
                EasingMode = Windows.UI.Xaml.Media.Animation.EasingMode.EaseOut
            };

            foreach (var target in new[] { ParentColumnSlide, CurrentColumnSlide, PreviewColumnSlide })
            {
                var anim = new Windows.UI.Xaml.Media.Animation.DoubleAnimation
                {
                    To = 0,
                    Duration = dur,
                    EasingFunction = ease
                };
                Windows.UI.Xaml.Media.Animation.Storyboard.SetTarget(anim, target);
                Windows.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(anim, "X");
                sb.Children.Add(anim);
            }

            sb.Begin();
        }

        private void CurrentList_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            switch (e.Key)
            {
                case Windows.System.VirtualKey.GamepadLeftTrigger:
                case Windows.System.VirtualKey.GamepadRightTrigger:
                    e.Handled = true;
                    break;
            }
        }

        private void CurrentList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var items = _navigator.Current?.Entries;
            string itemName = (items != null && CurrentList.SelectedIndex >= 0 && CurrentList.SelectedIndex < items.Count)
                ? items[CurrentList.SelectedIndex].Name : "(none)";
            Log.Info("SELECTION: index={Index} item=\"{Item}\" count={Count} updating={Updating} isMediaActive={MediaActive}",
                CurrentList.SelectedIndex, itemName, items?.Count ?? 0, _updating, _isMediaPlayerActive);

            if (_updating) return;
            if (CurrentList.SelectedIndex >= 0 && _navigator.Current != null)
            {
                _navigator.Current.SelectedIndex = CurrentList.SelectedIndex;

                if (!_isMediaPlayerActive)
                {
                    var selected = CurrentList.SelectedItem as EntryViewModel;

                    // At root: keep debounce for HDD spin-up, but don't update visual elements
                    // (PreviewHeader/PreviewStatus would bleed through the semi-transparent QuickRefPanel).
                    // Favorites column root: show the how-to guide instead of stale "Loading..." text.
                    if (_navigator.Parent != null)
                    {
                        if (_navigator.Current.IsFavorite)
                        {
                            ShowFavoritesGuide();
                        }
                        else
                        {
                            // Instant loading feedback — clear stale preview immediately
                            HideAllPreviewPanels();
                            PreviewHeader.Text = selected?.Name ?? "";
                            PreviewStatus.Text = "Loading...";
                        }
                    }

                    // Debounce preview update — skip if scrolling rapidly. Portal columns
                    // cost a REST round-trip per preview, so give them a longer window.
                    bool isPortal = _navigator.Current.IsPortal;
                    _previewDebounceTimer.Interval = TimeSpan.FromMilliseconds(
                        isPortal ? PortalPreviewDebounceMs : PreviewDebounceMs);
                    _previewDebounceTimer.Stop();
                    _previewDebounceTimer.Start();
                }
            }

            // Update footer count
            int totalCount = _navigator.Current?.Entries.Count ?? 0;
            int selectedIndex = CurrentList.SelectedIndex >= 0 ? CurrentList.SelectedIndex + 1 : 0;
            FooterItemCount.Text = totalCount > 0 ? $"{selectedIndex}/{totalCount}" : "";

            // Update A button label based on selected item type
            UpdateFooterALabelFromSelection();

            // Refresh checkbox colors on selection change
            if (_isBatchMode) UpdateBatchCheckboxes();
        }

        private void OnPreviewDebounceTick(object sender, object e)
        {
            _previewDebounceTimer.Stop();
            _ = _navigator.OnSelectionChangedAsync();
        }

        private static readonly Windows.UI.Xaml.Media.SolidColorBrush _checkBorderNormal = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0x55, 0x93, 0xC4, 0x3C));
        private static readonly Windows.UI.Xaml.Media.SolidColorBrush _checkBorderSelected = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x1A, 0x1D, 0x23));
        private static readonly Windows.UI.Xaml.Media.SolidColorBrush _checkFillNormal = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x93, 0xC4, 0x3C));
        private static readonly Windows.UI.Xaml.Media.SolidColorBrush _checkFillSelected = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x1A, 0x1D, 0x23));

        private void OnCurrentListContainerChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (args.ItemContainer is Windows.UI.Xaml.Controls.ListViewItem container)
            {
                var vm = args.Item as EntryViewModel;
                var check = FindBatchCheck(container);
                if (check != null)
                {
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
        }

        private void UpdateFooterALabel(string label)
        {
            FooterALabel.Text = label;
        }

        private void UpdateFooterALabelFromSelection()
        {
            var selected = CurrentList.SelectedItem as EntryViewModel;
            if (selected == null || FileActionSheetControl.IsOpen)
            {
                UpdateFooterALabel("Enter");
                FooterXLabel.Text = "Refresh";
                return;
            }
            if (selected.IsDirectory || selected.IsArchive)
            {
                UpdateFooterALabel("Open");
                FooterXLabel.Text = "Refresh";
                return;
            }
            string ext = System.IO.Path.GetExtension(selected.Name);
            if (FilePreviewService.IsImageFile(ext) && !FilePreviewService.IsSvgFile(ext)
                || FilePreviewService.IsPdfFile(ext))
            {
                UpdateFooterALabel("Open");
                FooterXLabel.Text = "Refresh";
                return;
            }
            if (FilePreviewService.IsMediaFile(ext))
            {
                UpdateFooterALabel("Play");
                FooterXLabel.Text = "Fullscreen";
                return;
            }
            UpdateFooterALabel("Menu");
            FooterXLabel.Text = "Refresh";
        }

        private void UpdateClipboardIndicator()
        {
            if (ClipboardState.HasItems)
            {
                var count = ClipboardState.Count;
                var item = count == 1 ? "1 item" : $"{count} items";
                FooterClipboardIndicator.Text = $"\U0001F4CB Copied: {item}";
                FooterClipboardIndicator.Visibility = Visibility.Visible;
            }
            else
            {
                FooterClipboardIndicator.Text = "";
                FooterClipboardIndicator.Visibility = Visibility.Collapsed;
            }
            UpdateFooterXLabel();
        }

        private void UpdateFooterXLabel()
        {
            FooterXLabel.Text = "Refresh";
        }

        // --- Input handling ---

        private void OnKeyDown(object sender, KeyRoutedEventArgs e)
        {
            // GamepadInputService handles all gamepad input via polling.
            // Mark gamepad navigation keys as handled to suppress XAML's
            // built-in XY focus navigation sound (the "click" on DPad/A).
            switch (e.Key)
            {
                case Windows.System.VirtualKey.GamepadDPadUp:
                case Windows.System.VirtualKey.GamepadDPadDown:
                case Windows.System.VirtualKey.GamepadDPadLeft:
                case Windows.System.VirtualKey.GamepadDPadRight:
                case Windows.System.VirtualKey.GamepadA:
                    e.Handled = true;
                    break;
            }
        }

        private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            var props = e.GetCurrentPoint(this).Properties;
            if (props.IsRightButtonPressed)
            {
                e.Handled = true;
                OnBack();
            }
        }

        private void OnPreviewNavigationStarting(WebView sender, WebViewNavigationStartingEventArgs args)
        {
            Log.Dbg("OnPreviewNavigationStarting: uri={Uri}", args.Uri?.ToString() ?? "(null)");
        }

        private void OnPreviewNavigationCompleted(WebView sender, WebViewNavigationCompletedEventArgs args)
        {
            Log.Dbg("OnPreviewNavigationCompleted: isSuccess={IsSuccess}", args.IsSuccess);
        }

        public bool IsMediaFullscreen => ImageFullScreen.IsOpen || PdfFullScreen.IsOpen
            || TextEditorOverlayControl.IsOpen
            || VideoFullScreenPanel.Visibility == Visibility.Visible
            || AudioFullScreenPanel.Visibility == Visibility.Visible;

        public bool IsMediaPlayerActive => _isMediaPlayerActive;
    }
}
