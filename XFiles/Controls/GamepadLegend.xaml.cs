using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Imaging;

namespace XFiles.Controls
{
    public sealed partial class GamepadLegend : UserControl
    {
        private const string BtnBase = "ms-appx:///Assets/GamepadButtons/";

        public GamepadLegend()
        {
            this.InitializeComponent();
            LoadIcons();
        }

        private void LoadIcons()
        {
            IconA.Source = new BitmapImage(new System.Uri(BtnBase + "abxy/a.png"));
            IconB.Source = new BitmapImage(new System.Uri(BtnBase + "abxy/b.png"));
            IconX.Source = new BitmapImage(new System.Uri(BtnBase + "abxy/x.png"));
            IconY.Source = new BitmapImage(new System.Uri(BtnBase + "abxy/y.png"));
            IconLB.Source = new BitmapImage(new System.Uri(BtnBase + "lr/button_xbox_digital_bumper_light_1.png"));
            IconRB.Source = new BitmapImage(new System.Uri(BtnBase + "lr/button_xbox_digital_bumper_light_2.png"));
            IconDpad.Source = new BitmapImage(new System.Uri(BtnBase + "dpads/dpad-all-directions.png"));
        }

        public void SetContextLabels(string xLabel, string yLabel, string lbLabel = "", string rbLabel = "")
        {
            LabelX.Text = xLabel;
            LabelY.Text = yLabel;
            LabelLB.Text = lbLabel;
            LabelRB.Text = rbLabel;

            if (!string.IsNullOrEmpty(lbLabel) || !string.IsNullOrEmpty(rbLabel))
            {
                // Show LB/RB labels when set
            }
        }

        public void ShowDpad(bool show)
        {
            DpadPanel.Visibility = show ? Windows.UI.Xaml.Visibility.Visible : Windows.UI.Xaml.Visibility.Collapsed;
        }
    }
}
