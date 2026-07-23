using System;
using System.Collections.Generic;
using System.ComponentModel;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using XFiles.FileSystem;

namespace XFiles.Controls
{
    public sealed partial class VideoTrackMenu : UserControl
    {
        public event EventHandler<SubtitleTrack> SubtitleSelected;
        public event EventHandler<int> AudioTrackSelected;

        private enum MenuView { Options, Subtitles, AudioTracks }
        private MenuView _currentView = MenuView.Options;

        private List<OptionItem> _optionItems;
        private List<SubtitleTrackItem> _subtitleItems;
        private List<AudioTrackItem> _audioItems;
        private int _selectedSubtitleIndex = -1;
        private int _selectedAudioIndex = -1;
        private int _activeSubtitleIndex = -1;
        private int _activeAudioIndex = -1;
        private bool _hasSubtitles;
        private bool _hasAudio;

        public VideoTrackMenu()
        {
            this.InitializeComponent();
        }

        public bool IsOpen => Visibility == Visibility.Visible;

        public void Show(List<SubtitleTrack> subtitles, List<AudioTrackInfo> audioTracks,
            int currentSubtitleIndex, int currentAudioIndex)
        {
            _selectedSubtitleIndex = currentSubtitleIndex;
            _selectedAudioIndex = currentAudioIndex;
            _activeSubtitleIndex = currentSubtitleIndex;
            _activeAudioIndex = currentAudioIndex;

            _hasSubtitles = subtitles != null && subtitles.Count > 0;
            _hasAudio = audioTracks != null && audioTracks.Count > 0;

            if (!_hasSubtitles && !_hasAudio) return;

            _optionItems = new List<OptionItem>();
            if (_hasAudio)
                _optionItems.Add(new OptionItem { Label = "Audio Tracks", IconPath = "ms-appx:///Assets/Views/VideoTrackMenu/videotrackmenu-audio-20.png" });
            if (_hasSubtitles)
                _optionItems.Add(new OptionItem { Label = "Subtitles", IconPath = "ms-appx:///Assets/Views/VideoTrackMenu/videotrackmenu-subtitles-20.png" });

            _subtitleItems = new List<SubtitleTrackItem>();
            if (_hasSubtitles)
            {
                for (int i = 0; i < subtitles.Count; i++)
                {
                    _subtitleItems.Add(new SubtitleTrackItem
                    {
                        Track = subtitles[i],
                        DisplayName = subtitles[i].GetDisplayName(),
                    });
                }
                RefreshSubtitleIndicators();
            }

            _audioItems = new List<AudioTrackItem>();
            if (_hasAudio)
            {
                for (int i = 0; i < audioTracks.Count; i++)
                {
                    _audioItems.Add(new AudioTrackItem
                    {
                        Track = audioTracks[i],
                        DisplayName = audioTracks[i].DisplayName,
                    });
                }
                RefreshAudioIndicators();
            }

            _currentView = MenuView.Options;
            ShowOptionsView();

            Visibility = Visibility.Visible;
            Overlay.Visibility = Visibility.Visible;

            OptionsList.ItemsSource = _optionItems;
            OptionsList.SelectedIndex = 0;
            OptionsList.Focus(FocusState.Programmatic);
        }

        public void Close()
        {
            _currentView = MenuView.Options;
            Overlay.Visibility = Visibility.Collapsed;
            Visibility = Visibility.Collapsed;
        }

