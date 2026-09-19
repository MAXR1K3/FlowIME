namespace FlowIME.Windows.Input;

public enum FocusWindowSource
{
    Focus,
    Caret,
    TopLevelFallback
}

public sealed record FocusWindowResult(
    nint TopLevelWindow,
    uint ThreadId,
    nint ActiveWindow,
    nint FocusWindow,
    nint CaretWindow,
    nint EffectiveInputWindow,
    FocusWindowSource Source,
    bool GuiThreadInfoAvailable,
    int NativeError,
    string? Error);
