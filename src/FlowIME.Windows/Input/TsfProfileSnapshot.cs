namespace FlowIME.Windows.Input;

public sealed record TsfProfileSnapshot(
    bool Success,
    int HResult,
    uint ProfileType,
    ushort LanguageId,
    Guid Clsid,
    Guid ProfileGuid,
    Guid CategoryId,
    nint SubstituteKeyboardLayout,
    uint Capabilities,
    nint KeyboardLayout,
    uint Flags,
    string? Error);
