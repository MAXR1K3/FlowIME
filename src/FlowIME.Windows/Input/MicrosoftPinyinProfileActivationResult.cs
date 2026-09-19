namespace FlowIME.Windows.Input;

internal sealed record MicrosoftPinyinProfileActivationResult(
    bool Success,
    int HResult,
    bool VerifiedActive,
    bool WasAlreadyActive,
    string? Error);
