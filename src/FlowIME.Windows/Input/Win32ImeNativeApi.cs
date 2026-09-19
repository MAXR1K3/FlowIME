using System.Runtime.InteropServices;
using FlowIME.Windows.Interop;

namespace FlowIME.Windows.Input;

internal sealed class Win32ImeNativeApi : IImeNativeApi
{
    private const uint TimeoutMilliseconds = 100;

    public nint GetDefaultImeWindow(nint targetWindow) =>
        Imm32Native.ImmGetDefaultIMEWnd(targetWindow);

    public bool TrySendImeControl(
        nint imeWindow,
        nuint command,
        nint parameter,
        out nuint result,
        out int nativeError)
    {
        // SendMessageTimeout can return zero without overwriting the thread's
        // last-error value. Clear it first so a failure never reports a stale error.
        Marshal.SetLastPInvokeError(0);
        var sendResult = User32Native.SendMessageTimeoutW(
            imeWindow,
            User32Native.WmImeControl,
            command,
            parameter,
            User32Native.SmtoAbortIfHung,
            TimeoutMilliseconds,
            out result);

        if (sendResult != 0)
        {
            nativeError = 0;
            return true;
        }

        nativeError = Marshal.GetLastPInvokeError();
        return false;
    }
}
