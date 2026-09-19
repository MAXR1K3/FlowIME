using FlowIME.Windows.Context;

namespace FlowIME.Windows.Tests.Context;

public sealed class WindowPresentationSnapshotTests
{
    [Fact]
    public void Negative_tolerance_is_rejected()
    {
        var snapshot = new WindowPresentationSnapshot(
            true,
            false,
            false,
            new PixelRect(0, 0, 1920, 1080),
            new PixelRect(0, 0, 1920, 1080));

        Assert.Throws<ArgumentOutOfRangeException>(() => snapshot.IsFullscreen(-1));
    }

    [Fact]
    public void Multi_monitor_origin_is_supported()
    {
        var snapshot = new WindowPresentationSnapshot(
            true,
            false,
            false,
            new PixelRect(-1920, 0, 0, 1080),
            new PixelRect(-1920, 0, 0, 1080));

        Assert.True(snapshot.IsFullscreen());
    }

    [Fact]
    public void Oversized_window_beyond_tolerance_is_not_fullscreen()
    {
        var snapshot = new WindowPresentationSnapshot(
            true,
            false,
            false,
            new PixelRect(-20, -20, 1940, 1100),
            new PixelRect(0, 0, 1920, 1080));

        Assert.False(snapshot.IsFullscreen());
    }
}
