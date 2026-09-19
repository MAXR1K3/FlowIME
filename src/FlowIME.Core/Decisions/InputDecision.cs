using FlowIME.Core.Models;
using FlowIME.Core.Rules;

namespace FlowIME.Core.Decisions;

public enum InputDecisionSource
{
    None,
    ContextPolicy,
    ApplicationRule,
    GlobalDefault
}

public enum InputDecisionReasonCode
{
    NoTarget,
    ContextPolicy,
    ApplicationRule,
    GlobalDefault
}

/// <summary>
/// Final target selected by the decision layer. The decision says what should
/// happen; automation still owns cancellation, manual-override suppression and
/// the actual backend mutation.
/// </summary>
public sealed record InputDecision(
    InputDecisionSource Source,
    InputDecisionReasonCode ReasonCode,
    string Reason,
    string? ProviderId,
    InputAction Action,
    ApplicationRule? ApplicationRule = null,
    GlobalDefaultTarget? GlobalDefault = null,
    string? ContextPolicyId = null)
{
    public bool HasTarget => Source != InputDecisionSource.None;

    public static InputDecision None { get; } =
        new(
            InputDecisionSource.None,
            InputDecisionReasonCode.NoTarget,
            "no-target",
            null,
            InputAction.Keep);
}
