using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Shapes;
using XFiles.FileSystem;
using XFiles.Metadata;

namespace XFiles.Controls
{
    public sealed partial class FilePropertiesDialog : UserControl
    {
        private static readonly SolidColorBrush UsedBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x6A, 0xC2, 0x5A));
        private static readonly SolidColorBrush FreeBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x3A, 0x7B, 0xD5));
        private static readonly SolidColorBrush DarkUsedBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x22, 0x3E, 0x1D));
        private static readonly SolidColorBrush DarkFreeBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x1F, 0x3A, 0x5F));
        private static readonly SolidColorBrush MutedBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF));
        private static readonly SolidColorBrush TextBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, ulong> _driveTotals =
            new System.Collections.Concurrent.ConcurrentDictionary<string, ulong>();

        private static readonly FontFamily TitleFont = new FontFamily("ms-appx:///Assets/Fonts/Oxanium-Bold.ttf#Oxanium");

        private static readonly HashSet<string> ImageExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { ".png", ".jpg", ".jpeg", ".gif", ".bmp" };
        private static readonly HashSet<string> AudioExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { ".mp3", ".flac", ".wav", ".m4a", ".aac" };
        private static readonly HashSet<string> VideoExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { ".mp4", ".mkv", ".avi", ".mov", ".webm", ".wmv", ".ts" };

        public bool IsOpen => Visibility == Visibility.Visible;

        public string CurrentPath => _entry?.FullPath;

        public bool CurrentIsDirectory => _entry?.IsDirectory ?? false;

        /// <summary>Re-shows the dialog for the same entry (attributes may have changed).</summary>
        public void Reload(ArchiveBrowser browser)
        {
            if (_entry == null) return;
            Log.Info("FilePropertiesDialog.Reload: {Name}", _entry.Name);
            Show(_entry, browser);
        }

        private CancellationTokenSource _scanCts;
        private FileEntry _entry;
        private ArchiveBrowser _browser;

        public FilePropertiesDialog()
        {
            this.InitializeComponent();
        }

        public void Show(FileEntry entry, ArchiveBrowser browser = null)
        {
            Log.Info("FilePropertiesDialog.Show: {Name} (thread {Thread})", entry?.Name, Environment.CurrentManagedThreadId);
            if (entry == null)
            {
                Log.Warn("FilePropertiesDialog.Show: null entry");
                return;
            }

            _scanCts?.Cancel();
            _scanCts = new CancellationTokenSource();
            _entry = entry;
            _browser = browser;

            RowsPanel.Children.Clear();
            PieHost.Children.Clear();
            PieHost.Visibility = Visibility.Collapsed;
            if (PieCaption != null) PieCaption.Visibility = Visibility.Collapsed;
            StatusText.Text = "";
            PermissionsHint.Visibility = Visibility.Collapsed;
            HeaderIcon.Visibility = Visibility.Visible;
            TitleText.Text = entry.Name;
            NameText.Text = entry.IsDirectory ? "Folder Properties" : "File Properties";

            bool isInArchive = !string.IsNullOrEmpty(entry.ArchiveRootPath);

            if (isInArchive)
            {
                ShowArchiveEntry(entry);
            }
            else if (entry.IsNetwork)
            {
                ShowNetworkEntry(entry);
            }
            else if (entry.IsDirectory)
            {
                ShowFolderEntry(entry);
            }
            else
            {
                ShowFileEntry(entry);
            }

            Overlay.Visibility = Visibility.Visible;
            Visibility = Visibility.Visible;
        }

        public void Close()
        {
            _scanCts?.Cancel();
            if (_scanBar != null) _scanBar.Visibility = Visibility.Collapsed;
            Overlay.Visibility = Visibility.Collapsed;
            Visibility = Visibility.Collapsed;
            Log.Info("FilePropertiesDialog.Close: {Name}", _entry?.Name);
        }

        /// <summary>Router entry: dpad pages the row list; B closes; Y opens permissions (local only).</summary>
        public void HandleDPad(VirtualKey key)
        {
            if (key == VirtualKey.Up)
            {
                double v = RowsScrollViewer.VerticalOffset;
                RowsScrollViewer.ChangeView(null, Math.Max(0, v - 32), null, true);
            }
            else if (key == VirtualKey.Down)
            {
                double v = RowsScrollViewer.VerticalOffset;
                RowsScrollViewer.ChangeView(null, v + 32, null, true);
            }
        }

        public event Action PermissionsRequested;

        /// <summary>Router entry: button handling for the dialog.</summary>
        public void HandleButton(VirtualKey key)
        {
            switch (key)
            {
                case VirtualKey.GamepadB:
                case VirtualKey.Escape:
                    Close();
                    break;
                case VirtualKey.GamepadY:
                case VirtualKey.Y:
                    if (PermissionsHint.Visibility == Visibility.Visible)
                    {
                        Log.Info("FilePropertiesDialog: permissions requested for {Name}", _entry?.Name);
                        PermissionsRequested?.Invoke();
                    }
                    break;
            }
        }

        // ---------- local folder: incremental background scan + pie ----------

        private void ShowFolderEntry(FileEntry entry)
        {
            string path = entry.FullPath ?? "";
            AddRow("Type", "Folder");
            AddRow("Location", path);

            var (attrs, created, modified, accessed) = FileOperations.TryGetItemInfo(path);
            if (modified.HasValue) AddRow("Modified", modified.Value.ToLocalTime().ToString("g"));
            if (created.HasValue) AddRow("Created", created.Value.ToLocalTime().ToString("g"));
            if (accessed.HasValue) AddRow("Accessed", accessed.Value.ToLocalTime().ToString("g"));
            if (attrs != 0) AddRow("Attributes", FormatAttributes(attrs));
            PermissionsHint.Visibility = Visibility.Visible;

            PieHost.Visibility = Visibility.Visible;
            if (PieCaption != null) PieCaption.Visibility = Visibility.Visible;
            AddRow("Total size", "Calculating…");
            AddRow("Files", "Calculating…");
            AddRow("Subfolders", "Calculating…");
            AddProgressBarRow();

            StatusText.Text = "Reading folder…";
            var ct = _scanCts.Token;

            // Progress<T> must be created on the UI thread so its callback posts
            // through the dispatcher; creating it inside Task.Run captures the
            // thread-pool context and crashes with RPC_E_WRONG_THREAD on XAML.
            var uiProgress = new Progress<DirectoryStatsSnapshot>(snapshot =>
            {
                if (ct.IsCancellationRequested) return;
                ulong total = 0;
                if (_driveTotals.TryGetValue(path, out ulong knownTotal)) total = knownTotal;
                RenderFolderSnapshot(snapshot, total, path);
            });

            _ = Task.Run(async () =>
            {
                // Drive free space (slow P/Invoke on spun-down drives) for the pie.
                ulong total = 0;
                try
                {
                    var space = FileOperations.GetDriveFreeSpace(System.IO.Path.GetPathRoot(path));
                    if (space.HasValue) total = space.Value.TotalBytes;
                }
                catch { }
                _driveTotals[path] = total;

                var final = await DirectoryStatsCalculator.ComputeAsync(path, uiProgress, ct);
                if (ct.IsCancellationRequested) return;

                await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, async () =>
                {
                    if (ct.IsCancellationRequested) return;
                    RenderFolderSnapshot(final, total, path);
                    if (final.Complete)
                    {
                        const string doneText = "Fully scanned";
                        StatusText.Text = doneText;
                        await System.Threading.Tasks.Task.Delay(2000);
                        if (!ct.IsCancellationRequested && StatusText.Text == doneText) StatusText.Text = "";
                    }
                    else
                    {
                        StatusText.Text = "Stopped early — folder too large or cancelled";
                    }
                    Log.Info("FilePropertiesDialog.ShowFolderEntry: {Path} -> files={Files} folders={Folders} bytes={Bytes} complete={Complete}",
                        path, final.FileCount, final.FolderCount, final.TotalBytes, final.Complete);
                });
            });
        }

        private void RenderFolderSnapshot(DirectoryStatsSnapshot s, ulong driveTotal, string path)
        {
            // Values already rendered by previous ticks remain; update the three stat rows.
            var rows = GetRows();
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].Label == "Total size") rows[i].Value.Text = Formatting.FormatSize(s.TotalBytes);
                else if (rows[i].Label == "Files") rows[i].Value.Text = s.FileCount.ToString("N0");
                else if (rows[i].Label == "Subfolders") rows[i].Value.Text = s.FolderCount.ToString("N0");
            }

            // Pie: folder bytes vs its drive total. Tiny folders get a minimum
