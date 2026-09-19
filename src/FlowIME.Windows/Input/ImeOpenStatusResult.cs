namespace FlowIME.Windows.Input;

public sealed record ImeOpenStatusResult(
    bool Success,
    bool IsOpen,
    nint ImeWindow,
    int NativeError,
    string? Error);
