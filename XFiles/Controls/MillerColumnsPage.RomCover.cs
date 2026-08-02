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
        private async Task LoadRomCoverFromLocalFileAsync(string filePath)
        {
            _coverArtCts?.Cancel();
            _coverArtCts = new CancellationTokenSource();
            var ct = _coverArtCts.Token;

            try
            {
                using (var stream = DirectoryScanner.OpenFileRead(filePath))
                {
                    if (stream == null) return;

                    var bitmap = new Windows.UI.Xaml.Media.Imaging.BitmapImage();
                    await bitmap.SetSourceAsync(stream.AsRandomAccessStream());

                    if (ct.IsCancellationRequested) return;

                    RomCoverImage.Source = bitmap;
                    RomCoverImage.Visibility = Visibility.Visible;

                    Log.Verb("LoadRomCoverFromLocalFileAsync: loaded {Path}", filePath);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Log.Verb("LoadRomCoverFromLocalFileAsync: failed - {Message}", ex.Message);
            }
        }

        private async Task FetchRomCoverArtAsync(string system, string title)
        {
            if (string.IsNullOrEmpty(system) || system == "ROM" || string.IsNullOrEmpty(title))
                return;

            _coverArtCts?.Cancel();
            _coverArtCts = new CancellationTokenSource();
            var ct = _coverArtCts.Token;

            try
            {
                if (!RomCoverProvider.LibRetroSystemNames.TryGetValue(system, out string libRetroSystem))
                    return;

                var titleVariations = RomCoverProvider.BuildTitleVariations(title);
                var missedUrls = new List<string>();

                foreach (var variation in titleVariations)
                {
                    if (ct.IsCancellationRequested) return;

                    string url = $"https://thumbnails.libretro.com/{Uri.EscapeDataString(libRetroSystem)}/Named_Titles/{Uri.EscapeDataString(variation)}.png";

                    // Check SQLite cache — skip if already known (hit or miss, within 30 days)
                    if (await Metadata.MetadataCache.IsLibRetroUrlCachedAsync(url))
                    {
                        Log.Verb("FetchRomCoverArtAsync: cached skip {Url}", url);
                        continue;
                    }

                    Log.Verb("FetchRomCoverArtAsync: trying {Url}", url);

                    var response = await _coverArtClient.GetAsync(url, ct);
                    if (!response.IsSuccessStatusCode)
                    {
                        Log.Verb("FetchRomCoverArtAsync: {StatusCode} for '{Variation}'", response.StatusCode, variation);
                        missedUrls.Add(url);
                        continue;
                    }

                    var stream = await response.Content.ReadAsStreamAsync();
                    var bitmap = new Windows.UI.Xaml.Media.Imaging.BitmapImage();
                    await bitmap.SetSourceAsync(stream.AsRandomAccessStream());

                    if (ct.IsCancellationRequested) return;

                    RomCoverImage.Source = bitmap;
                    RomCoverImage.Visibility = Visibility.Visible;

                    // Cache as hit
                    await Metadata.MetadataCache.SetLibRetroThumbnailAsync(url, true);

                    Log.Info("FetchRomCoverArtAsync: loaded cover for '{Title}' via variation '{Variation}'",
                        title, variation);
                    return;
                }

                // All variations missed — cache them so we don't retry for 30 days
                foreach (var missedUrl in missedUrls)
                {
                    await Metadata.MetadataCache.SetLibRetroThumbnailAsync(missedUrl, false);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Log.Verb("FetchRomCoverArtAsync: failed - {Message}", ex.Message);
            }
        }

        private string _lastErrorText = "";
        private static readonly HttpClient _errorShareClient = new HttpClient();
        private static readonly HttpClient _coverArtClient = new HttpClient();
        private static CancellationTokenSource _coverArtCts;
    }
}
