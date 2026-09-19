namespace FlowIME.Core.Rules;

/// <summary>
/// Atomically observed rule configuration. Consumers that decide behavior must
/// use one snapshot so an application-rule edit cannot race a global-default read.
/// </summary>
public sealed record RuleConfigurationSnapshot(
    IReadOnlyList<ApplicationRule> Rules,
    GlobalDefaultTarget? GlobalDefault);
