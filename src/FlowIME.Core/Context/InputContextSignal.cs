namespace FlowIME.Core.Context;

public enum InputContextSignalKind
{
    Application,
    Fullscreen,
    Game,
    TextInput,
    BrowserAddressBar,
    GameTextEntry
}

public enum ContextSignalConfidence
{
    Low,
    Medium,
    High,
    Certain
}

/// <summary>
/// One active contextual fact about the foreground interaction. Signals contain
/// classification only; detectors must not copy typed text, URLs, or document data.
/// </summary>
public sealed record InputContextSignal(
    InputContextSignalKind Kind,
    string Source,
    ContextSignalConfidence Confidence = ContextSignalConfidence.High);
