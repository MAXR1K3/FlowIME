using FlowIME.Core.Context;
using FlowIME.Core.Models;
using FlowIME.Windows.Context;

namespace FlowIME.Windows.Tests.Context;

public sealed class StandardGameTextEntryDetectorTests
{
    [Fact]
    public async Task High_confidence_text_focus_activates_runtime_for_projection()
    {
        var window = Window();
        var registry = Registry(window, GameTextEntryDetectionMode.StandardTextControl);
        var runtime = new GameTextEntryRuntimeState();
        var detector = new StandardGameTextEntryDetector(
            registry,
            runtime,
            new FakeProbe(new StandardTextControlObservation(
                true,
                ContextSignalConfidence.Certain,
                "accessible-text",
                (nint)0x20)),
            FullscreenProbe());

        var signals = await detector.DetectAsync(
            Request(window),
            TestContext.Current.CancellationToken);

        Assert.Empty(signals);
        var snapshot = runtime.GetSnapshot();
        Assert.True(snapshot.Active);
        Assert.Equal(GameTextEntryActivationSource.StandardTextControl, snapshot.Source);
        Assert.Equal("chat", snapshot.ProfileId);

        var projected = await new GameTextEntryRuntimeDetector(runtime).DetectAsync(
            Request(window),
            TestContext.Current.CancellationToken);
        Assert.Single(projected);
    }

    [Fact]
    public async Task Does_not_probe_on_non_focus_trigger_and_preserves_existing_runtime()
    {
        var window = Window();
        var application = ApplicationIdentity.FromWindow(window);
        var runtime = new GameTextEntryRuntimeState();
        _ = runtime.Activate(
            application,
            window.ProcessId,
            window.Hwnd,
            window.ProcessName,
            GameTextEntryActivationSource.StandardTextControl,
            "chat");
        var probe = new FakeProbe(new StandardTextControlObservation(
            false,
            ContextSignalConfidence.Low,
            "not-standard-text-control",
            (nint)0x20));
        var detector = new StandardGameTextEntryDetector(
            Registry(window, GameTextEntryDetectionMode.StandardTextControl),
            runtime,
            probe,
            FullscreenProbe());
        var request = Request(window) with { Trigger = InputContextTrigger.ManualRefresh };

        var signals = await detector.DetectAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Empty(signals);
        Assert.Equal(0, probe.CaptureCount);
        Assert.True(runtime.GetSnapshot().Active);
    }

    [Fact]
    public async Task Does_not_probe_when_profile_did_not_enable_standard_detection()
    {
        var window = Window();
        var probe = new FakeProbe(new StandardTextControlObservation(
            true,
            ContextSignalConfidence.Certain,
            "accessible-text",
            (nint)0x20));
        var detector = new StandardGameTextEntryDetector(
            Registry(window, GameTextEntryDetectionMode.HotkeyProfile),
            new GameTextEntryRuntimeState(),
            probe,
            FullscreenProbe());

        var signals = await detector.DetectAsync(
            Request(window),
            TestContext.Current.CancellationToken);

        Assert.Empty(signals);
        Assert.Equal(0, probe.CaptureCount);
    }


    [Fact]
    public async Task Does_not_probe_or_latch_when_configured_game_is_not_fullscreen()
    {
        var window = Window();
        var runtime = new GameTextEntryRuntimeState();
        var probe = new FakeProbe(new StandardTextControlObservation(
            true,
            ContextSignalConfidence.Certain,
            "accessible-text",
            (nint)0x20));
        var detector = new StandardGameTextEntryDetector(
            Registry(window, GameTextEntryDetectionMode.StandardTextControl),
            runtime,
            probe,
            new FakePresentationProbe(new WindowPresentationSnapshot(
                IsVisible: true,
                IsMinimized: false,
                IsCloaked: false,
                WindowBounds: new PixelRect(0, 0, 1600, 900),
                MonitorBounds: new PixelRect(0, 0, 1920, 1080))));

        _ = await detector.DetectAsync(
            Request(window),
            TestContext.Current.CancellationToken);

        Assert.Equal(0, probe.CaptureCount);
        Assert.False(runtime.GetSnapshot().Active);
    }

    [Fact]
    public async Task Ambiguous_medium_confidence_document_does_not_enter_text_mode()
    {
        var window = Window();
        var runtime = new GameTextEntryRuntimeState();
        var detector = new StandardGameTextEntryDetector(
            Registry(window, GameTextEntryDetectionMode.StandardTextControl),
            runtime,
            new FakeProbe(new StandardTextControlObservation(
                true,
                ContextSignalConfidence.Medium,
                "accessible-document",
                (nint)0x20)),
            FullscreenProbe());

        var signals = await detector.DetectAsync(
            Request(window),
            TestContext.Current.CancellationToken);

        Assert.Empty(signals);
        Assert.False(runtime.GetSnapshot().Active);
    }

