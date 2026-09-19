using FlowIME.Core.Context;
using FlowIME.Core.Models;

namespace FlowIME.Core.Decisions;

/// <summary>
/// Prevents ordinary application/global IME rules from fighting the dedicated
/// gameplay US-keyboard baseline. The policy never mutates input state itself;
/// the Windows baseline service performs the keyboard-layout request.
///
/// GameTextEntry intentionally bypasses this policy so the higher-priority text-entry
/// policy can temporarily choose a text-input target without dismantling gameplay mode.
/// </summary>
public sealed class GameplayKeyboardBaselinePolicy : IInputContextPolicy
{
    public const string PolicyId = "gameplay.us-keyboard-baseline";

    private readonly GameplayKeyboardBaselineState _state;

    public GameplayKeyboardBaselinePolicy(GameplayKeyboardBaselineState state)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
    }

    public string Id => PolicyId;

    public int Priority => 10_000;

    public ContextPolicyMatch? Evaluate(InputContextSnapshot context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!_state.IsReady ||
            !context.HasSignal(InputContextSignalKind.Game) ||
            context.HasSignal(InputContextSignalKind.GameTextEntry))
        {
            return null;
        }

        return new ContextPolicyMatch(
            InputAction.Keep,
            Specificity: 10_000,
            Reason: "gameplay-us-keyboard-baseline");
    }
}
