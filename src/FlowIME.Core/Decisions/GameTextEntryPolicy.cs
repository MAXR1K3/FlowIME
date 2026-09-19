using FlowIME.Core.Context;
using FlowIME.Core.Models;

namespace FlowIME.Core.Decisions;

/// <summary>
/// Text-entry target policy. It outranks the Gameplay US baseline only while
/// GameTextEntry is positively active. An activated-but-unconfigured session resolves
/// to Keep rather than falling through to unrelated application/global rules.
/// </summary>
public sealed class GameTextEntryPolicy : IInputContextPolicy
{
    public const string PolicyId = "gameplay.text-entry";

    private readonly GameTextEntryProfileRegistry _profiles;

    public GameTextEntryPolicy(GameTextEntryProfileRegistry profiles)
    {
        _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
    }

    public string Id => PolicyId;

    public int Priority => 20_000;

    public ContextPolicyMatch? Evaluate(InputContextSnapshot context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!context.HasSignal(InputContextSignalKind.GameTextEntry))
        {
            return null;
        }

        var profile = _profiles.Resolve(context.Application);
        if (profile is null)
        {
            return new ContextPolicyMatch(
                InputAction.Keep,
                Specificity: 20_000,
                Reason: "game-text-entry-no-profile");
        }

        return new ContextPolicyMatch(
            profile.Action,
            Specificity: 20_000,
            Reason: $"game-text-entry-profile:{profile.Id}",
            ProviderId: profile.Action == InputAction.Keep
                ? null
                : profile.ProviderId);
    }
}
