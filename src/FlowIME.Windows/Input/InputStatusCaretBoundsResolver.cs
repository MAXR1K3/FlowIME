using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using FlowIME.Windows.Interop;

namespace FlowIME.Windows.Input;

internal sealed class InputStatusCaretBoundsResolver(
    Func<nint, User32Native.Rect?> win32Resolver,
    Func<nint, User32Native.Rect?> uiAutomationResolver,
    Func<nint, bool> isFocusedTextEntry)
{
    private readonly Func<nint, User32Native.Rect?> _win32Resolver =
        win32Resolver ?? throw new ArgumentNullException(nameof(win32Resolver));
    private readonly Func<nint, User32Native.Rect?> _uiAutomationResolver =
        uiAutomationResolver ?? throw new ArgumentNullException(nameof(uiAutomationResolver));
    private readonly Func<nint, bool> _isFocusedTextEntry =
        isFocusedTextEntry ?? throw new ArgumentNullException(nameof(isFocusedTextEntry));

    internal User32Native.Rect? Resolve(nint anchor) =>
        _isFocusedTextEntry(anchor)
            ? _win32Resolver(anchor) ?? _uiAutomationResolver(anchor)
            : null;
}

internal static class UiAutomationCaretBoundsProvider
{
    internal static bool IsEditableTextControl(
        ControlType controlType,
        bool isEnabled,
        bool isReadOnly) =>
        controlType == ControlType.Edit && isEnabled && !isReadOnly;

    internal static bool IsFocusedTextEntry(nint anchor)
    {
        if (anchor == 0)
        {
            return false;
        }

        try
        {
            var focusedElement = AutomationElement.FocusedElement;
            _ = User32Native.GetWindowThreadProcessId(anchor, out var anchorProcessId);
            if (focusedElement is not null && anchorProcessId != 0 &&
                focusedElement.Current.ProcessId == anchorProcessId &&
                BelongsToAnchorWindow(focusedElement, anchor))
            {
                // A TextPattern selection also exists in selectable, read-only
                // documents. Only an enabled, writable Edit control is input evidence.
                if (!focusedElement.Current.IsEnabled)
                {
                    return false;
                }

                if (focusedElement.Current.ControlType != ControlType.Edit)
                {
                    return IsWritableNativeEdit(anchor);
                }

                if (focusedElement.TryGetCurrentPattern(ValuePattern.Pattern, out var value) &&
                    value is ValuePattern valuePattern)
                {
                    return IsEditableTextControl(
                        focusedElement.Current.ControlType,
                        isEnabled: true,
                        valuePattern.Current.IsReadOnly);
                }

                return IsWritableNativeEdit(anchor);
            }
        }
        catch (ElementNotAvailableException)
        {
        }
        catch (InvalidOperationException)
        {
        }
        catch (COMException)
        {
        }

        // Native Edit controls are still usable when their UIA provider is absent.
        // Do not accept arbitrary HWND caret owners (e.g. selectable documents).
        return IsWritableNativeEdit(anchor);
    }

    private static bool IsWritableNativeEdit(nint anchor)
    {
        var thread = User32Native.GetWindowThreadProcessId(anchor, out _);
        var info = new User32Native.GuiThreadInfo
        {
            CbSize = checked((uint)Marshal.SizeOf<User32Native.GuiThreadInfo>())
        };
        var root = GetAncestor(anchor, 2);
        if (thread == 0 || root == 0 ||
            !User32Native.GetGUIThreadInfo(thread, ref info) ||
            info.HwndFocus == 0 || GetAncestor(info.HwndFocus, 2) != root)
        {
            return false;
        }

        return HasWritableNativeEditCandidate(
            info.HwndFocus,
            info.HwndCaret,
            window => IsWritableNativeEditWindow(window, root));
    }

    internal static bool HasWritableNativeEditCandidate(
        nint focusedWindow,
        nint caretWindow,
        Func<nint, bool> isWritableEdit) =>
        (caretWindow != 0 && isWritableEdit(caretWindow)) ||
        (focusedWindow != 0 && focusedWindow != caretWindow && isWritableEdit(focusedWindow));

    private static bool IsWritableNativeEditWindow(nint window, nint root)
    {
        if (GetAncestor(window, 2) != root)
        {
            return false;
        }

        var className = new char[256];
        var count = User32Native.GetClassNameW(window, className, className.Length);
        var name = count > 0 ? new string(className, 0, count) : string.Empty;
        return IsWritableNativeEditClass(
            name,
            IsWindowEnabled(window),
            GetWindowLongPtrW(window, -16).ToInt64());
    }

    internal static bool IsWritableNativeEditClass(string name, bool enabled, long style) =>
        enabled &&
        (style & 0x0800) == 0 && // ES_READONLY
        (name.Equals("Edit", StringComparison.OrdinalIgnoreCase) ||
         name.StartsWith("RichEdit", StringComparison.OrdinalIgnoreCase) ||
         name.StartsWith("WindowsForms10.EDIT", StringComparison.OrdinalIgnoreCase));

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

            if (!BelongsToAnchorWindow(focusedElement, anchor))
            {
                return null;
            }

            if (focusedElement.Current.ControlType != ControlType.Edit ||
                !focusedElement.Current.IsEnabled ||
                !focusedElement.TryGetCurrentPattern(TextPattern.Pattern, out var pattern) ||
                pattern is not TextPattern textPattern)
            {
                return null;
            }

            if (!focusedElement.TryGetCurrentPattern(ValuePattern.Pattern, out var value) ||
                value is not ValuePattern valuePattern || valuePattern.Current.IsReadOnly)
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

    private static bool BelongsToAnchorWindow(AutomationElement focusedElement, nint anchor)
    {
        var anchorRoot = GetAncestor(anchor, 2); // GA_ROOT
        if (anchorRoot == 0)
        {
            return false;
        }

        AutomationElement? element = focusedElement;
        for (var depth = 0; depth < 32 && element is not null; depth++)
        {
            var elementWindow = new nint(element.Current.NativeWindowHandle);
            if (elementWindow != 0 && GetAncestor(elementWindow, 2) == anchorRoot)
            {
                return true;
            }

            element = TreeWalker.ControlViewWalker.GetParent(element);
        }

        return false;
    }

    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint window, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowEnabled(nint window);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtrW(nint window, int index);
}