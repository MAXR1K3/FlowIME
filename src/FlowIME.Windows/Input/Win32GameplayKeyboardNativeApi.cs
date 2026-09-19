using System.Runtime.InteropServices;
using FlowIME.Windows.Interop;

namespace FlowIME.Windows.Input;

internal sealed class Win32GameplayKeyboardNativeApi : IGameplayKeyboardNativeApi
{
    public IReadOnlyList<nint> GetKeyboardLayouts()
    {
        var count = User32Native.GetKeyboardLayoutList(0, null);
        if (count <= 0)
        {
            return Array.Empty<nint>();
        }

        var layouts = new nint[count];
        var copied = User32Native.GetKeyboardLayoutList(layouts.Length, layouts);
        if (copied <= 0)
        {
            return Array.Empty<nint>();
        }

        return copied == layouts.Length
            ? layouts
            : layouts.Take(copied).ToArray();
    }

    public nint GetKeyboardLayout(uint threadId) =>
        User32Native.GetKeyboardLayout(threadId);

    public bool RequestInputLanguageChange(
        nint hwnd,
        nint keyboardLayout,
        out int errorCode)
    {
        Marshal.SetLastPInvokeError(0);
        var result = User32Native.PostMessageW(
            hwnd,
            User32Native.WmInputLangChangeRequest,
            0,
            keyboardLayout);

        errorCode = result
            ? 0
            : Marshal.GetLastPInvokeError();
        return result;
    }
}
