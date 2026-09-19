using FlowIME.Core.Models;

namespace FlowIME.Core.Rules;

public interface IRuleEngine
{
    RuleMatchResult Match(
        WindowContext window,
        IReadOnlyCollection<ApplicationRule> rules);

    RuleResolution Resolve(
        WindowContext window,
        IReadOnlyCollection<ApplicationRule> rules,
        GlobalDefaultTarget? globalDefault);
}
