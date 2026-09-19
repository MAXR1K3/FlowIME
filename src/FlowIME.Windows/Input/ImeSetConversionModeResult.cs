namespace FlowIME.Windows.Input;

public sealed record ImeSetConversionModeResult(
    bool Success,
    uint RequestedConversionMode,
    nint ImeWindow,
    int NativeError,
    string? Error);
