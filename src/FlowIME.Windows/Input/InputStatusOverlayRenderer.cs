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
        int opacityPercent)
    {
        var scale = Math.Max(1d, dpi / 96d);
        var logical = size switch
        {
            InputStatusOverlaySize.Small => new OverlayVisualMetrics(58, 34, 10, 14),
            InputStatusOverlaySize.Large => new OverlayVisualMetrics(88, 48, 14, 19),
            _ => new OverlayVisualMetrics(72, 40, 12, 16)
        };
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
            Color = new SKColor(15, 23, 42, 44),
            ImageFilter = SKImageFilter.CreateDropShadowOnly(
                0,
                (float)(2 * scale),
                (float)(5 * scale),
                (float)(5 * scale),
                new SKColor(15, 23, 42, 56))
        })
        {
            canvas.DrawRoundRect(surfaceRect, radius, radius, shadowPaint);
        }

        using (var surfacePaint = new SKPaint { IsAntialias = true })
        using (var surfaceShader = SKShader.CreateLinearGradient(
            new SKPoint(surfaceRect.Left, surfaceRect.Top),
            new SKPoint(surfaceRect.Left, surfaceRect.Bottom),
            [new SKColor(253, 253, 254), new SKColor(244, 246, 249)],
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
            Color = new SKColor(207, 213, 223, 210)
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

        var accentWidth = Math.Max(3f, (float)(3 * scale));
        var accentHeight = Math.Max(12f, (float)(14 * scale));
        var accentRect = new SKRect(
            surfaceRect.Left + (float)(9 * scale),
            surfaceRect.MidY - (accentHeight / 2f),
            surfaceRect.Left + (float)(9 * scale) + accentWidth,
            surfaceRect.MidY + (accentHeight / 2f));
        using (var accentPaint = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(59, 130, 246)
        })
        {
            canvas.DrawRoundRect(accentRect, accentWidth / 2f, accentWidth / 2f, accentPaint);
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
            Color = new SKColor(28, 32, 39)
        };
        var metrics = font.Metrics;
        var baseline = surfaceRect.MidY - ((metrics.Ascent + metrics.Descent) / 2f);
        var opticalOffset = (float)(3 * scale);
        canvas.DrawText(
            label,
            surfaceRect.MidX + opticalOffset,
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
        int FontSize);
}
