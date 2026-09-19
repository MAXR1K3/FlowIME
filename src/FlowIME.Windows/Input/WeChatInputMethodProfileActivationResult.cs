namespace FlowIME.Windows.Input;

internal sealed record WeChatInputMethodProfileActivationResult(
    bool Success,
    int HResult,
    bool VerifiedActive,
    bool WasAlreadyActive,
    string? Error);
