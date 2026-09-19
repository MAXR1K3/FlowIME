using FlowIME.Core.Context;
using FlowIME.Core.Models;
using FlowIME.Windows.Context;

namespace FlowIME.Windows.Tests.Context;

public sealed class FullscreenWindowDetectorTests
{
    [Fact]
    public async Task Exact_monitor_bounds_emit_fullscreen_signal()
    {
        var detector = new FullscreenWindowDetector(
            new FixedProbe(Snapshot(
                window: new PixelRect(0, 0, 2560, 1440),
                monitor: new PixelRect(0, 0, 2560, 1440))));

        var signals = await detector.DetectAsync(
            Request(),
            TestContext.Current.CancellationToken);

        var signal = Assert.Single(signals);
        Assert.Equal(InputContextSignalKind.Fullscreen, signal.Kind);
        Assert.Equal(FullscreenWindowDetector.DetectorId, signal.Source);
        Assert.Equal(ContextSignalConfidence.High, signal.Confidence);
    }

    [Fact]
    public async Task Small_frame_variance_is_tolerated()
    {
        var detector = new FullscreenWindowDetector(
            new FixedProbe(Snapshot(
                window: new PixelRect(-2, 1, 2562, 1438),
                monitor: new PixelRect(0, 0, 2560, 1440))));

        var signals = await detector.DetectAsync(
            Request(),
            TestContext.Current.CancellationToken);

        Assert.Single(signals);
    }

    [Fact]
    public async Task Maximized_work_area_with_taskbar_is_not_fullscreen()
    {
        var detector = new FullscreenWindowDetector(
            new FixedProbe(Snapshot(
                window: new PixelRect(0, 0, 2560, 1400),
                monitor: new PixelRect(0, 0, 2560, 1440))));

        var signals = await detector.DetectAsync(
            Request(),
            TestContext.Current.CancellationToken);

        Assert.Empty(signals);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    public async Task Non_presentable_windows_do_not_emit_signal(
        bool visible,
        bool minimized,
        bool cloaked)
    {
        var detector = new FullscreenWindowDetector(
            new FixedProbe(new WindowPresentationSnapshot(
                visible,
                minimized,
                cloaked,
                new PixelRect(0, 0, 1920, 1080),
                new PixelRect(0, 0, 1920, 1080))));

        var signals = await detector.DetectAsync(
            Request(),
            TestContext.Current.CancellationToken);

        Assert.Empty(signals);
    }

    [Fact]
    public async Task Missing_geometry_does_not_emit_signal()
    {
        var detector = new FullscreenWindowDetector(
            new FixedProbe(new WindowPresentationSnapshot(
                IsVisible: true,
                IsMinimized: false,
                IsCloaked: false,
                WindowBounds: null,
                MonitorBounds: new PixelRect(0, 0, 1920, 1080))));

        var signals = await detector.DetectAsync(
            Request(),
            TestContext.Current.CancellationToken);

        Assert.Empty(signals);
    }

    [Fact]
    public async Task Cancellation_is_observed_before_native_sampling()
    {
        var probe = new CountingProbe();
        var detector = new FullscreenWindowDetector(probe);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            detector.DetectAsync(Request(), cancellation.Token).AsTask());

        Assert.Equal(0, probe.CaptureCount);
    }

    private static WindowPresentationSnapshot Snapshot(
        PixelRect window,
        PixelRect monitor) =>
        new(
            IsVisible: true,
            IsMinimized: false,
            IsCloaked: false,
            WindowBounds: window,
            MonitorBounds: monitor);

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

    private sealed class FixedProbe(WindowPresentationSnapshot snapshot)
        : IWindowPresentationProbe
    {
        public WindowPresentationSnapshot Capture(nint hwnd) => snapshot;
    }

    private sealed class CountingProbe : IWindowPresentationProbe
    {
        public int CaptureCount { get; private set; }

        public WindowPresentationSnapshot Capture(nint hwnd)
        {
            CaptureCount++;
            return new WindowPresentationSnapshot(
                true,
                false,
                false,
                new PixelRect(0, 0, 1920, 1080),
                new PixelRect(0, 0, 1920, 1080));
        }
    }
}
