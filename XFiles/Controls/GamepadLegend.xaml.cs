using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Imaging;

namespace XFiles.Controls
{
    public sealed partial class GamepadLegend : UserControl
    {
        private const string Theme = "xbox-dark";

        public GamepadLegend()
        {
            this.InitializeComponent();
            LoadIcons();
        }

        private void LoadIcons()
        {
            IconA.Source = new BitmapImage(new System.Uri($"ms-appx:///Assets/GamepadButtons/{Theme}/a-1.png"));
            IconB.Source = new BitmapImage(new System.Uri($"ms-appx:///Assets/GamepadButtons/{Theme}/b-1.png"));
            IconX.Source = new BitmapImage(new System.Uri($"ms-appx:///Assets/GamepadButtons/{Theme}/x-1.png"));
            IconY.Source = new BitmapImage(new System.Uri($"ms-appx:///Assets/GamepadButtons/{Theme}/y-1.png"));
            IconLB.Source = new BitmapImage(new System.Uri($"ms-appx:///Assets/GamepadButtons/{Theme}/lb.png"));
            IconRB.Source = new BitmapImage(new System.Uri($"ms-appx:///Assets/GamepadButtons/{Theme}/rb.png"));
            IconDpad.Source = new BitmapImage(new System.Uri($"ms-appx:///Assets/GamepadButtons/{Theme}/dpad-1.png"));
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
