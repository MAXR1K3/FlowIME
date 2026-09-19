using FlowIME.Core.Context;
using FlowIME.Core.Models;

namespace FlowIME.Core.Decisions;

public sealed record ContextPolicyMatch(
    InputAction Action,
    string? ProviderId = null,
    int Specificity = 0,
    string Reason = "context-policy");

/// <summary>
/// Pure decision policy over already-detected context signals. Policies do not
/// inspect native UI themselves and do not mutate the IME. This separation lets
/// P8B/P8D add fullscreen/browser behavior without touching AutomationCoordinator.
/// Implementations must be deterministic, side-effect free and thread-safe.
/// </summary>
public interface IInputContextPolicy
{
    string Id { get; }

    int Priority { get; }

    ContextPolicyMatch? Evaluate(InputContextSnapshot context);
}
