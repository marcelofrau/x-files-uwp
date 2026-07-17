using System;
using System.Threading.Tasks;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;

namespace XFiles.Controls
{
    public sealed partial class DebugOverlay : UserControl
    {
        private readonly ScreenLogger _sink;
        private string _pendingText = "";
        private bool _dirty;

        public DebugOverlay(ScreenLogger sink)
        {
            _sink = sink;
            this.InitializeComponent();
            _sink.OnLogLine += OnLogLine;
        }

        private async void OnLogLine(string line)
        {
            _dirty = true;
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                if (!_dirty) return;
                _dirty = false;
                LogText.Text = string.Join("\n", _sink.GetLines());
                Scroller.ChangeView(null, Scroller.ExtentHeight, null);
            });
        }
    }
}
