using FlowIME.Core.Settings;
using SkiaSharp;

namespace FlowIME.Windows.Input;

internal readonly record struct OverlayPixel(byte Red, byte Green, byte Blue, byte Alpha);

internal sealed class InputStatusOverlayFrame : IDisposable
{
    private readonly SKBitmap _bitmap;

    internal InputStatusOverlayFrame(
        SKBitmap bitmap,
        int surfaceLeft,
        int surfaceTop,
        string typefaceFamily)
    {
        _bitmap = bitmap;
        SurfaceLeft = surfaceLeft;
        SurfaceTop = surfaceTop;
        TypefaceFamily = typefaceFamily;
    }

    internal int Width => _bitmap.Width;
    internal int Height => _bitmap.Height;
    internal int SurfaceLeft { get; }
    internal int SurfaceTop { get; }
    internal string TypefaceFamily { get; }
    internal nint Pixels => _bitmap.GetPixels();
    internal int ByteCount => _bitmap.ByteCount;

    internal OverlayPixel GetPixel(int x, int y)
    {
        var color = _bitmap.GetPixel(x, y);
        return new OverlayPixel(color.Red, color.Green, color.Blue, color.Alpha);
    }

    public void Dispose() => _bitmap.Dispose();
}

internal static class InputStatusOverlayRenderer
{
    private const string CjkTypefaceFamily = "Microsoft YaHei UI";
    private const string LatinTypefaceFamily = "Segoe UI Variable Display";

    internal static InputStatusOverlayFrame Render(
        string label,
        InputStatusOverlaySize size,
        uint dpi,
        int opacityPercent,
        OverlayColorScheme colorScheme = OverlayColorScheme.Light,
        SKBitmap? brandIcon = null)
    {
        var scale = Math.Max(1d, dpi / 96d);
        var logical = size switch
        {
            InputStatusOverlaySize.Small => new OverlayVisualMetrics(68, 34, 10, 14, 16),
            InputStatusOverlaySize.Large => new OverlayVisualMetrics(104, 48, 14, 19, 22),
            _ => new OverlayVisualMetrics(84, 40, 12, 16, 18)
        };
        var extraCharacters = Math.Max(0, label.Length - 4);
        if (extraCharacters > 0)
        {
            logical = logical with
            {
                Width = logical.Width + Math.Min(220, extraCharacters * 20)
            };
        }
        var palette = OverlayPalette.For(colorScheme);
        var shadowPadding = Math.Max(6, (int)Math.Ceiling(8 * scale));
        var surfaceWidth = Math.Max(1, (int)Math.Round(logical.Width * scale));
        var surfaceHeight = Math.Max(1, (int)Math.Round(logical.Height * scale));
        var width = surfaceWidth + (shadowPadding * 2);
        var height = surfaceHeight + (shadowPadding * 2);
        var surfaceRect = new SKRect(
            shadowPadding,
            shadowPadding,
            shadowPadding + surfaceWidth,
            shadowPadding + surfaceHeight);
        var bitmap = new SKBitmap(new SKImageInfo(
            width,
            height,
            SKColorType.Bgra8888,
            SKAlphaType.Premul));

        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);

        var radius = (float)(logical.CornerRadius * scale);
        using (var shadowPaint = new SKPaint
        {
            IsAntialias = true,
            Color = palette.Shadow,
            ImageFilter = SKImageFilter.CreateDropShadowOnly(
                0,
                (float)(2 * scale),
                (float)(5 * scale),
                (float)(5 * scale),
                palette.Shadow)
        })
        {
            canvas.DrawRoundRect(surfaceRect, radius, radius, shadowPaint);
        }

        using (var surfacePaint = new SKPaint { IsAntialias = true })
        using (var surfaceShader = SKShader.CreateLinearGradient(
            new SKPoint(surfaceRect.Left, surfaceRect.Top),
            new SKPoint(surfaceRect.Left, surfaceRect.Bottom),
            [palette.SurfaceTop, palette.SurfaceBottom],
            null,
            SKShaderTileMode.Clamp))
        {
            surfacePaint.Shader = surfaceShader;
            surfacePaint.Color = SKColors.White.WithAlpha(
                checked((byte)Math.Round(255 * Math.Clamp(opacityPercent, 40, 100) / 100d)));
            canvas.DrawRoundRect(surfaceRect, radius, radius, surfacePaint);
        }

