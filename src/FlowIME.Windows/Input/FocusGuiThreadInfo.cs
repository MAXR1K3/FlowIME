namespace FlowIME.Windows.Input;

internal readonly record struct FocusGuiThreadInfo(
    nint ActiveWindow,
    nint FocusWindow,
    nint CaretWindow,
    uint Flags);
