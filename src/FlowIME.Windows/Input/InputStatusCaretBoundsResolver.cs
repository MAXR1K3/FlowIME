using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using FlowIME.Windows.Interop;

namespace FlowIME.Windows.Input;

internal sealed class InputStatusCaretBoundsResolver(
    Func<nint, User32Native.Rect?> win32Resolver,
    Func<nint, User32Native.Rect?> uiAutomationResolver)
{
    private readonly Func<nint, User32Native.Rect?> _win32Resolver =
        win32Resolver ?? throw new ArgumentNullException(nameof(win32Resolver));
    private readonly Func<nint, User32Native.Rect?> _uiAutomationResolver =
        uiAutomationResolver ?? throw new ArgumentNullException(nameof(uiAutomationResolver));

    internal User32Native.Rect? Resolve(nint anchor) =>
        _win32Resolver(anchor) ?? _uiAutomationResolver(anchor);
}

internal static class UiAutomationCaretBoundsProvider
{
    internal static User32Native.Rect? TryGetBounds(nint anchor)
    {
        if (anchor == 0)
        {
            return null;
        }

        try
        {
            var focusedElement = AutomationElement.FocusedElement;
            if (focusedElement is null)
            {
                return null;
            }

            _ = User32Native.GetWindowThreadProcessId(anchor, out var anchorProcessId);
            if (anchorProcessId == 0 || focusedElement.Current.ProcessId != anchorProcessId)
            {
                return null;
            }

            if (!focusedElement.TryGetCurrentPattern(TextPattern.Pattern, out var pattern) ||
                pattern is not TextPattern textPattern)
            {
                return null;
            }

            var selection = textPattern.GetSelection();
            if (selection.Length == 0)
            {
                return null;
            }

            var range = selection[0];
            var rectangles = range.GetBoundingRectangles();
            if (rectangles.Length > 0)
            {
                return CreateCaretBounds(rectangles[0], useRightEdge: false);
            }

            var adjacentRange = range.Clone();
            if (adjacentRange.MoveEndpointByUnit(
                    TextPatternRangeEndpoint.End,
                    TextUnit.Character,
                    1) > 0)
            {
                rectangles = adjacentRange.GetBoundingRectangles();
                if (rectangles.Length > 0)
                {
                    return CreateCaretBounds(rectangles[0], useRightEdge: false);
                }
            }

            adjacentRange = range.Clone();
            if (adjacentRange.MoveEndpointByUnit(
                    TextPatternRangeEndpoint.Start,
                    TextUnit.Character,
                    -1) < 0)
            {
                rectangles = adjacentRange.GetBoundingRectangles();
                if (rectangles.Length > 0)
                {
                    return CreateCaretBounds(rectangles[^1], useRightEdge: true);
                }
            }

            return null;
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (COMException)
        {
            return null;
        }
        catch (OverflowException)
        {
            return null;
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    internal static User32Native.Rect CreateCaretBounds(
        System.Windows.Rect bounds,
        bool useRightEdge)
    {
        if (!double.IsFinite(bounds.X) ||
            !double.IsFinite(bounds.Y) ||
            !double.IsFinite(bounds.Width) ||
            !double.IsFinite(bounds.Height))
        {
            throw new ArgumentOutOfRangeException(nameof(bounds));
        }

        var x = checked((int)Math.Round(useRightEdge ? bounds.Right : bounds.Left));
        var top = checked((int)Math.Round(bounds.Top));
        var height = Math.Max(1, checked((int)Math.Round(bounds.Height)));
        return new User32Native.Rect
        {
            Left = x,
            Top = top,
            Right = checked(x + 1),
            Bottom = checked(top + height)
        };
    }
}