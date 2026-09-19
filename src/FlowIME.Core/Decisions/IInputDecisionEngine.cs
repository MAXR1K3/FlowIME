using FlowIME.Core.Context;
using FlowIME.Core.Rules;

namespace FlowIME.Core.Decisions;

public interface IInputDecisionEngine
{
    InputDecision Resolve(
        InputContextSnapshot context,
        RuleConfigurationSnapshot configuration);
}
