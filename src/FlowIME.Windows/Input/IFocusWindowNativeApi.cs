namespace FlowIME.Windows.Input;

internal interface IFocusWindowNativeApi
{
    uint GetWindowThreadId(nint hwnd, out int nativeError);

    bool TryGetGuiThreadInfo(
        uint threadId,
        out FocusGuiThreadInfo info,
        out int nativeError);
}
