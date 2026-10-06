namespace FlowIME.Windows.Input;

internal interface IGameplayKeyboardNativeApi
{
    IReadOnlyList<nint> GetKeyboardLayouts();

    nint GetKeyboardLayout(uint threadId);

    uint GetWindowThreadId(
        nint hwnd,
        out uint processId,
        out int errorCode);

    bool RequestInputLanguageChange(
        nint hwnd,
        nint keyboardLayout,
        out int errorCode);
}
