namespace FlowIME.App.Windowing;

internal readonly record struct WindowPixelSize(int Width, int Height);

internal static class MainWindowSizePolicy
{
    internal const int PreferredWidth = 1120;
    internal const int PreferredHeight = 760;
    internal const int MinimumWidth = 920;
    internal const int MinimumHeight = 640;
    internal const int MaximumWidth = 1280;
    internal const int MaximumHeight = 900;

    internal static WindowPixelSize Clamp(
        int width,
        int height,
        int workAreaWidth,
        int workAreaHeight,
        uint dpi)
    {
        var scale = dpi > 0 ? dpi / 96d : 1d;
        var minimumWidth = Math.Min(workAreaWidth, Scale(MinimumWidth, scale));
        var minimumHeight = Math.Min(workAreaHeight, Scale(MinimumHeight, scale));
        var maximumWidth = Math.Min(workAreaWidth, Scale(MaximumWidth, scale));
        var maximumHeight = Math.Min(workAreaHeight, Scale(MaximumHeight, scale));

        return new WindowPixelSize(
            Math.Clamp(width, minimumWidth, maximumWidth),
            Math.Clamp(height, minimumHeight, maximumHeight));
    }

    internal static WindowPixelSize Preferred(uint dpi)
    {
        var scale = dpi > 0 ? dpi / 96d : 1d;
        return new WindowPixelSize(
            Scale(PreferredWidth, scale),
            Scale(PreferredHeight, scale));
    }

    private static int Scale(int value, double scale) =>
        Math.Max(1, (int)Math.Round(value * scale));
}
