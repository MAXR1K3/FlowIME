using FlowIME.Core.Models;
using FlowIME.Windows.Input;

namespace FlowIME.Windows.Tests.Input;

public sealed class InputStatusOverlayStateResolverTests
{
    private static readonly nint StandardUsLayout = unchecked((nint)0x04090409);

    [Theory]
    [InlineData(InputMode.Chinese, "中")]
    [InlineData(InputMode.English, "EN")]
    public void Known_provider_mode_is_displayed(InputMode mode, string expected)
    {
        var label = InputStatusOverlayStateResolver.ResolveLabel(
            mode,
            keyboardLayout: 0,
            gameplayUsBaselineActive: false);

        Assert.Equal(expected, label);
    }

    [Fact]
    public void Unknown_provider_with_standard_us_layout_displays_us()
    {
        var label = InputStatusOverlayStateResolver.ResolveLabel(
            InputMode.Unknown,
            StandardUsLayout,
            gameplayUsBaselineActive: false);

        Assert.Equal("US", label);
    }

    [Fact]
    public void Gameplay_us_baseline_prefers_us_label()
    {
        var label = InputStatusOverlayStateResolver.ResolveLabel(
            InputMode.Chinese,
            StandardUsLayout,
            gameplayUsBaselineActive: true);

        Assert.Equal("US", label);
    }

    [Fact]
    public void Unknown_non_us_state_does_not_invent_label()
    {
        var label = InputStatusOverlayStateResolver.ResolveLabel(
            InputMode.Unknown,
            keyboardLayout: 0,
            gameplayUsBaselineActive: false);

        Assert.Null(label);
    }
}
