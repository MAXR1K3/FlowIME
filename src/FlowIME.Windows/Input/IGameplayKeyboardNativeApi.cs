namespace FlowIME.Windows.Input;

internal interface IGameplayKeyboardNativeApi
{
    IReadOnlyList<nint> GetKeyboardLayouts();

    nint GetKeyboardLayout(uint threadId);

    bool RequestInputLanguageChange(
        nint hwnd,
        nint keyboardLayout,
        out int errorCode);
}
