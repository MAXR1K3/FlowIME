using FlowIME.Windows.Interop;

namespace FlowIME.Windows.Applications;

internal sealed class Win32TopLevelWindowSource : ITopLevelWindowSource
{
    public IReadOnlyList<nint> EnumerateVisibleTopLevelWindows()
    {
        var windows = new List<nint>();
        User32Native.EnumWindows(
            (hwnd, _) =>
            {
                if (hwnd != 0 &&
                    User32Native.IsWindowVisible(hwnd) &&
                    User32Native.GetWindow(hwnd, User32Native.GwOwner) == 0)
                {
                    windows.Add(hwnd);
                }

                return true;
            },
            0);

        return windows;
    }
}
