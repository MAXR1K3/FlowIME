using FlowIME.Core.Settings;

namespace FlowIME.Core.Tests.Settings;

public sealed class InputStatusOverlaySettingsTests
{
    [Fact]
    public void Default_enables_overlay()
    {
        Assert.True(InputStatusOverlaySettings.Default.Enabled);
    }

    [Fact]
    public void Default_preserves_the_existing_bottom_center_presentation()
    {
        var settings = InputStatusOverlaySettings.Default;

        Assert.Equal(InputStatusOverlayPosition.BottomCenter, settings.Position);
        Assert.Equal(InputStatusOverlaySize.Medium, settings.Size);
        Assert.Equal(92, settings.OpacityPercent);
        Assert.True(settings.AnimationsEnabled);
    }

    [Theory]
    [InlineData(10, 40)]
    [InlineData(75, 75)]
    [InlineData(120, 100)]
    public void Normalize_clamps_opacity_to_a_readable_range(int requested, int expected)
    {
        var settings = new InputStatusOverlaySettings { OpacityPercent = requested };

        Assert.Equal(expected, settings.Normalize().OpacityPercent);
    }
}
