namespace FlowIME.Windows.Input;

public sealed record TsfCompartmentSnapshot(
    bool Success,
    uint ClientId,
    int? KeyboardOpenClose,
    int? InputModeConversion,
    string Scope,
    string? Error);
