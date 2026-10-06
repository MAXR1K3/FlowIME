using FlowIME.Core.Models;

namespace FlowIME.Core.Rules;

public enum RuleResolutionSource
{
    None,
    ContextPolicy,
    ApplicationRule,
    GlobalDefault
}

/// <summary>
/// Final rule-layer decision for one foreground application. Application rules
/// always win over the global default, including an explicit Keep action.
/// </summary>
public sealed record RuleResolution(
    RuleResolutionSource Source,
    ApplicationRule? Rule,
    GlobalDefaultTarget? GlobalDefault,
    string? ProviderId,
    InputAction Action)
{
    public bool HasTarget => Source != RuleResolutionSource.None;

    public static RuleResolution None { get; } =
        new(RuleResolutionSource.None, null, null, null, InputAction.Keep);

    public static RuleResolution FromApplicationRule(ApplicationRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        return new RuleResolution(
            RuleResolutionSource.ApplicationRule,
            rule,
            null,
            ResolveProviderId(rule.Action, rule.ProviderId),
            rule.Action);
    }

    public static RuleResolution FromGlobalDefault(GlobalDefaultTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var normalized = target.Normalize();
        return new RuleResolution(
            RuleResolutionSource.GlobalDefault,
            null,
            normalized,
            ResolveProviderId(normalized.Action, normalized.ProviderId),
            normalized.Action);
    }

    private static string? ResolveProviderId(InputAction action, string? providerId) =>
        action is InputAction.Keep or InputAction.StandardUsKeyboard
            ? null
            : InputMethodProviderIds.Normalize(providerId);
}
