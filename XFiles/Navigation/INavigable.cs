namespace XFiles.Navigation
{
    /// <summary>
    /// Semantic navigation contract. Each "screen" or focused component implements
    /// this to receive gamepad input as semantic events instead of raw button state.
    /// </summary>
    public interface INavigable
    {
        void OnDPadUp();
        void OnDPadDown();
        void OnDPadLeft();
        void OnDPadRight();
        void OnConfirm();
        void OnBack();
        void OnContextMenu();
        void OnPageUp();
        void OnPageDown();
        void OnScrollHorizontal(double delta);
        void OnScrollVertical(double delta);
    }
}
