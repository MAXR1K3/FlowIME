using FlowIME.Core.Settings;
using FlowIME.Windows.Interop;

namespace FlowIME.Windows.Input;

internal readonly record struct InputStatusOverlayPoint(int X, int Y);

internal static class InputStatusOverlayPlacement
{
    internal static InputStatusOverlayPoint Resolve(
        InputStatusOverlayPosition position,
        User32Native.Rect workArea,
        int width,
        int height,
        int margin,
        User32Native.Rect? caretBounds,
        int caretGap)
    {
        if (position == InputStatusOverlayPosition.Caret && caretBounds is { } caret)
        {
            var x = caret.Right + caretGap;
            var y = caret.Bottom + caretGap;
            if (y + height > workArea.Bottom - margin)
            {
                y = caret.Top - height - caretGap;
            }

            return new InputStatusOverlayPoint(
                Math.Clamp(x, workArea.Left + margin, workArea.Right - width - margin),
                Math.Clamp(y, workArea.Top + margin, workArea.Bottom - height - margin));
        }

        if (position == InputStatusOverlayPosition.Caret)
        {
            position = InputStatusOverlayPosition.BottomCenter;
        }

        var left = workArea.Left + margin;
        var centerX = workArea.Left + ((workArea.Right - workArea.Left - width) / 2);
        var right = workArea.Right - width - margin;
        var top = workArea.Top + margin;
        var centerY = workArea.Top + ((workArea.Bottom - workArea.Top - height) / 2);
        var bottom = workArea.Bottom - height - margin;

        return position switch
        {
            InputStatusOverlayPosition.TopLeft => new(left, top),
            InputStatusOverlayPosition.TopCenter => new(centerX, top),
            InputStatusOverlayPosition.TopRight => new(right, top),
            InputStatusOverlayPosition.Center => new(centerX, centerY),
            InputStatusOverlayPosition.BottomLeft => new(left, bottom),
            InputStatusOverlayPosition.BottomRight => new(right, bottom),
            _ => new(centerX, bottom)
        };
    }
}
