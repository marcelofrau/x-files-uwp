using System;
using Windows.Foundation;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace XFiles.Controls
{
    public sealed class MarqueeTextBlock : UserControl
    {
        private readonly TextBlock _textBlock;
        private DispatcherTimer _marqueeTimer;
        private double _offset;
        private bool _needsMarquee;

        public static readonly DependencyProperty TextProperty =
            DependencyProperty.Register(nameof(Text), typeof(string), typeof(MarqueeTextBlock),
                new PropertyMetadata("", OnTextOrFontChanged));

        public static readonly DependencyProperty FontSizeProperty =
            DependencyProperty.Register(nameof(FontSize), typeof(double), typeof(MarqueeTextBlock),
                new PropertyMetadata(14.0, OnTextOrFontChanged));

        public static readonly DependencyProperty ForegroundProperty =
            DependencyProperty.Register(nameof(Foreground), typeof(Brush), typeof(MarqueeTextBlock),
                new PropertyMetadata(null, OnForegroundChanged));

        public static readonly DependencyProperty FontFamilyProperty =
            DependencyProperty.Register(nameof(FontFamily), typeof(FontFamily), typeof(MarqueeTextBlock),
                new PropertyMetadata(null));

        public static readonly DependencyProperty FontWeightProperty =
            DependencyProperty.Register(nameof(FontWeight), typeof(FontWeight), typeof(MarqueeTextBlock),
                new PropertyMetadata(FontWeights.Normal));

        public static readonly DependencyProperty MarqueeProperty =
            DependencyProperty.Register(nameof(Marquee), typeof(bool), typeof(MarqueeTextBlock),
                new PropertyMetadata(false, OnMarqueeChanged));

        public static readonly DependencyProperty MarqueeSpeedProperty =
            DependencyProperty.Register(nameof(MarqueeSpeed), typeof(double), typeof(MarqueeTextBlock),
                new PropertyMetadata(40.0));

        public static readonly DependencyProperty PaddingProperty =
            DependencyProperty.Register(nameof(Padding), typeof(Thickness), typeof(MarqueeTextBlock),
                new PropertyMetadata(new Thickness(0), OnPaddingChanged));

        public static readonly DependencyProperty TextTrimmingProperty =
            DependencyProperty.Register(nameof(TextTrimming), typeof(TextTrimming), typeof(MarqueeTextBlock),
                new PropertyMetadata(TextTrimming.CharacterEllipsis));

        private readonly Grid _rootGrid;

        public string Text
        {
            get => (string)GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }

        public new double FontSize
        {
            get => (double)GetValue(FontSizeProperty);
            set => SetValue(FontSizeProperty, value);
        }

        public new Brush Foreground
        {
            get => (Brush)GetValue(ForegroundProperty);
            set => SetValue(ForegroundProperty, value);
        }

        public new FontFamily FontFamily
        {
            get => (FontFamily)GetValue(FontFamilyProperty);
            set => SetValue(FontFamilyProperty, value);
        }

        public new FontWeight FontWeight
        {
            get => (FontWeight)GetValue(FontWeightProperty);
            set => SetValue(FontWeightProperty, value);
        }

        public bool Marquee
        {
            get => (bool)GetValue(MarqueeProperty);
            set => SetValue(MarqueeProperty, value);
        }

        public double MarqueeSpeed
        {
            get => (double)GetValue(MarqueeSpeedProperty);
            set => SetValue(MarqueeSpeedProperty, value);
        }

        public TextTrimming TextTrimming
        {
            get => (TextTrimming)GetValue(TextTrimmingProperty);
            set => SetValue(TextTrimmingProperty, value);
        }

        public new Thickness Padding
        {
            get => (Thickness)GetValue(PaddingProperty);
            set => SetValue(PaddingProperty, value);
        }

        public MarqueeTextBlock()
        {
            _textBlock = new TextBlock
            {
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.NoWrap
            };

            _rootGrid = new Grid();
            var clip = new Windows.UI.Xaml.Media.RectangleGeometry();
            _rootGrid.Clip = clip;
            _rootGrid.SizeChanged += (s, e) =>
            {
                clip.Rect = new Windows.Foundation.Rect(0, 0, _rootGrid.ActualWidth, _rootGrid.ActualHeight);
            };
            _rootGrid.Children.Add(_textBlock);

            Content = _rootGrid;

            SizeChanged += OnSizeChanged;
            _textBlock.SizeChanged += OnTextBlockSizeChanged;
        }

        protected override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            ApplyProperties();
        }

        private void ApplyProperties()
        {
            _textBlock.Text = Text;
            _textBlock.FontSize = FontSize;
            _textBlock.FontFamily = FontFamily;
            _textBlock.FontWeight = FontWeight;
            _textBlock.TextTrimming = TextTrimming;
            if (Foreground != null)
                _textBlock.Foreground = Foreground;
        }

        private static void OnPaddingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is MarqueeTextBlock m)
                m._rootGrid.Padding = (Thickness)e.NewValue;
        }

        private static void OnForegroundChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is MarqueeTextBlock m && e.NewValue is Brush b)
                m._textBlock.Foreground = b;
        }

        private static void OnTextOrFontChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is MarqueeTextBlock m)
            {
                m.ApplyProperties();
                m.CheckOverflow();
            }
        }

        private static void OnMarqueeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is MarqueeTextBlock m)
            {
                if (m.Marquee)
                    m.CheckOverflow();
                else
                    m.StopMarquee();
            }
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            CheckOverflow();
        }

        private void OnTextBlockSizeChanged(object sender, SizeChangedEventArgs e)
        {
            CheckOverflow();
        }

        private void CheckOverflow()
        {
            if (ActualWidth <= 0 || _textBlock.ActualWidth <= 0)
                return;

            bool overflow = _textBlock.ActualWidth > ActualWidth + 1;
            if (overflow && Marquee)
                StartMarquee();
            else
                StopMarquee();
        }

        public void StartMarquee()
        {
            _needsMarquee = true;
            _offset = 0;
            _textBlock.Margin = new Thickness(0);

            if (_marqueeTimer == null)
            {
                _marqueeTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(16)
                };
                _marqueeTimer.Tick += OnMarqueeTick;
            }
            _marqueeTimer.Start();
        }

        public void StopMarquee()
        {
            _needsMarquee = false;
            _marqueeTimer?.Stop();
            _textBlock.Margin = new Thickness(0);
        }

        private void OnMarqueeTick(object sender, object e)
        {
            if (ActualWidth <= 0 || _textBlock.ActualWidth <= ActualWidth + 1)
            {
                StopMarquee();
                return;
            }

            double maxOffset = _textBlock.ActualWidth - ActualWidth + 16;
            _offset += MarqueeSpeed / 60.0;

            if (_offset > maxOffset)
                _offset = 0;

            _textBlock.Margin = new Thickness(-_offset, 0, 0, 0);
        }
    }
}
