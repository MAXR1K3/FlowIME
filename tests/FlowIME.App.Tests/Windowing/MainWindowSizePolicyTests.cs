using FlowIME.App.Windowing;

namespace FlowIME.App.Tests.Windowing;

public sealed class MainWindowSizePolicyTests
{
    [Theory]
    [InlineData(1600, 1200, 2560, 1440, 96, 1280, 900)]
    [InlineData(700, 500, 1920, 1080, 96, 920, 640)]
    [InlineData(2400, 1600, 3840, 2160, 144, 1920, 1350)]
    [InlineData(1600, 1000, 1000, 700, 96, 1000, 700)]
    public void Clamp_keeps_the_window_inside_the_supported_effective_size_range(
        int width,
        int height,
        int workWidth,
        int workHeight,
        uint dpi,
        int expectedWidth,
        int expectedHeight)
    {
        var result = MainWindowSizePolicy.Clamp(width, height, workWidth, workHeight, dpi);

        Assert.Equal(expectedWidth, result.Width);
        Assert.Equal(expectedHeight, result.Height);
    }
}
