namespace FlowIME.Windows.Input;

public sealed record ImeConversionModeResult(
    bool Success,
    uint ConversionMode,
    nint ImeWindow,
    int NativeError,
    string? Error)
{
    public bool IsNative => (ConversionMode & LegacyImeConversionModeAccessor.ImeCmodeNative) != 0;
}