// slice (0.02) so the used wedge stays visible even if not proportional.
            double total = driveTotal > 0 ? (double)driveTotal : 0;
            double fraction = total > 0 ? Math.Max(0, Math.Min(1, s.TotalBytes / total)) : 0;
            const double MinPieSlice = 0.02;
            double realFraction = fraction;
            if (fraction > 0 && fraction < MinPieSlice) fraction = MinPieSlice;
            BuildPie(PieHost, fraction);

            if (PieCaption != null)
            {
                if (driveTotal > 0)
                {
                    PieCaption.Text = $"{Formatting.FormatSize(s.TotalBytes)} of {Formatting.FormatSize((long)driveTotal)} · {realFraction:P2} of drive";
                }
                else
                {
                    PieCaption.Text = Formatting.FormatSize(s.TotalBytes);
                }
            }

            if (!s.Complete)
            {
                StatusText.Text = s.Cancelled
                    ? "Stopped"
                    : $"Reading… {s.FileCount:N0} files · {Formatting.FormatSize(s.TotalBytes)}";
            }

            if (_scanBar != null && (s.Complete || s.Cancelled))
            {
                _scanBar.Visibility = Visibility.Collapsed;
            }
        }

        // ---------- local file: probe media/image/pdf on background ----------

        private void ShowFileEntry(FileEntry entry)
        {
            string path = entry.FullPath ?? "";
            string ext = System.IO.Path.GetExtension(entry.Name);
            bool isImage = ImageExts.Contains(ext);
            bool isAudio = AudioExts.Contains(ext);
            bool isVideo = VideoExts.Contains(ext);

            AddRow("Type", ext.Length > 0 ? $"{ext.TrimStart('.')} file" : "File");
            AddRow("Location", path);
            if (entry.SizeBytes > 0) AddRow("Size", Formatting.FormatSize(entry.SizeBytes));
            var (attrs, created, modified, accessed) = FileOperations.TryGetItemInfo(path);
            if (modified.HasValue) AddRow("Modified", modified.Value.ToLocalTime().ToString("g"));
            if (created.HasValue) AddRow("Created", created.Value.ToLocalTime().ToString("g"));
            if (accessed.HasValue) AddRow("Accessed", accessed.Value.ToLocalTime().ToString("g"));
            if (attrs != 0) AddRow("Attributes", FormatAttributes(attrs));
            PermissionsHint.Visibility = Visibility.Visible;

            if (isImage || isAudio || isVideo || ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                StatusText.Text = "Reading file info…";
                var ct = _scanCts.Token;
                _ = Task.Run(async () =>
                {
                    if (isImage)
                    {
                        byte[] head = MediaMetadataProbe.ReadFileHead(path);
                        var (w, h) = MediaMetadataProbe.ProbeImageDimensions(head);
                        if (ct.IsCancellationRequested) return;
                        await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                        {
                            if (ct.IsCancellationRequested) return;
                            AddRow("Dimensions", $"{w} × {h}", force: true);
                            StatusText.Text = "";
                        });
                    }
                    else if (ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
                    {
                        int pages = MediaMetadataProbe.ProbePdfPageCount(path);
                        if (ct.IsCancellationRequested) return;
                        await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                        {
                            if (ct.IsCancellationRequested) return;
                            if (pages > 0) AddRow("Pages", pages.ToString("N0"), force: true);
                            StatusText.Text = "";
                        });
                    }
                    else
                    {
                        var meta = MediaMetadataProbe.ProbeFile(path);
                        if (ct.IsCancellationRequested) return;
                        await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                        {
                            if (ct.IsCancellationRequested) return;
                            if (meta.DurationMs.HasValue)
                            {
                                AddRow("Duration", Formatting.FormatFsTime(TimeSpan.FromMilliseconds(meta.DurationMs.Value)), force: true);
                            }
                            if (meta.Codec != null) AddRow("Codec", meta.Codec, force: true);
                            if (meta.BitrateKbps.HasValue && meta.BitrateKbps.Value > 0)
                                AddRow("Bitrate", $"{meta.BitrateKbps.Value} kbps", force: true);
                            if (meta.SampleRateHz.HasValue)
                                AddRow("Sample rate", $"{meta.SampleRateHz.Value} Hz", force: true);
                            if (meta.Channels.HasValue)
                                AddRow("Channels", meta.Channels.Value == 1 ? "Mono" : meta.Channels.Value == 2 ? "Stereo" : meta.Channels.Value.ToString(), force: true);
                            if (meta.BitsPerSample.HasValue)
                                AddRow("Bits", meta.BitsPerSample.Value.ToString(), force: true);
                            if (meta.Width.HasValue && meta.Height.HasValue)
                                AddRow("Dimensions", $"{meta.Width} × {meta.Height}", force: true);
                            if (meta.FrameRateFps.HasValue)
                                AddRow("Frame rate", $"{meta.FrameRateFps.Value:F2} fps", force: true);
                            StatusText.Text = "";
                        });
                    }
                });
            }

            bool isArchive = !entry.IsDirectory && ArchiveBrowser.IsArchiveFile(entry.Name);
            if (isArchive)
            {
                AddCompressionBar();
                AddRow("Uncompressed size", "Calculating…");
                AddRow("Files", "Calculating…");
                AddRow("Ratio", "Calculating…");
                AddRow("Saved", "Calculating…");
                AddProgressBarRow();

                StatusText.Text = "Reading archive…";
                var ct = _scanCts.Token;
                _ = Task.Run(async () =>
                {
                    long uncompressed = 0;
                    long files = 0;
                    long folders = 0;
                    try
                    {
                        var probe = new ArchiveBrowser();
                        try
                        {
                            var stats = probe.ComputeSubtreeStats(path, "");
                            uncompressed = stats.TotalBytes;
                            files = stats.FileCount;
                            folders = stats.FolderCount;
                        }
                        finally
                        {
                            probe.Dispose();
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warn("FilePropertiesDialog.ShowFileEntry: compression scan failed for {Path}: {Error}", path, ex.Message);
                    }

                    if (ct.IsCancellationRequested) return;
                    await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                    {
                        if (ct.IsCancellationRequested) return;
                        RenderCompressionState(uncompressed, entry.SizeBytes, files, folders);
                        StatusText.Text = "";
                    });
                });
            }
        }

        // ---------- archive file (local): compression ratio ----------
        //
        // WinRAR-style rectangle: green = compressed size actually on disk,
        // blue = space saved versus the uncompressed content. Uncompressed size
        // comes from a background archive listing; remote archives keep the
        // basic network mode (opening the whole file over the wire is costly).

        private Canvas _cmpHost;
        private Rectangle _cmpFill;
        private Windows.UI.Xaml.Controls.ProgressBar _scanBar;

        private void AddProgressBarRow()
        {
            var bar = new Windows.UI.Xaml.Controls.ProgressBar
            {
                IsIndeterminate = true,
                Width = 260,
                Height = 4,
                Margin = new Thickness(132, 4, 0, 10),
                Foreground = UsedBrush,
                Background = DarkFreeBrush,
                HorizontalAlignment = Windows.UI.Xaml.HorizontalAlignment.Left
            };
            RowsPanel.Children.Add(bar);
            _scanBar = bar;
        }

        private void AddCompressionBar()
        {
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 6)
            };
            row.Children.Add(new TextBlock
            {
                Text = "Compression",
                Foreground = MutedBrush,
                FontFamily = TitleFont,
                FontSize = 12,
                Width = 130,
                VerticalAlignment = VerticalAlignment.Center
            });

            var host = new Canvas
            {
                Width = 260,
                Height = 18,
                VerticalAlignment = VerticalAlignment.Center
            };
            host.Children.Add(new Rectangle
            {
                Width = 260,
                Height = 18,
                Fill = DarkFreeBrush,
                RadiusX = 3,
                RadiusY = 3
            });
            _cmpFill = new Rectangle
            {
                Width = 0,
                Height = 18,
                Fill = UsedBrush,
                RadiusX = 3,
                RadiusY = 3
            };
            host.Children.Add(_cmpFill);

            row.Children.Add(host);
            RowsPanel.Children.Add(row);
            _cmpHost = host;
        }

        private void RenderCompressionState(long uncompressed, long compressed, long files, long folders)
        {
            string ratioText = "—";
            string savedText = "—";
            if (_cmpFill != null)
            {
                if (compressed == 0 || uncompressed == 0)
                {
                    _cmpFill.Width = 0;
                }
                else if (compressed >= uncompressed)
                {
                    _cmpFill.Width = 260;
                    ratioText = "1.0×";
                    savedText = "0%";
                }
                else
                {
                    double packed = (double)compressed / uncompressed;
                    _cmpFill.Width = 260.0 * packed;
                    ratioText = $"{uncompressed / (double)compressed:0.0}×";
                    savedText = $"{(1.0 - packed) * 100:0}%";
                }
            }

            foreach (var r in GetRows())
            {
                if (r.Label == "Uncompressed size")
                {
                    if (uncompressed > 0) r.Value.Text = Formatting.FormatSize(uncompressed);
                    else r.Value.Text = "—";
                }
                else if (r.Label == "Files") r.Value.Text = files.ToString("N0");
                else if (r.Label == "Ratio") r.Value.Text = ratioText;
                else if (r.Label == "Saved") r.Value.Text = savedText;
            }
            if (_scanBar != null && (uncompressed > 0 || compressed > 0))
            {
                _scanBar.Visibility = Visibility.Collapsed;
            }
            Log.Info("FilePropertiesDialog.RenderCompressionState: uncompressed={U} compressed={C} files={F} folders={D}",
                uncompressed, compressed, files, folders);
        }

        private static string FormatAttributes(uint attrs)
        {
            const uint FILE_ATTRIBUTE_READONLY = 0x1;
            const uint FILE_ATTRIBUTE_HIDDEN = 0x2;
            const uint FILE_ATTRIBUTE_SYSTEM = 0x4;
            const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;
            const uint FILE_ATTRIBUTE_ARCHIVE = 0x20;
            const uint FILE_ATTRIBUTE_NOT_CONTENT_INDEXED = 0x2000;
            var list = new System.Collections.Generic.List<string>();
            if ((attrs & FILE_ATTRIBUTE_READONLY) != 0) list.Add("Read-only");
            if ((attrs & FILE_ATTRIBUTE_HIDDEN) != 0) list.Add("Hidden");
            if ((attrs & FILE_ATTRIBUTE_SYSTEM) != 0) list.Add("System");
            if ((attrs & FILE_ATTRIBUTE_ARCHIVE) != 0) list.Add("Archive");
            if ((attrs & FILE_ATTRIBUTE_NOT_CONTENT_INDEXED) != 0) list.Add("Not indexed");
            if ((attrs & FILE_ATTRIBUTE_DIRECTORY) != 0) list.Add("Folder");
            return list.Count > 0 ? string.Join(", ", list) : attrs.ToString();
        }

        // ---------- in-archive: subtree stats from memory (0 I/O) ----------

        private void ShowArchiveEntry(FileEntry entry)
        {
            AddRow("Type", entry.IsDirectory ? "Folder (in archive)" : "File (in archive)");
            if (!string.IsNullOrEmpty(entry.FullPath)) AddRow("In", entry.FullPath);
            if (!string.IsNullOrEmpty(entry.ArchiveRootPath)) AddRow("Archive", entry.ArchiveRootPath);
            if (entry.SizeBytes > 0) AddRow("Size", Formatting.FormatSize(entry.SizeBytes));

            var stats = _browser != null
                ? _browser.ComputeSubtreeStats(entry.ArchiveRootPath, entry.ArchiveInternalPath)
                : new ArchiveSubtreeStats.Stats();
            Log.Info("FilePropertiesDialog.ShowArchiveEntry: {Name} -> files={Files} folders={Folders} bytes={Bytes}",
                entry.Name, stats.FileCount, stats.FolderCount, stats.TotalBytes);

            AddRow("Files", stats.FileCount.ToString("N0"));
            AddRow("Subfolders", stats.FolderCount.ToString("N0"));
            AddRow("Total size", Formatting.FormatSize(stats.TotalBytes));
            NameText.Text = string.IsNullOrEmpty(entry.ArchiveInternalPath) ? entry.Name : entry.ArchiveInternalPath;
        }

        // ---------- network: basic only ----------

        private void ShowNetworkEntry(FileEntry entry)
        {
            AddRow("Type", entry.IsDirectory ? "Remote folder" : "Remote file");
            AddRow("Source", entry.NetworkShareName ?? "Network");
            string remotePath = entry.NetworkPath;
            if (!string.IsNullOrEmpty(remotePath)) AddRow("Path", remotePath);
            if (entry.SizeBytes > 0) AddRow("Size", Formatting.FormatSize(entry.SizeBytes));
            if (entry.LastModified.HasValue)
                AddRow("Modified", entry.LastModified.Value.ToLocalTime().ToString("g"));
            StatusText.Text = "Network properties";
        }

        // ---------- row plumbing ----------

        private class RowEntry
        {
            public string Label;
            public TextBlock Value;
        }

        private readonly List<RowEntry> _rows = new List<RowEntry>();

        private List<RowEntry> GetRows()
        {
            _rows.Clear();
            foreach (var child in RowsPanel.Children)
            {
                if (child is StackPanel sp && sp.Children.Count == 2)
                {
                    if (sp.Children[0] is TextBlock label && sp.Children[1] is TextBlock value)
                    {
                        _rows.Add(new RowEntry { Label = label.Text, Value = value });
                    }
                }
            }
            return _rows;
        }

        private void AddRow(string label, string value, bool force = false)
        {
            // "force" rows (appended after initial layout) bypass the label index lookup;
            // updates use SetRowValue-like logic so duplicates are avoided.
            if (!force)
            {
                foreach (var existing in _rows)
                {
                    if (existing.Label == label)
                    {
                        existing.Value.Text = value;
                        return;
                    }
                }
            }
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 6)
            };
            row.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = MutedBrush,
                FontFamily = TitleFont,
                FontSize = 12,
                Width = 130,
                VerticalAlignment = VerticalAlignment.Center
            });
            row.Children.Add(new TextBlock
            {
                Text = value,
                Foreground = TextBrush,
                FontFamily = TitleFont,
                FontSize = 13,
                MaxWidth = 340,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
                VerticalAlignment = VerticalAlignment.Center
            });
            RowsPanel.Children.Add(row);
            _rows.Add(new RowEntry { Label = label, Value = (TextBlock)row.Children[1] });
        }

        // ---------- pie ----------

        private void BuildPie(Canvas canvas, double usedFraction)
        {
            canvas.Children.Clear();
            double s = canvas.Width / 160.0;
            double cx = 80.0 * s;
            double cy = 80.0 * s;
            double r = 62.0 * s;
            double extrude = 9.0 * s;

            var slices = PieGeometry.Slices(usedFraction);
            bool empty = usedFraction <= 0;
            for (int i = 0; i < slices.Length; i++)
            {
                var slice = slices[i];
                if (slice.Fraction <= 0) continue;

                var brush = slices.Length == 1
                    ? (empty ? FreeBrush : UsedBrush)
                    : (i == 0 ? UsedBrush : FreeBrush);
                var darkBrush = slices.Length == 1
                    ? (empty ? DarkFreeBrush : DarkUsedBrush)
                    : (i == 0 ? DarkUsedBrush : DarkFreeBrush);

                if (slice.Fraction >= 0.999)
                {
                    canvas.Children.Add(new Path
                    {
                        Data = BuildFullCircle(cx, cy + extrude, r),
                        Fill = darkBrush
                    });
                    canvas.Children.Add(new Path
                    {
                        Data = BuildFullCircle(cx, cy, r),
                        Fill = brush
                    });
                    continue;
                }
                canvas.Children.Add(new Path
                {
                    Data = BuildWedge(cx, cy + extrude, r, slice.StartDeg, slice.EndDeg),
                    Fill = darkBrush
                });
                canvas.Children.Add(new Path
                {
                    Data = BuildWedge(cx, cy, r, slice.StartDeg, slice.EndDeg),
                    Fill = brush
                });
            }
            canvas.Children.Add(new Path
            {
                Data = BuildFullCircle(cx, cy, r),
                Stroke = new SolidColorBrush(Windows.UI.Color.FromArgb(120, 0xFF, 0xFF, 0xFF)),
                StrokeThickness = 1.5
            });
        }

        private static PathGeometry BuildFullCircle(double cx, double cy, double r)
        {
            var fig = new PathFigure { IsClosed = true, IsFilled = true };
            var left = PieGeometry.ArcPoint(cx, cy, r, 180);
            var right = PieGeometry.ArcPoint(cx, cy, r, 0);
            fig.StartPoint = new Point(left.X, left.Y);
            fig.Segments.Add(new ArcSegment
            {
                Point = new Point(right.X, right.Y),
                Size = new Size(r, r),
                IsLargeArc = true,
                SweepDirection = SweepDirection.Clockwise
            });
            fig.Segments.Add(new ArcSegment
            {
                Point = new Point(left.X, left.Y),
                Size = new Size(r, r),
                IsLargeArc = true,
                SweepDirection = SweepDirection.Clockwise
            });
            var g = new PathGeometry();
            g.Figures.Add(fig);
            return g;
        }

        private static PathGeometry BuildWedge(double cx, double cy, double r, double startDeg, double endDeg)
        {
            var fig = new PathFigure { IsClosed = true, IsFilled = true };
            var p1 = PieGeometry.ArcPoint(cx, cy, r, startDeg);
            var p2 = PieGeometry.ArcPoint(cx, cy, r, endDeg);
            fig.StartPoint = new Point(cx, cy);
            fig.Segments.Add(new LineSegment { Point = new Point(p1.X, p1.Y) });
            fig.Segments.Add(new ArcSegment
            {
                Point = new Point(p2.X, p2.Y),
                Size = new Size(r, r),
                IsLargeArc = PieGeometry.IsLargeArc(startDeg, endDeg),
                SweepDirection = SweepDirection.Clockwise
            });
            var g = new PathGeometry();
            g.Figures.Add(fig);
            return g;
        }
    }
}