namespace FlowIME.Core.Automation;

public sealed record AutomationRuntimeSnapshot(
    bool Started,
    bool ExecutionEnabled,
    long Generation,
    bool OperationInFlight,
    string? LastErrorType,
    string? LastErrorMessage,
    int DecisionJournalCount = 0,
    string? LastDecisionOutcome = null,
    string? LastDecisionReason = null);
