using FlowIME.Windows.Interop;

namespace FlowIME.Windows.Context;

internal sealed class Win32WindowPresentationProbe : IWindowPresentationProbe
{
    public WindowPresentationSnapshot Capture(nint hwnd)
    {
        if (hwnd == 0)
        {
            return Empty();
        }

        var visible = User32Native.IsWindowVisible(hwnd);
        var minimized = User32Native.IsIconic(hwnd);
        var cloaked = TryGetCloaked(hwnd);
        var windowBounds = TryGetWindowBounds(hwnd);
        var monitorBounds = TryGetMonitorBounds(hwnd);

        return new WindowPresentationSnapshot(
            visible,
            minimized,
            cloaked,
            windowBounds,
            monitorBounds);
    }

    private static PixelRect? TryGetWindowBounds(nint hwnd)
    {
        if (DwmapiNative.TryGetExtendedFrameBounds(hwnd, out var extended))
        {
            return ToPixelRect(extended);
        }

        return User32Native.GetWindowRect(hwnd, out var fallback)
            ? ToPixelRect(fallback)
            : null;
    }

    private static PixelRect? TryGetMonitorBounds(nint hwnd)
    {
        var monitor = User32Native.MonitorFromWindow(
            hwnd,
            User32Native.MonitorDefaultToNearest);
        if (monitor == 0)
        {
            return null;
        }

        var info = User32Native.MonitorInfo.Create();
        if (!User32Native.GetMonitorInfoW(monitor, ref info))
        {
            return null;
        }

        return ToPixelRect(info.Monitor);
    }

    private static bool TryGetCloaked(nint hwnd) =>
        DwmapiNative.TryGetCloaked(hwnd, out var cloaked) && cloaked;

    private static PixelRect ToPixelRect(User32Native.Rect rect) =>
        new(rect.Left, rect.Top, rect.Right, rect.Bottom);

    private static WindowPresentationSnapshot Empty() =>
        new(
            IsVisible: false,
            IsMinimized: false,
            IsCloaked: false,
            WindowBounds: null,
            MonitorBounds: null);
}
