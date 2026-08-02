using System;
using System.Collections.Generic;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;

namespace XFiles.Controls
{
    public class ControlsGuideRow
    {
        public string Icon { get; set; }
        public string Label { get; set; }
        public string Description { get; set; }
    }

    public class ControlsGuideSection
    {
        public string Title { get; set; }
        public List<ControlsGuideRow> Rows { get; set; }
    }

    public sealed partial class ControlsGuideOverlay : UserControl
    {
        private const string GB = "ms-appx:///Assets/GamepadButtons/";

        public ControlsGuideOverlay()
        {
            this.InitializeComponent();
        }

        public void Show()
        {
            Log.Info("ControlsGuideOverlay: showing guide");
            SectionsControl.ItemsSource = BuildSections();
            Visibility = Visibility.Visible;
        }

        public void Hide()
        {
            Log.Info("ControlsGuideOverlay: hiding guide");
            Visibility = Visibility.Collapsed;
            SectionsControl.ItemsSource = null;
        }

        public bool IsVisible => Visibility == Visibility.Visible;

        public void HandleButton(VirtualKey key)
        {
            if (key == VirtualKey.GamepadB || key == VirtualKey.Escape)
                Hide();
        }

        private const double StickDeadzone = 0.15;
        private const double ScrollSpeed = 26.0;

        public void HandleStick(float x, float y)
        {
            if (!IsVisible) return;
            if (Math.Abs(y) <= StickDeadzone) return;

            double delta = -y * ScrollSpeed;
            double newOffset = GuideScrollViewer.VerticalOffset + delta;
            if (newOffset < 0) newOffset = 0;
            double max = GuideScrollViewer.ScrollableHeight;
            if (newOffset > max) newOffset = max;
            GuideScrollViewer.ChangeView(null, newOffset, null, true);
        }

        private void OnBackdropTapped(object sender, TappedRoutedEventArgs e)
        {
            Hide();
        }

        private static ControlsGuideRow Row(string icon, string label, string description)
        {
            return new ControlsGuideRow { Icon = GB + icon, Label = label, Description = description };
        }

