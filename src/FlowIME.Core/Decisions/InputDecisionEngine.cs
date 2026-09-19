using System.Diagnostics;
using FlowIME.Core.Context;
using FlowIME.Core.Models;
using FlowIME.Core.Rules;

namespace FlowIME.Core.Decisions;

/// <summary>
/// Deterministic decision pipeline.
///
/// Priority contract:
/// 1. explicit context policies;
/// 2. application rules;
/// 3. global default;
/// 4. no target.
///
/// Manual override is intentionally not a policy: it is a runtime suppression
/// gate after resolution so it can temporarily veto any automatic target without
/// altering persisted rules.
/// </summary>
public sealed class InputDecisionEngine : IInputDecisionEngine
{
    private readonly IRuleEngine _ruleEngine;
    private readonly IReadOnlyList<IInputContextPolicy> _contextPolicies;

    public InputDecisionEngine(
        IRuleEngine ruleEngine,
        IEnumerable<IInputContextPolicy>? contextPolicies = null)
    {
        _ruleEngine = ruleEngine ?? throw new ArgumentNullException(nameof(ruleEngine));
        var configured = (contextPolicies ?? Array.Empty<IInputContextPolicy>()).ToArray();
        ValidatePolicyIds(configured);
        _contextPolicies = configured
            .OrderByDescending(policy => policy.Priority)
            .ThenBy(policy => policy.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private static void ValidatePolicyIds(IReadOnlyList<IInputContextPolicy> policies)
    {
        foreach (var policy in policies)
        {
            ArgumentNullException.ThrowIfNull(policy);
            if (string.IsNullOrWhiteSpace(policy.Id))
            {
                throw new ArgumentException(
                    "Context policy IDs must be non-empty.",
                    nameof(policies));
            }
        }

        var duplicate = policies
            .GroupBy(policy => policy.Id, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new ArgumentException(
                $"Duplicate context policy ID '{duplicate.Key}'.",
                nameof(policies));
        }
    }

    public InputDecision Resolve(
        InputContextSnapshot context,
        RuleConfigurationSnapshot configuration)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(configuration);

        Candidate? best = null;
        foreach (var policy in _contextPolicies)
        {
            ContextPolicyMatch? match;
            try
            {
                match = policy.Evaluate(context);
            }
            catch (Exception ex)
            {
                Trace.WriteLine(
                    $"[FlowIME.Decision] utc={DateTimeOffset.UtcNow:O} " +
                    $"policy={policy.Id} result=failed type={ex.GetType().Name}");
                continue;
            }

            if (match is null)
            {
                continue;
            }

            if (match.Action is InputAction.Chinese or InputAction.English &&
                string.IsNullOrWhiteSpace(match.ProviderId))
            {
                Trace.WriteLine(
                    $"[FlowIME.Decision] utc={DateTimeOffset.UtcNow:O} " +
                    $"policy={policy.Id} result=invalid-match reason=missing-provider");
                continue;
            }

            var candidate = new Candidate(policy, match);
            if (best is null || IsBetter(candidate, best))
            {
                best = candidate;
            }
        }

        if (best is not null)
        {
            var match = best.Match;
            return new InputDecision(
                InputDecisionSource.ContextPolicy,
                InputDecisionReasonCode.ContextPolicy,
                string.IsNullOrWhiteSpace(match.Reason)
                    ? "context-policy"
                    : match.Reason,
                match.Action == InputAction.Keep
                    ? null
                    : InputMethodProviderIds.Normalize(match.ProviderId),
                match.Action,
                ContextPolicyId: best.Policy.Id);
        }

        var legacy = _ruleEngine.Resolve(
            context.Window,
            configuration.Rules,
            configuration.GlobalDefault);

        return legacy.Source switch
        {
            RuleResolutionSource.ApplicationRule when legacy.Rule is not null =>
                new InputDecision(
                    InputDecisionSource.ApplicationRule,
                    InputDecisionReasonCode.ApplicationRule,
                    "application-rule",
                    InputMethodProviderIds.Normalize(legacy.ProviderId),
                    legacy.Action,
                    ApplicationRule: legacy.Rule),

            RuleResolutionSource.GlobalDefault when legacy.GlobalDefault is not null =>
                new InputDecision(
                    InputDecisionSource.GlobalDefault,
                    InputDecisionReasonCode.GlobalDefault,
                    "global-default",
                    InputMethodProviderIds.Normalize(legacy.ProviderId),
                    legacy.Action,
                    GlobalDefault: legacy.GlobalDefault),

            _ => InputDecision.None
        };
    }

    private static bool IsBetter(Candidate candidate, Candidate current)
    {
        var priority = candidate.Policy.Priority.CompareTo(current.Policy.Priority);
        if (priority != 0)
        {
            return priority > 0;
        }

        var specificity = candidate.Match.Specificity.CompareTo(current.Match.Specificity);
        if (specificity != 0)
        {
            return specificity > 0;
        }

        return StringComparer.Ordinal.Compare(candidate.Policy.Id, current.Policy.Id) < 0;
    }

    private sealed record Candidate(
        IInputContextPolicy Policy,
        ContextPolicyMatch Match);
}
