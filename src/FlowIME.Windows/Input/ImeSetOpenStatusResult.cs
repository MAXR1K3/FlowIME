namespace FlowIME.Windows.Input;

public sealed record ImeSetOpenStatusResult(
    bool Success,
    bool RequestedOpen,
    nint ImeWindow,
    int NativeError,
    string? Error);
