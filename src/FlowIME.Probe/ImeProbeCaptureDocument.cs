namespace FlowIME.Probe;

internal sealed record ImeProbeCaptureDocument(
    int SchemaVersion,
    DateTimeOffset CapturedAtUtc,
    string Label,
    ImeProbeWindowIdentity Window,
    IReadOnlyList<ImeProbeSample> Samples);

internal sealed record ImeProbeWindowIdentity(
    string ProcessName,
    uint ProcessId,
    string? ExecutablePath,
    string WindowClass,
    string? PackageFamilyName,
    string TopLevelHwnd);

internal sealed record ImeProbeSample(
    DateTimeOffset CapturedAtUtc,
    bool TargetIsForeground,
    string ForegroundHwnd,
    ImeProbeFocusSnapshot Focus,
    string KeyboardLayout,
    ImeProbeOpenStatusSnapshot OpenStatus,
    ImeProbeConversionSnapshot ConversionMode,
    ImeProbeProfileSnapshot ActiveProfile);

internal sealed record ImeProbeFocusSnapshot(
    uint ThreadId,
    string ActiveHwnd,
    string FocusHwnd,
    string CaretHwnd,
    string EffectiveInputHwnd,
    string Source,
    bool GuiThreadInfoAvailable,
    int NativeError,
    string? Error);

internal sealed record ImeProbeOpenStatusSnapshot(
    bool Success,
    bool? IsOpen,
    string ImeWindow,
    int NativeError,
    string? Error);

internal sealed record ImeProbeConversionSnapshot(
    bool Success,
    uint? ConversionMode,
    bool? IsNative,
    string ImeWindow,
    int NativeError,
    string? Error);

internal sealed record ImeProbeProfileSnapshot(
    bool Success,
    int HResult,
    uint ProfileType,
    ushort LanguageId,
    Guid Clsid,
    Guid ProfileGuid,
    Guid CategoryId,
    string KeyboardLayout,
    string SubstituteKeyboardLayout,
    uint Capabilities,
    uint Flags,
    string? Error);
