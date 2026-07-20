namespace XFiles.Navigation
{
    /// <summary>
    /// Semantic navigation contract. Each "screen" or focused component implements
    /// this to receive gamepad input as semantic events instead of raw button state.
    /// </summary>
    public interface INavigable
    {
        bool IsMediaFullscreen { get; }
        void OnDPadUp();
        void OnDPadDown();
        void OnDPadLeft();
        void OnDPadRight();
        void OnConfirm();
        void OnBack();
        void OnContextMenu();
        void OnRefresh();
        void OnSettings();
        void OnPageUp();
        void OnPageDown();
        void OnSeekBack();
        void OnSeekForward();
        void OnSeekRepeat(int seconds);
        void OnTriggerHeld(float leftTrigger, float rightTrigger);
        void OnLeftStickMove(float x, float y);
        void OnRightStickMove(float x, float y);
        void OnScrollHorizontal(double delta);
        void OnScrollVertical(double delta);
    }
}
