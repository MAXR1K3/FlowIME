using FlowIME.Core.Context;
using FlowIME.Core.Models;

namespace FlowIME.Core.Tests.Context;

public sealed class GameTextEntryRuntimeStateTests
{
    [Fact]
    public void Inactive_by_default()
    {
        var state = new GameTextEntryRuntimeState();

        var snapshot = state.GetSnapshot();

        Assert.False(snapshot.Active);
        Assert.Equal(GameTextEntryActivationSource.None, snapshot.Source);
        Assert.Equal(0L, snapshot.Generation);
    }

    [Fact]
    public void Activate_is_scoped_to_application_and_process()
    {
        var state = new GameTextEntryRuntimeState();
        var application = ApplicationIdentity.FromWindow(Window("game", 42));

        Assert.True(state.Activate(
            application,
            processId: 42,
            topLevelHwnd: (nint)0x10,
            processName: "game",
            source: GameTextEntryActivationSource.HotkeyProfile,
            profileId: "game-chat"));

        Assert.True(state.IsActiveFor(application, 42, (nint)0x10));
        Assert.False(state.IsActiveFor(application, 43, (nint)0x10));
        Assert.False(state.IsActiveFor(application, 42, (nint)0x11));
        Assert.Equal("game-chat", state.GetSnapshot().ProfileId);
    }

    [Fact]
    public void Deactivate_clears_active_identity_and_increments_generation()
    {
        var state = new GameTextEntryRuntimeState();
        var application = ApplicationIdentity.FromWindow(Window("game", 42));
        state.Activate(
            application,
            42,
            (nint)0x10,
            "game",
            GameTextEntryActivationSource.Manual);

        Assert.True(state.Deactivate("submitted"));

        var snapshot = state.GetSnapshot();
        Assert.False(snapshot.Active);
        Assert.Equal(2L, snapshot.Generation);
        Assert.Equal("submitted", snapshot.LastReason);
        Assert.Equal("none", snapshot.ApplicationKey);
    }

    [Fact]
    public async Task Runtime_detector_emits_only_for_matching_identity()
    {
        var state = new GameTextEntryRuntimeState();
        var window = Window("game", 42);
        var application = ApplicationIdentity.FromWindow(window);
        state.Activate(
            application,
            42,
            window.Hwnd,
            window.ProcessName,
            GameTextEntryActivationSource.Adapter);
        var detector = new GameTextEntryRuntimeDetector(state);

        var matching = await detector.DetectAsync(
            Request(window),
            TestContext.Current.CancellationToken);
        var other = await detector.DetectAsync(
            Request(Window("other", 99)),
            TestContext.Current.CancellationToken);

        Assert.Single(matching);
        Assert.Equal(InputContextSignalKind.GameTextEntry, matching[0].Kind);
        Assert.Empty(other);
    }

    private static ContextDetectionRequest Request(WindowContext window) =>
        new(
            window,
            InputContextTrigger.GameTextEntryChanged,
            FocusHwnd: 0,
            Timestamp: DateTimeOffset.UtcNow);

    private static WindowContext Window(string processName, uint processId) =>
        new(
            (nint)0x10,
            processId,
            7,
            processName,
            $@"C:\Games\{processName}.exe",
            "Game",
            "GameWindow",
            null);
}