        public bool HandleButton(VirtualKey key)
        {
            if (!IsOpen) return false;

            switch (key)
            {
                case VirtualKey.GamepadB:
                case VirtualKey.Escape:
                    if (_currentView == MenuView.Options)
                    {
                        Close();
                    }
                    else
                    {
                        _currentView = MenuView.Options;
                        ShowOptionsView();
                        OptionsList.Focus(FocusState.Programmatic);
                    }
                    return true;

                case VirtualKey.GamepadA:
                case VirtualKey.Enter:
                    if (_currentView == MenuView.Options)
                    {
                        if (OptionsList.SelectedIndex < 0) return true;
                        var selected = _optionItems[OptionsList.SelectedIndex];
                        if (selected.Label == "Audio Tracks" && _hasAudio)
                        {
                            _currentView = MenuView.AudioTracks;
                            ShowAudioView();
                            AudioList.Focus(FocusState.Programmatic);
                        }
                        else if (selected.Label == "Subtitles" && _hasSubtitles)
                        {
                            _currentView = MenuView.Subtitles;
                            ShowSubtitlesView();
                            SubtitleList.Focus(FocusState.Programmatic);
                        }
                    }
                    else if (_currentView == MenuView.Subtitles)
                    {
                        if (SubtitleList.SelectedIndex >= 0)
                        {
                            _activeSubtitleIndex = SubtitleList.SelectedIndex;
                            RefreshSubtitleIndicators();
                            SubtitleSelected?.Invoke(this, _subtitleItems[SubtitleList.SelectedIndex].Track);
                        }
                        Close();
                    }
                    else if (_currentView == MenuView.AudioTracks)
                    {
                        if (AudioList.SelectedIndex >= 0)
                        {
                            _activeAudioIndex = AudioList.SelectedIndex;
                            RefreshAudioIndicators();
                            AudioTrackSelected?.Invoke(this, _audioItems[AudioList.SelectedIndex].Track.Index);
                        }
                        Close();
                    }
                    return true;

                case VirtualKey.GamepadDPadUp:
                case VirtualKey.Up:
                    if (_currentView == MenuView.Options)
                    {
                        if (OptionsList.SelectedIndex > 0)
                            OptionsList.SelectedIndex--;
                    }
                    else if (_currentView == MenuView.Subtitles)
                    {
                        if (SubtitleList.SelectedIndex > 0)
                        {
                            SubtitleList.SelectedIndex--;
                            SubtitleList.ScrollIntoView(SubtitleList.SelectedItem);
                        }
                    }
                    else if (_currentView == MenuView.AudioTracks)
                    {
                        if (AudioList.SelectedIndex > 0)
                        {
                            AudioList.SelectedIndex--;
                            AudioList.ScrollIntoView(AudioList.SelectedItem);
                        }
                    }
                    return true;

                case VirtualKey.GamepadDPadDown:
                case VirtualKey.Down:
                    if (_currentView == MenuView.Options)
                    {
                        if (OptionsList.SelectedIndex < _optionItems.Count - 1)
                            OptionsList.SelectedIndex++;
                    }
                    else if (_currentView == MenuView.Subtitles)
                    {
                        if (SubtitleList.SelectedIndex < _subtitleItems.Count - 1)
                        {
                            SubtitleList.SelectedIndex++;
                            SubtitleList.ScrollIntoView(SubtitleList.SelectedItem);
                        }
                    }
                    else if (_currentView == MenuView.AudioTracks)
                    {
                        if (AudioList.SelectedIndex < _audioItems.Count - 1)
                        {
                            AudioList.SelectedIndex++;
                            AudioList.ScrollIntoView(AudioList.SelectedItem);
                        }
                    }
                    return true;

                case VirtualKey.GamepadDPadLeft:
                case VirtualKey.Left:
                    if (_currentView != MenuView.Options)
                    {
                        _currentView = MenuView.Options;
                        ShowOptionsView();
                        OptionsList.Focus(FocusState.Programmatic);
                    }
                    return true;

                case VirtualKey.GamepadDPadRight:
                case VirtualKey.Right:
                    if (_currentView == MenuView.Options)
                    {
                        return HandleButton(VirtualKey.GamepadA);
                    }
                    return true;
            }
            return false;
        }

        private void ShowOptionsView()
        {
            OptionsPanel.Visibility = Visibility.Visible;
            SubtitlesPanel.Visibility = Visibility.Collapsed;
            AudioPanel.Visibility = Visibility.Collapsed;
            BackHintText.Text = "Select";
        }

