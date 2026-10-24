using FlowIME.Core.Context;
using FlowIME.Core.Models;
using FlowIME.Windows.Context;

namespace FlowIME.Windows.Tests.Context;

public sealed class GameplayEligibilityDetectorTests
{
    [Fact]
    public async Task Fullscreen_alone_does_not_emit_game_signal()
    {
        var detector = Detector(fullscreen: true);

        var signals = await detector.DetectAsync(
            Request(),
            TestContext.Current.CancellationToken);

        Assert.Empty(signals);
    }

    [Fact]
    public async Task Non_fullscreen_window_samples_evidence_and_promotes_known_game()
    {
        var probe = new CountingEvidenceProbe(Evidence(GameplayEvidenceKind.WindowsGameMetadata));
        var detector = Detector(fullscreen: false, probe);

        var signals = await detector.DetectAsync(
            Request(),
            TestContext.Current.CancellationToken);

        var signal = Assert.Single(signals);
        Assert.Equal(InputContextSignalKind.Game, signal.Kind);
        Assert.Equal(ContextSignalConfidence.High, signal.Confidence);
        Assert.Equal(1, probe.CaptureCount);

        var observation = Assert.Single(detector.GetRecentObservations());
        Assert.False(observation.IsFullscreen);
        Assert.True(observation.IsEligible);
        Assert.Equal("windows-game-metadata", observation.Reason);
    }

    [Fact]
    public async Task Non_fullscreen_direct3d_evidence_is_rejected_but_recorded()
    {
        var detector = Detector(
            fullscreen: false,
            new FixedEvidenceProbe(Evidence(GameplayEvidenceKind.Direct3DExclusive)));

        var signals = await detector.DetectAsync(
            Request(),
            TestContext.Current.CancellationToken);

        Assert.Empty(signals);

        var observation = Assert.Single(detector.GetRecentObservations());
        Assert.False(observation.IsFullscreen);
        Assert.False(observation.IsEligible);
        Assert.Equal("insufficient-game-evidence", observation.Reason);
        Assert.Contains(GameplayEvidenceKind.Direct3DExclusive, observation.Evidence);
    }

    [Fact]
    public async Task Non_fullscreen_window_without_evidence_stays_safe_and_is_recorded()
    {
        var detector = Detector(fullscreen: false);

        var signals = await detector.DetectAsync(
            Request(),
            TestContext.Current.CancellationToken);

        Assert.Empty(signals);

        var observation = Assert.Single(detector.GetRecentObservations());
        Assert.False(observation.IsFullscreen);
        Assert.False(observation.IsEligible);
        Assert.Equal("insufficient-game-evidence", observation.Reason);
        Assert.Empty(observation.Evidence);
    }

    [Fact]
    public async Task User_declared_game_promotes_windowed_game_without_system_metadata()
    {
        var window = Request().Window;
        var profiles = new GameTextEntryProfileRegistry();
        profiles.ReplaceProfiles([new GameTextEntryProfile
        {
            Id = "declared",
            ApplicationIdentityKey = FlowIME.Core.Context.ApplicationIdentity.FromWindow(window).Key,
            Classification = GameplayClassification.Game,
            DetectionMode = GameTextEntryDetectionMode.StandardTextControl
        }]);
        var detector = new GameplayEligibilityDetector(
            profiles,
            new FixedPresentationProbe(WindowedSnapshot()),
            new GameplayEligibilityEvaluator(),
            []);

        var signals = await detector.DetectAsync(Request(), TestContext.Current.CancellationToken);

        Assert.Single(signals);
        Assert.Equal("user-declared-game", detector.GetRecentObservations().Single().Reason);
    }

