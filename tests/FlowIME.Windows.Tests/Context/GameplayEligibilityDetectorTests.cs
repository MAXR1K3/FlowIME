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
    public async Task Non_fullscreen_window_never_samples_game_evidence()
    {
        var probe = new CountingEvidenceProbe(Evidence(GameplayEvidenceKind.WindowsGameMetadata));
        var detector = Detector(fullscreen: false, probe);

        var signals = await detector.DetectAsync(
            Request(),
            TestContext.Current.CancellationToken);

        Assert.Empty(signals);
        Assert.Equal(0, probe.CaptureCount);
        Assert.Empty(detector.GetRecentObservations());
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
