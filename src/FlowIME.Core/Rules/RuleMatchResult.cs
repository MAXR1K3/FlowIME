using FlowIME.Core.Models;

namespace FlowIME.Core.Rules;

public sealed record RuleMatchResult(
    ApplicationRule? Rule,
    InputAction Action,
    bool IsMatch)
{
    public static RuleMatchResult NoMatch { get; } =
        new(null, InputAction.Keep, false);
}
