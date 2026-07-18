using Windows.UI.Xaml.Controls;

namespace XFiles
{
    public sealed partial class MainPage : Page
    {
        public MainPage()
        {
            this.InitializeComponent();
            Frame.Navigate(typeof(Controls.MillerColumnsPage));
        }
    }
}
