using System.Runtime.InteropServices;

namespace FlowIME.Windows.Interop;

internal static class DwmapiNative
{
    private const uint DwmwaExtendedFrameBounds = 9;
    private const uint DwmwaCloaked = 14;

    internal static bool TryGetExtendedFrameBounds(
        nint hwnd,
        out User32Native.Rect rect)
    {
        rect = default;
        var result = DwmGetWindowAttribute(
            hwnd,
            DwmwaExtendedFrameBounds,
            ref rect,
            (uint)Marshal.SizeOf<User32Native.Rect>());
        return result >= 0;
    }

    internal static bool TryGetCloaked(nint hwnd, out bool cloaked)
    {
        uint value = 0;
        var result = DwmGetWindowAttribute(
            hwnd,
            DwmwaCloaked,
            ref value,
            sizeof(uint));
        cloaked = result >= 0 && value != 0;
        return result >= 0;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(
        nint hwnd,
        uint attribute,
        ref User32Native.Rect value,
        uint size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(
        nint hwnd,
        uint attribute,
        ref uint value,
        uint size);
}
