using FlowIME.App.Services;
using FlowIME.Core.Context;
using FlowIME.Core.Models;
using FlowIME.Infrastructure.Configuration;
using ApplicationIdentity = FlowIME.Core.Context.ApplicationIdentity;

namespace FlowIME.App.Tests.Services;

/// <summary>
/// Exercises the exact production composition, not a mock classifier. HWND 0 is
/// deliberately non-fullscreen; a unique unregistered EXE path has no OS metadata.
/// </summary>
public sealed class GameplayContextPipelineTests
{
    [Fact]
    public async Task Production_pipeline_tracks_profile_add_exclude_remove_and_alias_without_restart()
    {
        var profiles = new GameTextEntryProfileRegistry();
        var state = new GameTextEntryRuntimeState();
        var pipeline = new GameplayContextPipeline(profiles, state);
        var window = Window(@"C:\FlowIME-Regression-Unknown\game.exe");
        var identity = ApplicationIdentity.FromWindow(window);
        var profile = GameTextEntryProfile.CreateDefault(identity.Key, "Regression Game") with
        {
            ExecutablePath = window.ExecutablePath,
            Enabled = false,
            AdditionalExecutablePaths = [@"C:\FlowIME-Regression-Unknown\bin\game-dx12.exe"]
        };
        var tick = DateTimeOffset.UtcNow;
        async Task<InputContextSnapshot> Resolve(WindowContext target) =>
            await pipeline.Engine.ResolveAsync(new(target, InputContextTrigger.GameTextEntryChanged, 0,
                tick = tick.AddTicks(1)), TestContext.Current.CancellationToken);

        Assert.False((await Resolve(window)).HasSignal(InputContextSignalKind.Game));
        profiles.ReplaceProfiles([profile]);
        Assert.True((await Resolve(window)).HasSignal(InputContextSignalKind.Game));
        Assert.Contains("user-declared-game", pipeline.EligibilityDetector.GetRecentObservations(1)[0].Reason, StringComparison.Ordinal);
        Assert.True((await Resolve(Window(profile.AdditionalExecutablePaths[0]))).HasSignal(InputContextSignalKind.Game));
        Assert.False((await Resolve(Window(@"C:\Other-Directory\game.exe"))).HasSignal(InputContextSignalKind.Game));
        profiles.ReplaceProfiles([profile with { Classification = GameplayClassification.NotGame }]);
        Assert.False((await Resolve(window)).HasSignal(InputContextSignalKind.Game));
        Assert.Equal("user-declared-not-game", pipeline.EligibilityDetector.GetRecentObservations(1)[0].Reason);
        profiles.ReplaceProfiles([profile with { Classification = GameplayClassification.Automatic }]);
        Assert.False((await Resolve(window)).HasSignal(InputContextSignalKind.Game));
        profiles.ReplaceProfiles([profile]);
        Assert.True((await Resolve(window)).HasSignal(InputContextSignalKind.Game));
        profiles.ReplaceProfiles([]);
        Assert.False((await Resolve(window)).HasSignal(InputContextSignalKind.Game));
    }

    [Fact]
    public async Task Persisted_profile_is_classified_by_fresh_production_pipeline_after_restart()
    {
        var root = Path.Combine(Path.GetTempPath(), "FlowIME.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root);
        try
        {
            var window = Window(@"C:\FlowIME-Regression-Unknown\saved-game.exe");
            var profile = GameTextEntryProfile.CreateDefault(ApplicationIdentity.FromWindow(window).Key, "Saved") with
            { ExecutablePath = window.ExecutablePath, Enabled = false };
            using (var store = new JsonGameTextEntryProfileRepository(paths))
                await store.ReplaceProfilesAsync([profile], TestContext.Current.CancellationToken);
            using var reopened = new JsonGameTextEntryProfileRepository(paths);
            var profiles = new GameTextEntryProfileRegistry();
            profiles.ReplaceProfiles(await reopened.GetProfilesAsync(TestContext.Current.CancellationToken));
            var pipeline = new GameplayContextPipeline(profiles, new GameTextEntryRuntimeState());
            var snapshot = await pipeline.Engine.ResolveAsync(new(window, InputContextTrigger.ForegroundChanged, 0, DateTimeOffset.UtcNow), TestContext.Current.CancellationToken);
            Assert.True(snapshot.HasSignal(InputContextSignalKind.Game));
            Assert.Null(profiles.Resolve(snapshot.Application)); // Chat disabled != game disabled.
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static WindowContext Window(string path) => new(0, 0, 0, "game", path, null, null, null);
}
