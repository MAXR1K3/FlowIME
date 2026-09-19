namespace FlowIME.Windows.Context;

internal readonly record struct PixelRect(
    int Left,
    int Top,
    int Right,
    int Bottom)
{
    public int Width => Math.Max(0, Right - Left);

    public int Height => Math.Max(0, Bottom - Top);

    public bool IsEmpty => Width == 0 || Height == 0;
}

/// <summary>
/// Privacy-safe presentation facts about a top-level window. This snapshot contains
/// geometry/state only: no title, URL, typed text, or document content.
/// </summary>
internal sealed record WindowPresentationSnapshot(
    bool IsVisible,
    bool IsMinimized,
    bool IsCloaked,
    PixelRect? WindowBounds,
    PixelRect? MonitorBounds)
{
    public bool IsFullscreen(int edgeTolerancePixels = 4)
    {
        if (edgeTolerancePixels < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(edgeTolerancePixels));
        }

        if (!IsVisible || IsMinimized || IsCloaked ||
            WindowBounds is not { } window ||
            MonitorBounds is not { } monitor ||
            window.IsEmpty || monitor.IsEmpty)
        {
            return false;
        }

        // Use monitor bounds rather than work-area bounds. A normal maximized window
        // with a visible taskbar therefore does not count as fullscreen. Extended
        // frame bounds are preferred by the native probe, so invisible resize borders
        // do not create false negatives on ordinary borderless fullscreen windows.
        return Math.Abs(window.Left - monitor.Left) <= edgeTolerancePixels &&
               Math.Abs(window.Top - monitor.Top) <= edgeTolerancePixels &&
               Math.Abs(window.Right - monitor.Right) <= edgeTolerancePixels &&
               Math.Abs(window.Bottom - monitor.Bottom) <= edgeTolerancePixels;
    }
}