    [Fact]
    public async Task User_declared_not_game_vetoes_system_evidence()
    {
        var window = Request().Window;
        var profiles = new GameTextEntryProfileRegistry();
        profiles.ReplaceProfiles([new GameTextEntryProfile
        {
            Id = "not-game",
            ApplicationIdentityKey = FlowIME.Core.Context.ApplicationIdentity.FromWindow(window).Key,
            Classification = GameplayClassification.NotGame
        }]);
        var detector = new GameplayEligibilityDetector(
            profiles,
            new FixedPresentationProbe(WindowedSnapshot()),
            new GameplayEligibilityEvaluator(),
            [new FixedEvidenceProbe(Evidence(GameplayEvidenceKind.WindowsGameMetadata))]);

        Assert.Empty(await detector.DetectAsync(Request(), TestContext.Current.CancellationToken));
        Assert.Equal("user-declared-not-game", detector.GetRecentObservations().Single().Reason);
    }

    [Theory]
    [InlineData(GameplayEvidenceKind.WindowsGameMetadata)]
    [InlineData(GameplayEvidenceKind.Direct3DExclusive)]
    public async Task Strong_evidence_promotes_fullscreen_to_game(
        GameplayEvidenceKind kind)
    {
        var detector = Detector(fullscreen: true, new FixedEvidenceProbe(Evidence(kind)));

        var signals = await detector.DetectAsync(
            Request(),
            TestContext.Current.CancellationToken);

        var signal = Assert.Single(signals);
        Assert.Equal(InputContextSignalKind.Game, signal.Kind);
        Assert.Equal(ContextSignalConfidence.High, signal.Confidence);
        Assert.StartsWith(
            GameplayEligibilityDetector.DetectorId,
            signal.Source,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Two_independent_system_facts_emit_certain_game_signal()
    {
        var detector = Detector(
            fullscreen: true,
            new FixedEvidenceProbe(Evidence(GameplayEvidenceKind.WindowsGameMetadata), "metadata"),
            new FixedEvidenceProbe(Evidence(GameplayEvidenceKind.Direct3DExclusive), "d3d"));

        var signals = await detector.DetectAsync(
            Request(),
            TestContext.Current.CancellationToken);

        var signal = Assert.Single(signals);
        Assert.Equal(ContextSignalConfidence.Certain, signal.Confidence);
    }

    [Fact]
    public async Task Fullscreen_assessments_are_kept_for_post_focus_diagnostics()
    {
        var detector = Detector(
            fullscreen: true,
            new FixedEvidenceProbe(Evidence(GameplayEvidenceKind.WindowsGameMetadata)));

        await detector.DetectAsync(Request(), TestContext.Current.CancellationToken);

        var observation = Assert.Single(detector.GetRecentObservations());
        Assert.Equal("game", observation.ProcessName);
        Assert.True(observation.IsFullscreen);
        Assert.True(observation.IsEligible);
        Assert.Equal("windows-game-metadata", observation.Reason);
        Assert.Contains(GameplayEvidenceKind.WindowsGameMetadata, observation.Evidence);
    }

    [Fact]
    public async Task Failing_optional_probe_does_not_hide_other_evidence()
    {
        var detector = Detector(
            fullscreen: true,
            new ThrowingEvidenceProbe(),
            new FixedEvidenceProbe(Evidence(GameplayEvidenceKind.WindowsGameMetadata), "healthy"));

        var signals = await detector.DetectAsync(
            Request(),
            TestContext.Current.CancellationToken);

        Assert.Single(signals);
    }

    [Fact]
    public async Task Cancellation_is_observed_before_native_sampling()
    {
        var presentation = new CountingPresentationProbe(FullscreenSnapshot());
        var detector = new GameplayEligibilityDetector(
            presentation,
            new GameplayEligibilityEvaluator(),
            []);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            detector.DetectAsync(Request(), cancellation.Token).AsTask());

        Assert.Equal(0, presentation.CaptureCount);
    }

    [Theory]
    [InlineData(GameplayClassification.Game, true)]
    [InlineData(GameplayClassification.NotGame, false)]
    public async Task Explicit_declaration_survives_failed_window_and_system_probes(GameplayClassification classification, bool eligible)
    {
        var profiles = new GameTextEntryProfileRegistry();
        profiles.ReplaceProfiles([GameTextEntryProfile.CreateDefault(
            FlowIME.Core.Context.ApplicationIdentity.FromWindow(Request().Window).Key, "Game") with
        { Classification = classification, Enabled = false }]);
        var detector = new GameplayEligibilityDetector(profiles, new ThrowingPresentationProbe(),
            new GameplayEligibilityEvaluator(), [new ThrowingEvidenceProbe()]);
        var signals = await detector.DetectAsync(Request(), TestContext.Current.CancellationToken);
        Assert.Equal(eligible, signals.Any(signal => signal.Kind == InputContextSignalKind.Game));
        var observation = Assert.Single(detector.GetRecentObservations());
        Assert.Contains("window-presentation", observation.FailedProbeIds);
        Assert.Contains("throwing", observation.FailedProbeIds);
        Assert.True(observation.ExecutablePathAvailable);
    }

    private sealed class ThrowingPresentationProbe : IWindowPresentationProbe
    {
        public WindowPresentationSnapshot Capture(nint hwnd) => throw new InvalidOperationException("unavailable");
    }

    private static GameplayEligibilityDetector Detector(
        bool fullscreen,
        params IGameplayEvidenceProbe[] probes) =>
        new(
            new FixedPresentationProbe(
                fullscreen ? FullscreenSnapshot() : WindowedSnapshot()),
            new GameplayEligibilityEvaluator(),
            probes);

    private static ContextDetectionRequest Request() =>
        new(
            new WindowContext(
                (nint)0x1234,
                10,
                20,
                "game",
                @"C:\Games\game.exe",
                "Game",
                "GameWindow",
                null),
            InputContextTrigger.ForegroundChanged,
            FocusHwnd: (nint)0x1234,
            Timestamp: DateTimeOffset.UtcNow);

    private static GameplayEvidence Evidence(GameplayEvidenceKind kind) =>
        new(kind, $"test.{kind}");

    private static WindowPresentationSnapshot FullscreenSnapshot() =>
        new(
            true,
            false,
            false,
            new PixelRect(0, 0, 2560, 1440),
            new PixelRect(0, 0, 2560, 1440));

    private static WindowPresentationSnapshot WindowedSnapshot() =>
        new(
            true,
            false,
            false,
            new PixelRect(100, 100, 1800, 1000),
            new PixelRect(0, 0, 2560, 1440));

    private sealed class FixedPresentationProbe(WindowPresentationSnapshot snapshot)
        : IWindowPresentationProbe
    {
        public WindowPresentationSnapshot Capture(nint hwnd) => snapshot;
    }

    private sealed class CountingPresentationProbe(WindowPresentationSnapshot snapshot)
        : IWindowPresentationProbe
    {
        public int CaptureCount { get; private set; }

        public WindowPresentationSnapshot Capture(nint hwnd)
        {
            CaptureCount++;
            return snapshot;
        }
    }

    private sealed class FixedEvidenceProbe(
        GameplayEvidence evidence,
        string id = "fixed") : IGameplayEvidenceProbe
    {
        public string Id => id;
        public int Order => 0;
        public GameplayEvidence? Capture(WindowContext window) => evidence;
    }

    private sealed class CountingEvidenceProbe(GameplayEvidence evidence)
        : IGameplayEvidenceProbe
    {
        public int CaptureCount { get; private set; }
        public string Id => "counting";
        public int Order => 0;

        public GameplayEvidence? Capture(WindowContext window)
        {
            CaptureCount++;
            return evidence;
        }
    }

    private sealed class ThrowingEvidenceProbe : IGameplayEvidenceProbe
    {
        public string Id => "throwing";
        public int Order => 0;

        public GameplayEvidence? Capture(WindowContext window) =>
            throw new InvalidOperationException("optional probe failed");
    }
}