    [Fact]
    public async Task Non_text_focus_deactivates_owned_standard_session()
    {
        var window = Window();
        var application = ApplicationIdentity.FromWindow(window);
        var runtime = new GameTextEntryRuntimeState();
        _ = runtime.Activate(
            application,
            window.ProcessId,
            window.Hwnd,
            window.ProcessName,
            GameTextEntryActivationSource.StandardTextControl,
            "chat");
        var detector = new StandardGameTextEntryDetector(
            Registry(window, GameTextEntryDetectionMode.StandardTextControl),
            runtime,
            new FakeProbe(new StandardTextControlObservation(
                false,
                ContextSignalConfidence.Low,
                "not-standard-text-control",
                (nint)0x20)),
            FullscreenProbe());

        var signals = await detector.DetectAsync(
            Request(window),
            TestContext.Current.CancellationToken);

        Assert.Empty(signals);
        Assert.False(runtime.GetSnapshot().Active);
        Assert.Equal("standard-focus-left", runtime.GetSnapshot().LastReason);
    }

    [Fact]
    public async Task Negative_standard_focus_does_not_clear_hotkey_owned_session()
    {
        var window = Window();
        var application = ApplicationIdentity.FromWindow(window);
        var runtime = new GameTextEntryRuntimeState();
        _ = runtime.Activate(
            application,
            window.ProcessId,
            window.Hwnd,
            window.ProcessName,
            GameTextEntryActivationSource.HotkeyProfile,
            "chat");
        var detector = new StandardGameTextEntryDetector(
            Registry(window, GameTextEntryDetectionMode.StandardTextControl),
            runtime,
            new FakeProbe(new StandardTextControlObservation(
                false,
                ContextSignalConfidence.Low,
                "not-standard-text-control",
                (nint)0x20)),
            FullscreenProbe());

        _ = await detector.DetectAsync(
            Request(window),
            TestContext.Current.CancellationToken);

        var snapshot = runtime.GetSnapshot();
        Assert.True(snapshot.Active);
        Assert.Equal(GameTextEntryActivationSource.HotkeyProfile, snapshot.Source);
    }


    [Fact]
    public async Task Older_focus_event_cannot_override_newer_committed_state()
    {
        var window = Window();
        var runtime = new GameTextEntryRuntimeState();
        var probe = new FakeProbe(new StandardTextControlObservation(
            true,
            ContextSignalConfidence.Certain,
            "accessible-text",
            (nint)0x20));
        var detector = new StandardGameTextEntryDetector(
            Registry(window, GameTextEntryDetectionMode.StandardTextControl),
            runtime,
            probe,
            FullscreenProbe());
        var now = DateTimeOffset.UtcNow;

        _ = await detector.DetectAsync(
            Request(window) with { Timestamp = now.AddMilliseconds(2) },
            TestContext.Current.CancellationToken);
        probe.Observation = new StandardTextControlObservation(
            false,
            ContextSignalConfidence.Low,
            "not-standard-text-control",
            (nint)0x20);
        _ = await detector.DetectAsync(
            Request(window) with { Timestamp = now },
            TestContext.Current.CancellationToken);

        Assert.True(runtime.GetSnapshot().Active);
        Assert.Equal(1, probe.CaptureCount);
    }

    private static GameTextEntryProfileRegistry Registry(
        WindowContext window,
        GameTextEntryDetectionMode mode)
    {
        var registry = new GameTextEntryProfileRegistry();
        registry.ReplaceProfiles([
            new GameTextEntryProfile
            {
                Id = "chat",
                ApplicationIdentityKey = ApplicationIdentity.FromWindow(window).Key,
                DetectionMode = mode,
                EnterGestures = mode.HasFlag(GameTextEntryDetectionMode.HotkeyProfile)
                    ? [new(0x54)]
                    : [],
                ExitGestures = mode.HasFlag(GameTextEntryDetectionMode.HotkeyProfile)
                    ? [new(0x0D)]
                    : []
            }
        ]);
        return registry;
    }

    private static ContextDetectionRequest Request(WindowContext window) =>
        new(
            window,
            InputContextTrigger.FocusChanged,
            FocusHwnd: (nint)0x20,
            Timestamp: DateTimeOffset.UtcNow,
            FocusObjectId: -4,
            FocusChildId: 0);

    private static IWindowPresentationProbe FullscreenProbe() =>
        new FakePresentationProbe(new WindowPresentationSnapshot(
            IsVisible: true,
            IsMinimized: false,
            IsCloaked: false,
            WindowBounds: new PixelRect(0, 0, 1920, 1080),
            MonitorBounds: new PixelRect(0, 0, 1920, 1080)));

    private static WindowContext Window() =>
        new(
            (nint)0x10,
            42,
            7,
            "game",
            @"C:\Games\game.exe",
            "Game",
            "GameWindow",
            null);

    private sealed class FakeProbe(StandardTextControlObservation observation) : IStandardTextControlProbe
    {
        public int CaptureCount { get; private set; }

        public StandardTextControlObservation Observation { get; set; } = observation;

        public StandardTextControlObservation Capture(ContextDetectionRequest request)
        {
            _ = request;
            CaptureCount++;
            return Observation;
        }
    }

    private sealed class FakePresentationProbe(WindowPresentationSnapshot snapshot) : IWindowPresentationProbe
    {
        public WindowPresentationSnapshot Capture(nint hwnd)
        {
            _ = hwnd;
            return snapshot;
        }
    }
}
