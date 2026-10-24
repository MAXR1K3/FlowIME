using FlowIME.Core.Context;
using FlowIME.Windows.Context;

namespace FlowIME.App.Services;

/// <summary>
/// Single production composition for classification and text entry. Both consumers
/// share the live registry; a saved game must not depend on Windows metadata.
/// </summary>
internal sealed class GameplayContextPipeline
{
    internal GameplayContextPipeline(
        GameTextEntryProfileRegistry profiles,
        GameTextEntryRuntimeState runtimeState)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(runtimeState);
        EligibilityDetector = new GameplayEligibilityDetector(profiles);
        Engine = new RecentInputContextCache(new InputContextEngine([
            new FullscreenWindowDetector(),
            EligibilityDetector,
            new StandardGameTextEntryDetector(profiles, runtimeState),
            new GameTextEntryRuntimeDetector(runtimeState)
        ]));
    }

    internal GameplayEligibilityDetector EligibilityDetector { get; }
    internal IInputContextEngine Engine { get; }
}
