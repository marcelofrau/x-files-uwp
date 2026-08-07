using System;
using System.Linq;
using Windows.Foundation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Shapes;
using XFiles.FileSystem;

namespace XFiles.Controls
{
    public struct DiskVolumeInfo
    {
        public string Root;
        public string Label;

        public DiskVolumeInfo(string root, string label)
        {
            Root = root;
            Label = label;
        }
    }

    public sealed partial class DiskUsageDialog : UserControl
    {
        private static readonly SolidColorBrush UsedBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x3A, 0x7B, 0xD5));
        private static readonly SolidColorBrush FreeBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x6A, 0xC2, 0x5A));
        private static readonly SolidColorBrush BodyBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x1F, 0x3A, 0x5F));
        private static readonly SolidColorBrush MutedBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF));
        private static readonly SolidColorBrush TextBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));
        private static readonly SolidColorBrush BarBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x2A, 0x2D, 0x33));
        private static readonly FontFamily TitleFont = new FontFamily("ms-appx:///Assets/Fonts/Oxanium-Bold.ttf#Oxanium");
        private static readonly FontFamily MonoFont = new FontFamily("ms-appx:///Assets/Inconsolata-Regular.ttf#Inconsolata");

        public bool IsOpen => Visibility == Visibility.Visible;

        public DiskUsageDialog()
        {
            this.InitializeComponent();
        }

        public void Show(params DiskVolumeInfo[] volumes)
        {
            var roots = (volumes ?? Array.Empty<DiskVolumeInfo>())
                .Where(v => !string.IsNullOrEmpty(v.Root))
                .GroupBy(v => System.IO.Path.GetPathRoot(v.Root) ?? v.Root, StringComparer.OrdinalIgnoreCase)
                .Select(g => new DiskVolumeInfo(g.Key, g.First().Label))
                .ToList();

            if (roots.Count == 0)
            {
                Log.Warn("DiskUsageDialog.Show: no volumes to display");
                return;
            }

            bool compact = roots.Count > 1;
            DriveText.Text = "Storage usage";

            VolumesPanel.Children.Clear();
            foreach (var vol in roots)
            {
                VolumesPanel.Children.Add(BuildVolumeRow(vol, compact));
            }

            Overlay.Visibility = Visibility.Visible;
            Visibility = Visibility.Visible;
            Log.Info("DiskUsageDialog.Show: {Roots}", string.Join(", ", roots.Select(DisplayName)));
        }

        public void Close()
        {
            Overlay.Visibility = Visibility.Collapsed;
            Visibility = Visibility.Collapsed;
            Log.Info("DiskUsageDialog.Close");
        }

        private static string DisplayName(DiskVolumeInfo vol) =>
            vol.Label == null ? vol.Root : $"{vol.Root} ({vol.Label})";

        private FrameworkElement BuildVolumeRow(DiskVolumeInfo vol, bool compact)
        {
            string root = vol.Root;
            var space = FileOperations.GetDriveFreeSpace(root);
            ulong total = 0, free = 0, used = 0;
            double fraction = 0.0;
            int pct = 0;
            if (space == null)
            {
                Log.Warn("DiskUsageDialog.BuildVolumeRow: cannot query free space for {Root}", root);
            }
            else
            {
                total = space.Value.TotalBytes;
                free = space.Value.FreeBytes;
                used = total > free ? total - free : 0;
                fraction = total > 0 ? (double)used / total : 0.0;
                pct = (int)Math.Round(fraction * 100.0);
            }

            double pieSize = compact ? 160 : 240;
            double barWidth = compact ? 180 : 260;
            double pctFontSize = compact ? 22 : 30;
            double labelFontSize = compact ? 13 : 16;
            double gap = compact ? 4 : 8;

            var canvas = new Canvas
            {
                Width = pieSize,
                Height = pieSize,
                Margin = new Thickness(0, 0, 20, 0)
            };
            BuildPie(canvas, fraction);

            var stats = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 0, compact ? 6 : 12)
            };
            stats.Children.Add(new TextBlock
            {
                Text = space == null ? "?" : $"{pct}%",
                Foreground = UsedBrush,
                FontFamily = TitleFont,
                FontSize = pctFontSize,
                Margin = new Thickness(0, 0, 0, compact ? 6 : 12)
            });
            stats.Children.Add(StatRow("Total", space == null ? "—" : Formatting.FormatSize((long)total), compact));
            stats.Children.Add(StatRow("Free", space == null ? "—" : Formatting.FormatSize((long)free), compact, FreeBrush));
            stats.Children.Add(StatRow("Used", space == null ? "—" : Formatting.FormatSize((long)used), compact, UsedBrush));
            stats.Children.Add(new ProgressBar
            {
                Minimum = 0,
                Maximum = 100,
                Value = pct,
                Height = compact ? 10 : 12,
                Width = barWidth,
                Foreground = UsedBrush,
                Background = BarBrush
            });

            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            row.Children.Add(canvas);
            row.Children.Add(stats);

            var block = new StackPanel { Margin = new Thickness(0, gap, 0, gap) };
            block.Children.Add(new TextBlock
            {
                Text = DisplayName(vol),
                Foreground = UsedBrush,
                FontFamily = MonoFont,
                FontSize = labelFontSize,
                Margin = new Thickness(0, 0, 0, gap),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            block.Children.Add(row);
            return block;
        }

        private static StackPanel StatRow(string label, string value, bool compact, SolidColorBrush valueBrush = null)
        {
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, compact ? 4 : 6)
            };
            row.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = MutedBrush,
                FontFamily = TitleFont,
                FontSize = compact ? 11 : 12,
                Width = compact ? 64 : 72
            });
            row.Children.Add(new TextBlock
            {
                Text = value,
                Foreground = valueBrush ?? TextBrush,
                FontFamily = MonoFont,
                FontSize = compact ? 12 : 13
            });
            return row;
        }

        private void BuildPie(Canvas canvas, double usedFraction)
        {
            canvas.Children.Clear();

            double s = canvas.Width / 240.0;
            double cx = 120.0 * s;
            double cy = 120.0 * s;
            double r = 92.0 * s;
            double extrude = 14.0 * s;

            // Body: full dark disc at the extruded offset (isometric "pizza" depth).
            canvas.Children.Add(new Path
            {
                Data = BuildFullCircle(cx, cy + extrude, r),
                Fill = BodyBrush
            });

            var slices = PieGeometry.Slices(usedFraction);
            foreach (var slice in slices)
            {
                var brush = slice.Fraction > 0.5 ? UsedBrush : FreeBrush;
                if (slice.Fraction <= 0) continue;
                if (slice.Fraction >= 0.999)
                {
                    canvas.Children.Add(new Path
                    {
                        Data = BuildFullCircle(cx, cy, r),
                        Fill = brush
                    });
                    continue;
                }
                canvas.Children.Add(new Path
                {
                    Data = BuildWedge(cx, cy, r, slice.StartDeg, slice.EndDeg),
                    Fill = brush
                });
            }

            // Slim rim highlight on top face edge.
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