        using (var borderPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = Math.Max(1f, (float)scale),
            Color = palette.Border
        })
        {
            var inset = borderPaint.StrokeWidth / 2f;
            canvas.DrawRoundRect(
                new SKRect(
                    surfaceRect.Left + inset,
                    surfaceRect.Top + inset,
                    surfaceRect.Right - inset,
                    surfaceRect.Bottom - inset),
                radius - inset,
                radius - inset,
                borderPaint);
        }

        var iconSize = (float)(logical.IconSize * scale);
        var iconRect = new SKRect(
            surfaceRect.Left + (float)(9 * scale),
            surfaceRect.MidY - (iconSize / 2f),
            surfaceRect.Left + (float)(9 * scale) + iconSize,
            surfaceRect.MidY + (iconSize / 2f));
        if (brandIcon is not null)
        {
            // Use the provider's registered silhouette, but tint it with the same
            // foreground role as the label so monochrome vendor icons remain
            // legible when Windows switches between light and dark app themes.
            using var iconPaint = new SKPaint
            {
                IsAntialias = true,
                ColorFilter = SKColorFilter.CreateBlendMode(palette.Text, SKBlendMode.SrcIn)
            };
            canvas.DrawBitmap(
                brandIcon,
                iconRect,
                new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None),
                iconPaint);
        }

        var typefaceFamily = ContainsCjk(label) ? CjkTypefaceFamily : LatinTypefaceFamily;
        using var typeface = SKTypeface.FromFamilyName(
            typefaceFamily,
            SKFontStyleWeight.SemiBold,
            SKFontStyleWidth.Normal,
            SKFontStyleSlant.Upright) ?? SKTypeface.Default;
        using var font = new SKFont(typeface, (float)(logical.FontSize * scale))
        {
            Edging = SKFontEdging.SubpixelAntialias,
            Subpixel = true
        };
        using var textPaint = new SKPaint
        {
            IsAntialias = true,
            Color = palette.Text
        };
        var metrics = font.Metrics;
        var baseline = surfaceRect.MidY - ((metrics.Ascent + metrics.Descent) / 2f);
        var textLeft = brandIcon is null
            ? surfaceRect.Left + (float)(8 * scale)
            : iconRect.Right + (float)(6 * scale);
        var textRight = surfaceRect.Right - (float)(8 * scale);
        canvas.DrawText(
            label,
            textLeft + ((textRight - textLeft) / 2f),
            baseline,
            SKTextAlign.Center,
            font,
            textPaint);
        canvas.Flush();

        return new InputStatusOverlayFrame(
            bitmap,
            shadowPadding,
            shadowPadding,
            typefaceFamily);
    }

    private static bool ContainsCjk(string value) =>
        value.Any(character => character is >= '\u2E80' and <= '\u9FFF');

    private readonly record struct OverlayVisualMetrics(
        int Width,
        int Height,
        int CornerRadius,
        int FontSize,
        int IconSize);

    private readonly record struct OverlayPalette(
        SKColor SurfaceTop,
        SKColor SurfaceBottom,
        SKColor Border,
        SKColor Text,
        SKColor IconBackground,
        SKColor IconForeground,
        SKColor Shadow)
    {
        internal static OverlayPalette For(OverlayColorScheme colorScheme) =>
            colorScheme switch
            {
                OverlayColorScheme.Dark => new OverlayPalette(
                    new SKColor(38, 42, 49),
                    new SKColor(27, 30, 36),
                    new SKColor(82, 90, 103, 220),
                    new SKColor(246, 247, 249),
                    new SKColor(196, 111, 79),
                    new SKColor(255, 250, 247),
                    new SKColor(0, 0, 0, 108)),
                OverlayColorScheme.Light => new OverlayPalette(
                    new SKColor(253, 253, 254),
                    new SKColor(244, 246, 249),
                    new SKColor(207, 213, 223, 210),
                    new SKColor(28, 32, 39),
                    new SKColor(164, 91, 65),
                    new SKColor(255, 250, 247),
                    new SKColor(15, 23, 42, 56)),
                _ => throw new ArgumentOutOfRangeException(nameof(colorScheme))
            };
    }
}
