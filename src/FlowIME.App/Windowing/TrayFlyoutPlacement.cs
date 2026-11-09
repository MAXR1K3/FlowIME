using Windows.Graphics;

namespace FlowIME.App.Windowing;

/// <summary>
/// Places the tray flyout next to the pointer the way Windows 11 places its
/// own notification-area flyouts: centred on the click, opening away from the
/// taskbar, and always fully inside the work area.
/// </summary>
internal static class TrayFlyoutPlacement
{
    /// <summary>Gap between the flyout and the taskbar or screen edge, in DIPs.</summary>
    internal const int MarginDips = 12;

    internal static RectInt32 Calculate(PointInt32 pointer, SizeInt32 size, RectInt32 workArea, double scale)
    {
        var margin = (int)Math.Round(MarginDips * scale);
        var minX = workArea.X + margin;
        var maxX = workArea.X + workArea.Width - size.Width - margin;
        var minY = workArea.Y + margin;
        var maxY = workArea.Y + workArea.Height - size.Height - margin;

        var x = Clamp(pointer.X - size.Width / 2, minX, maxX);

        // A pointer in the lower half means a bottom (or side) taskbar: open
        // upwards. A top taskbar puts the pointer above the work area.
        var opensUp = pointer.Y >= workArea.Y + workArea.Height / 2;
        var y = opensUp ? maxY : minY;
        if (pointer.Y > workArea.Y && pointer.Y < workArea.Y + workArea.Height)
        {
            // Pointer inside the work area (side taskbar or overflow): stay near it.
            y = Clamp(opensUp ? pointer.Y - size.Height - margin : pointer.Y + margin, minY, maxY);
        }

        return new RectInt32(x, y, size.Width, size.Height);
    }

    // Prefers the lower bound when the flyout is larger than the work area.
    private static int Clamp(int value, int min, int max) => Math.Max(min, Math.Min(value, max));
}
