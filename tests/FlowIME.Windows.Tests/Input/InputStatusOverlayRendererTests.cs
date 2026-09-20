using FlowIME.Core.Settings;
using FlowIME.Windows.Input;
using SkiaSharp;

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

    [Fact]
    public void Render_expands_the_surface_for_a_game_detection_message()
    {
        using var compact = InputStatusOverlayRenderer.Render(
            "US",
            InputStatusOverlaySize.Medium,
            dpi: 96,
            opacityPercent: 92);
        using var detection = InputStatusOverlayRenderer.Render(
            "游戏已识别",
            InputStatusOverlaySize.Medium,
            dpi: 96,
            opacityPercent: 92);

        Assert.True(detection.Width > compact.Width);
        Assert.Equal(compact.Height, detection.Height);
    }

    [Fact]
    public void Render_draws_the_supplied_input_method_brand_icon_before_the_language_label()
    {
        using var icon = new SKBitmap(16, 16);
        icon.Erase(new SKColor(220, 40, 20));
        using var frame = InputStatusOverlayRenderer.Render(
            "EN",
            InputStatusOverlaySize.Medium,
            dpi: 96,
            opacityPercent: 100,
            brandIcon: icon);

        var iconPixel = frame.GetPixel(frame.SurfaceLeft + 11, frame.Height / 2);
        Assert.True(iconPixel.Red < 80);
        Assert.True(iconPixel.Green < 80);
        Assert.True(iconPixel.Blue < 80);
    }

    [Fact]
    public void Render_tints_the_registered_brand_shape_for_dark_theme_contrast()
    {
        using var icon = new SKBitmap(16, 16);
        icon.Erase(new SKColor(20, 20, 20));
        using var frame = InputStatusOverlayRenderer.Render(
            "中",
            InputStatusOverlaySize.Medium,
            dpi: 96,
            opacityPercent: 100,
            colorScheme: OverlayColorScheme.Dark,
            brandIcon: icon);

        var iconPixel = frame.GetPixel(frame.SurfaceLeft + 11, frame.Height / 2);
        Assert.True(iconPixel.Red > 220);
        Assert.True(iconPixel.Green > 220);
        Assert.True(iconPixel.Blue > 220);
    }

    [Fact]
    public void Renderer_supports_system_light_and_dark_palettes()
    {
        var root = FindRepositoryRoot();
        var renderer = File.ReadAllText(Path.Combine(
            root,
            "src",
            "FlowIME.Windows",
            "Input",
            "InputStatusOverlayRenderer.cs"));
        var overlay = File.ReadAllText(Path.Combine(
            root,
            "src",
            "FlowIME.Windows",
            "Input",
            "InputStatusOverlay.cs"));

        Assert.Contains("OverlayColorScheme.Dark", renderer, StringComparison.Ordinal);
        Assert.Contains("OverlayColorScheme.Light", renderer, StringComparison.Ordinal);
        Assert.Contains("InputStatusOverlayThemeResolver.Resolve", overlay, StringComparison.Ordinal);
    }

    [Fact]
    public void Dark_palette_uses_a_dark_surface_with_high_contrast_content()
    {
        using var frame = InputStatusOverlayRenderer.Render(
            "中",
            InputStatusOverlaySize.Medium,
            dpi: 96,
            opacityPercent: 100,
            colorScheme: OverlayColorScheme.Dark);

        var surface = frame.GetPixel(frame.Width / 2, frame.SurfaceTop + 4);
        Assert.True(surface.Red < 70, $"Unexpected dark surface red channel: {surface.Red}");
        Assert.True(surface.Green < 70, $"Unexpected dark surface green channel: {surface.Green}");
        Assert.True(surface.Blue < 70, $"Unexpected dark surface blue channel: {surface.Blue}");
    }

    [Theory]
    [InlineData(0, "Dark")]
    [InlineData(1, "Light")]
    [InlineData(null, "Light")]
    public void Theme_resolver_maps_the_windows_apps_theme_setting(
        int? appsUseLightTheme,
        string expected)
    {
        Assert.Equal(expected, InputStatusOverlayThemeResolver.Resolve(appsUseLightTheme).ToString());
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "FlowIME.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
