namespace FlowIME.Windows.Context;

internal interface IWindowPresentationProbe
{
    WindowPresentationSnapshot Capture(nint hwnd);
}