        private void ShowSubtitlesView()
        {
            OptionsPanel.Visibility = Visibility.Collapsed;
            SubtitlesPanel.Visibility = Visibility.Visible;
            AudioPanel.Visibility = Visibility.Collapsed;
            SubtitleList.ItemsSource = _subtitleItems;
            SubtitleList.SelectedIndex = _selectedSubtitleIndex >= 0 ? _selectedSubtitleIndex : 0;
            BackHintText.Text = "Back";
        }

        private void ShowAudioView()
        {
            OptionsPanel.Visibility = Visibility.Collapsed;
            SubtitlesPanel.Visibility = Visibility.Collapsed;
            AudioPanel.Visibility = Visibility.Visible;
            AudioList.ItemsSource = _audioItems;
            AudioList.SelectedIndex = _selectedAudioIndex >= 0 ? _selectedAudioIndex : 0;
            BackHintText.Text = "Back";
        }

        private void RefreshSubtitleIndicators()
        {
            if (_subtitleItems == null) return;
            for (int i = 0; i < _subtitleItems.Count; i++)
            {
                bool active = (i == _activeSubtitleIndex);
                _subtitleItems[i].IndicatorIcon = active ? "ms-appx:///Assets/Views/VideoTrackMenu/videotrackmenu-checkmark-20.png" : "";
                _subtitleItems[i].IsActive = active ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void RefreshAudioIndicators()
        {
            if (_audioItems == null) return;
            for (int i = 0; i < _audioItems.Count; i++)
            {
                bool active = (i == _activeAudioIndex);
                _audioItems[i].IndicatorIcon = active ? "ms-appx:///Assets/Views/VideoTrackMenu/videotrackmenu-checkmark-20.png" : "";
                _audioItems[i].IsActive = active ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void OnOptionSelectionChanged(object sender, SelectionChangedEventArgs e) { }

        private void OnSubtitleSelectionChanged(object sender, SelectionChangedEventArgs e) { }

        private void OnAudioSelectionChanged(object sender, SelectionChangedEventArgs e) { }

        private void OnSubtitleContainerChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (args.ItemContainer is ListViewItem container)
            {
                var item = args.Item as SubtitleTrackItem;
                if (item != null)
                {
                    int itemIndex = _subtitleItems.IndexOf(item);
                    bool active = (itemIndex == _activeSubtitleIndex);
                    container.Opacity = active ? 1.0 : 0.7;
                }
            }
        }

        private void OnAudioContainerChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (args.ItemContainer is ListViewItem container)
            {
                var item = args.Item as AudioTrackItem;
                if (item != null)
                {
                    int itemIndex = _audioItems.IndexOf(item);
                    bool active = (itemIndex == _activeAudioIndex);
                    container.Opacity = active ? 1.0 : 0.7;
                }
            }
        }

        public class OptionItem
        {
            public string Label { get; set; }
            public string IconPath { get; set; }
        }

        public class SubtitleTrackItem : INotifyPropertyChanged
        {
            public SubtitleTrack Track { get; set; }
            public string DisplayName { get; set; }
            private string _indicatorIcon;
            public string IndicatorIcon
            {
                get => _indicatorIcon;
                set { _indicatorIcon = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IndicatorIcon))); }
            }
            private Visibility _isActive = Visibility.Collapsed;
            public Visibility IsActive
            {
                get => _isActive;
                set { _isActive = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsActive))); }
            }
            public event PropertyChangedEventHandler PropertyChanged;
        }

        public class AudioTrackItem : INotifyPropertyChanged
        {
            public AudioTrackInfo Track { get; set; }
            public string DisplayName { get; set; }
            private string _indicatorIcon;
            public string IndicatorIcon
            {
                get => _indicatorIcon;
                set { _indicatorIcon = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IndicatorIcon))); }
            }
            private Visibility _isActive = Visibility.Collapsed;
            public Visibility IsActive
            {
                get => _isActive;
                set { _isActive = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsActive))); }
            }
            public event PropertyChangedEventHandler PropertyChanged;
        }
    }
}