        private static List<ControlsGuideSection> BuildSections()
        {
            return new List<ControlsGuideSection>
            {
                new ControlsGuideSection
                {
                    Title = "File Browser",
                    Rows = new List<ControlsGuideRow>
                    {
                        Row("dpads/dpad-up-and-down.png", "D-Pad Up / Down", "Move selection (hold to repeat)"),
                        Row("dpads/dpad-left-and-right.png", "D-Pad Left / Right", "Parent folder / enter folder"),
                        Row("analog/button_xbox_analog_l_all-directions.png", "Left Stick", "Move selection (analog speed)"),
                        Row("abxy/a.png", "A", "Open folder or file"),
                        Row("abxy/b.png", "B", "Go back to parent"),
                        Row("abxy/y.png", "Y", "Context menu (copy, move, rename, delete, share)"),
                        Row("abxy/y.png", "Y (hold)", "Add / remove favorite"),
                        Row("abxy/x.png", "X", "Refresh folder · play audio or video fullscreen"),
                        Row("system/xboxl_view.png", "View", "Batch mode on / off"),
                        Row("system/xboxl_menu.png", "Start", "Start menu (search, favorites, logs, settings)"),
                        Row("lr/button_xbox_digital_bumper_light_1.png", "LB / RB", "Jump by first letter"),
                        Row("lr/button_xbox_analog_trigger_light_1.png", "LT / RT", "Page up / down (8 items)"),
                        Row("analog/button_xbox_analog_r_up-down.png", "Right Stick", "Scroll preview pane")
                    }
                },
                new ControlsGuideSection
                {
                    Title = "Batch Mode",
                    Rows = new List<ControlsGuideRow>
                    {
                        Row("abxy/a.png", "A", "Toggle item selection"),
                        Row("abxy/b.png", "B", "Exit batch mode"),
                        Row("abxy/y.png", "Y", "Batch actions (copy, move, delete, zip)"),
                        Row("abxy/x.png", "X", "Deselect all")
                    }
                },
                new ControlsGuideSection
                {
                    Title = "Audio Fullscreen",
                    Rows = new List<ControlsGuideRow>
                    {
                        Row("abxy/a.png", "A", "Play / pause"),
                        Row("abxy/b.png", "B", "Close"),
                        Row("analog/button_xbox_analog_l_up-down.png", "Sticks", "Volume"),
                        Row("lr/button_xbox_digital_bumper_light_1.png", "LB / RB", "Previous / next track"),
                        Row("system/xboxl_view.png", "View", "Cycle visualizer"),
                        Row("system/xboxl_view.png", "View (hold)", "Visualizer picker")
                    }
                },
                new ControlsGuideSection
                {
                    Title = "Video Fullscreen",
                    Rows = new List<ControlsGuideRow>
                    {
                        Row("abxy/a.png", "A", "Play / pause"),
                        Row("abxy/b.png", "B", "Close (first press hides controls)"),
                        Row("dpads/dpad-left-and-right.png", "D-Pad L / R", "Seek -5s / +5s"),
                        Row("analog/button_xbox_analog_l_up-down.png", "Sticks", "Volume"),
                        Row("lr/button_xbox_digital_bumper_light_1.png", "LB / RB", "Seek -5s / +5s"),
                        Row("lr/button_xbox_analog_trigger_light_1.png", "LT / RT", "Seek (continuous)"),
                        Row("system/xboxl_view.png", "View", "Audio / subtitle track menu")
                    }
                },
                new ControlsGuideSection
                {
                    Title = "Image Fullscreen",
                    Rows = new List<ControlsGuideRow>
                    {
                        Row("abxy/b.png", "B", "Close"),
                        Row("lr/button_xbox_analog_trigger_light_1.png", "LT / RT", "Zoom in / out"),
                        Row("dpads/dpad-all-directions.png", "D-Pad / Sticks", "Pan when zoomed")
                    }
                },
                new ControlsGuideSection
                {
                    Title = "PDF Fullscreen",
                    Rows = new List<ControlsGuideRow>
                    {
                        Row("abxy/b.png", "B", "Close"),
                        Row("lr/button_xbox_digital_bumper_light_1.png", "LB / RB", "Previous / next page"),
                        Row("lr/button_xbox_analog_trigger_light_1.png", "LT / RT", "Zoom in / out"),
                        Row("dpads/dpad-all-directions.png", "D-Pad / Sticks", "Pan when zoomed")
                    }
                },
                new ControlsGuideSection
                {
                    Title = "Text Editor",
                    Rows = new List<ControlsGuideRow>
                    {
                        Row("abxy/a.png", "A", "Virtual keyboard"),
                        Row("abxy/b.png", "B", "Close (first hides keyboard)"),
                        Row("abxy/x.png", "X", "Backspace"),
                        Row("abxy/y.png", "Y", "Newline"),
                        Row("system/xboxl_menu.png", "Start", "Save")
                    }
                },
                new ControlsGuideSection
                {
                    Title = "Visualizer Picker",
                    Rows = new List<ControlsGuideRow>
                    {
                        Row("dpads/dpad-up-and-down.png", "D-Pad Up / Down", "Choose visualizer"),
                        Row("abxy/a.png", "A", "Apply"),
                        Row("abxy/b.png", "B", "Close")
                    }
                },
                new ControlsGuideSection
                {
                    Title = "Media Player (Preview)",
                    Rows = new List<ControlsGuideRow>
                    {
                        Row("abxy/a.png", "A", "Play / pause"),
                        Row("abxy/b.png", "B", "Stop player"),
                        Row("lr/button_xbox_digital_bumper_light_1.png", "LB / RB", "Previous / next track"),
                        Row("analog/button_xbox_analog_l_up-down.png", "Sticks", "Volume")
                    }
                }
            };
        }
    }
}
