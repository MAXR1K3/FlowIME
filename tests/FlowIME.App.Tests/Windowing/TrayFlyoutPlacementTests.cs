using FlowIME.App.Windowing;
using Windows.Graphics;

namespace FlowIME.App.Tests.Windowing;

public sealed class TrayFlyoutPlacementTests
{
    private static readonly SizeInt32 Flyout = new(410, 450);

    [Fact]
    public void Bottom_taskbar_opens_upwards_centred_on_the_pointer()
    {
        var bounds = TrayFlyoutPlacement.Calculate(
            new PointInt32(1400, 1060), Flyout, new RectInt32(0, 0, 1920, 1032), 1.25);

        Assert.Equal(new RectInt32(1195, 567, 410, 450), bounds);
    }

    [Fact]
    public void Flyout_never_crosses_the_screen_edge()
    {
        var bounds = TrayFlyoutPlacement.Calculate(
            new PointInt32(1900, 1060), Flyout, new RectInt32(0, 0, 1920, 1032), 1.25);

        Assert.Equal(1495, bounds.X);
    }

    [Fact]
    public void Top_taskbar_opens_downwards()
    {
        var bounds = TrayFlyoutPlacement.Calculate(
            new PointInt32(300, 20), Flyout, new RectInt32(0, 48, 1920, 1032), 1.25);

        Assert.Equal(new RectInt32(95, 63, 410, 450), bounds);
    }

    [Fact]
    public void Pointer_inside_the_work_area_keeps_the_flyout_next_to_it()
    {
        // The Windows 11 hidden-icons overflow sits above the taskbar.
        var bounds = TrayFlyoutPlacement.Calculate(
            new PointInt32(1600, 900), Flyout, new RectInt32(0, 0, 1920, 1032), 1.25);

        Assert.Equal(435, bounds.Y);
    }
}
