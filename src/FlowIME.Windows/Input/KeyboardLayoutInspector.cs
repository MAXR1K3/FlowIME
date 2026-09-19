using System.Runtime.InteropServices;

namespace FlowIME.Windows.Input;

public sealed class KeyboardLayoutInspector : IKeyboardLayoutInspector
{
    public nint GetKeyboardLayout(uint threadId)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Keyboard-layout inspection requires Windows.");
        }

        return GetKeyboardLayoutNative(threadId);
    }

    [DllImport("user32.dll", EntryPoint = "GetKeyboardLayout")]
    private static extern nint GetKeyboardLayoutNative(uint idThread);
}
