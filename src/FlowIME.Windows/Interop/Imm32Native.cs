using System.Runtime.InteropServices;

namespace FlowIME.Windows.Interop;

internal static class Imm32Native
{
    [DllImport("imm32.dll")]
    internal static extern nint ImmGetDefaultIMEWnd(nint hwnd);
}
