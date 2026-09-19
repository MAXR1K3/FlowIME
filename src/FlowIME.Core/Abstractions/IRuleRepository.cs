using FlowIME.Core.Rules;

namespace FlowIME.Core.Abstractions;

public interface IRuleRepository
{
    ValueTask<RuleConfigurationSnapshot> GetConfigurationAsync(
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<ApplicationRule>> GetRulesAsync(
        CancellationToken cancellationToken = default);

    ValueTask ReplaceRulesAsync(
        IReadOnlyList<ApplicationRule> rules,
        CancellationToken cancellationToken = default);

    ValueTask<GlobalDefaultTarget?> GetGlobalDefaultAsync(
        CancellationToken cancellationToken = default);

    ValueTask ReplaceGlobalDefaultAsync(
        GlobalDefaultTarget? target,
        CancellationToken cancellationToken = default);
}
