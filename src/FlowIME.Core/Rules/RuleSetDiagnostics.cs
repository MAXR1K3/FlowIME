namespace FlowIME.Core.Rules;

/// <summary>
/// Lightweight health summary for enabled application rules. FlowIME's normal
/// UI prevents duplicate executable rules, but manual JSON edits and legacy
/// configurations can still create exact-overlap groups. These diagnostics are
/// observational only; they never change rule resolution semantics.
/// </summary>
public sealed record RuleSetDiagnostics(
    int EnabledRuleCount,
    int ExactOverlapGroupCount,
    int ConflictingTargetGroupCount,
    int RedundantTargetGroupCount,
    int AffectedRuleCount)
{
    public int ShadowedRulePairCount { get; init; }

    public int EqualPriorityCompetitionPairCount { get; init; }

    public bool HasIssues =>
        ExactOverlapGroupCount > 0 ||
        ShadowedRulePairCount > 0 ||
        EqualPriorityCompetitionPairCount > 0;

    public static RuleSetDiagnostics Empty { get; } = new(0, 0, 0, 0, 0);
}
