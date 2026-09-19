using FlowIME.Core.Models;

namespace FlowIME.Core.Context;

public enum InputContextTrigger
{
    ForegroundChanged,
    FocusChanged,
    ManualRefresh,
    SystemRecovery,
    GameplayExit,
    GameTextEntryChanged,
    RuleChanged
}

public sealed record ContextDetectionRequest(
    WindowContext Window,
    InputContextTrigger Trigger,
    nint FocusHwnd,
    DateTimeOffset Timestamp,
    int FocusObjectId = 0,
    int FocusChildId = 0);

public sealed record InputContextSnapshot(
    WindowContext Window,
    ApplicationIdentity Application,
    InputContextTrigger Trigger,
    nint FocusHwnd,
    DateTimeOffset Timestamp,
    IReadOnlyList<InputContextSignal> Signals)
{
    public bool HasSignal(InputContextSignalKind kind) =>
        Signals.Any(signal => signal.Kind == kind);
}
