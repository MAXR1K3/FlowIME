using FlowIME.Core.Context;
using FlowIME.Core.Decisions;
using FlowIME.Core.Models;
using FlowIME.Core.Rules;

namespace FlowIME.App.Services;

public sealed record CurrentStateSnapshot(
    WindowContext Window,
    InputState Input,
    InputAction? MatchedAction,
    ApplicationRule? MatchedRule = null,
    string? MatchedProviderId = null,
    RuleResolutionSource ResolutionSource = RuleResolutionSource.None,
    InputContextSnapshot? Context = null,
    InputDecision? Decision = null);
