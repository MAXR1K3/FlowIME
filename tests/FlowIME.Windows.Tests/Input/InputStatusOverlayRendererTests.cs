using FlowIME.Core.Settings;
using FlowIME.Windows.Input;

namespace FlowIME.Windows.Tests.Input;

public sealed class InputStatusOverlayRendererTests
{
    [Fact]
    public void Render_uses_a_light_modern_surface_with_transparent_shadow_padding()
    {
        using var frame = InputStatusOverlayRenderer.Render(
            "EN",
            InputStatusOverlaySize.Medium,
            dpi: 96,
            opacityPercent: 100);

        Assert.True(frame.Width > 74);
        Assert.True(frame.Height > 42);
        Assert.Equal(0, frame.GetPixel(0, 0).Alpha);

        var surface = frame.GetPixel(frame.Width / 2, frame.SurfaceTop + 4);
        Assert.True(surface.Red >= 235, $"Unexpected surface red channel: {surface.Red}");
        Assert.True(surface.Green >= 235, $"Unexpected surface green channel: {surface.Green}");
        Assert.True(surface.Blue >= 235, $"Unexpected surface blue channel: {surface.Blue}");
    }

    [Fact]
    public void Render_has_partially_transparent_edge_pixels_instead_of_a_jagged_region_cutout()
    {
        using var frame = InputStatusOverlayRenderer.Render(
            "中",
            InputStatusOverlaySize.Medium,
            dpi: 96,
            opacityPercent: 92);

        var hasAntialiasedEdge = false;
        for (var y = frame.SurfaceTop; y < frame.SurfaceTop + 14; y++)
        {
            for (var x = frame.SurfaceLeft; x < frame.SurfaceLeft + 14; x++)
            {
                var alpha = frame.GetPixel(x, y).Alpha;
                if (alpha is > 0 and < 220)
                {
                    hasAntialiasedEdge = true;
                    break;
                }
            }
        }

        Assert.True(hasAntialiasedEdge);
    }

    [Theory]
    [InlineData("中", "Microsoft YaHei UI")]
    [InlineData("EN", "Segoe UI Variable Display")]
    public void Render_selects_an_optically_suitable_typeface(string label, string expectedFamily)
    {
        using var frame = InputStatusOverlayRenderer.Render(
            label,
            InputStatusOverlaySize.Medium,
            dpi: 96,
            opacityPercent: 92);

        Assert.Equal(expectedFamily, frame.TypefaceFamily);
    }
}
