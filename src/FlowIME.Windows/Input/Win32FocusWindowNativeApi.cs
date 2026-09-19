using System.Runtime.InteropServices;
using FlowIME.Windows.Interop;

namespace FlowIME.Windows.Input;

internal sealed class Win32FocusWindowNativeApi : IFocusWindowNativeApi
{
    public uint GetWindowThreadId(nint hwnd, out int nativeError)
    {
        Marshal.SetLastPInvokeError(0);
        var threadId = User32Native.GetWindowThreadProcessId(hwnd, out _);
        nativeError = threadId == 0 ? Marshal.GetLastPInvokeError() : 0;
        return threadId;
    }

    public bool TryGetGuiThreadInfo(
        uint threadId,
        out FocusGuiThreadInfo info,
        out int nativeError)
    {
        var nativeInfo = new User32Native.GuiThreadInfo
        {
            CbSize = checked((uint)Marshal.SizeOf<User32Native.GuiThreadInfo>())
        };

        Marshal.SetLastPInvokeError(0);
        if (!User32Native.GetGUIThreadInfo(threadId, ref nativeInfo))
        {
            nativeError = Marshal.GetLastPInvokeError();
            info = default;
            return false;
        }

        nativeError = 0;
        info = new FocusGuiThreadInfo(
            nativeInfo.HwndActive,
            nativeInfo.HwndFocus,
            nativeInfo.HwndCaret,
            nativeInfo.Flags);
        return true;
    }
}
