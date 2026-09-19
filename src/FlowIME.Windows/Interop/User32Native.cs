using System.Runtime.InteropServices;

namespace FlowIME.Windows.Interop;

internal static class User32Native
{
    internal const uint EventSystemForeground = 0x0003;
    internal const uint EventObjectFocus = 0x8005;
    internal const uint WineventOutOfContext = 0x0000;
    internal const uint WineventSkipOwnProcess = 0x0002;
    internal const uint WmQuit = 0x0012;
    internal const uint WmInputLangChangeRequest = 0x0050;
    internal const uint WmImeControl = 0x0283;
    internal const uint SmtoAbortIfHung = 0x0002;
    internal const uint GwOwner = 4;
    internal const uint MonitorDefaultToNearest = 0x00000002;

    internal delegate bool EnumWindowsProc(nint hwnd, nint lParam);

    internal delegate void WinEventProc(
        nint hWinEventHook,
        uint eventType,
        nint hwnd,
        int idObject,
        int idChild,
        uint idEventThread,
        uint dwmsEventTime);

    internal delegate nint LowLevelKeyboardProc(
        int nCode,
        nuint wParam,
        nint lParam);


    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumWindows(
        EnumWindowsProc callback,
        nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(nint hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsIconic(nint hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(
        nint hwnd,
        out Rect rect);

    [DllImport("user32.dll")]
    internal static extern nint MonitorFromWindow(
        nint hwnd,
        uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetMonitorInfoW(
        nint monitor,
        ref MonitorInfo monitorInfo);

    [DllImport("user32.dll")]
    internal static extern nint GetWindow(nint hwnd, uint command);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern nint SetWinEventHook(
        uint eventMin,
        uint eventMax,
        nint hmodWinEventProc,
        WinEventProc lpfnWinEventProc,
        uint idProcess,
        uint idThread,
        uint dwFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnhookWinEvent(nint hWinEventHook);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint SetWindowsHookExW(
        int idHook,
        LowLevelKeyboardProc callback,
        nint module,
        uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("user32.dll")]
    internal static extern nint CallNextHookEx(
        nint hook,
        int nCode,
        nuint wParam,
        nint lParam);

    [DllImport("user32.dll")]
    internal static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    internal static extern int GetKeyboardLayoutList(
        int nBuff,
        [Out] nint[]? lpList);

    [DllImport("user32.dll")]
    internal static extern nint GetKeyboardLayout(uint idThread);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern int GetMessageW(
        out Message message,
        nint hwnd,
        uint messageFilterMin,
        uint messageFilterMax);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool TranslateMessage(in Message message);

    [DllImport("user32.dll")]
    internal static extern nint DispatchMessageW(in Message message);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PostMessageW(
        nint hwnd,
        uint message,
        nuint wParam,
        nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PostThreadMessageW(
        uint idThread,
        uint message,
        nuint wParam,
        nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern uint GetWindowThreadProcessId(
        nint hwnd,
        out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetGUIThreadInfo(
        uint idThread,
        ref GuiThreadInfo guiThreadInfo);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern int GetWindowTextLengthW(nint hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern int GetWindowTextW(
        nint hwnd,
        [Out] char[] text,
        int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern int GetClassNameW(
        nint hwnd,
        [Out] char[] className,
        int maxCount);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern nint SendMessageTimeoutW(
        nint hwnd,
        uint message,
        nuint wParam,
        nint lParam,
        uint flags,
        uint timeoutMilliseconds,
        out nuint result);

    [StructLayout(LayoutKind.Sequential)]
    internal struct KbdLlHookStruct
    {
        internal uint VkCode;
        internal uint ScanCode;
        internal uint Flags;
        internal uint Time;
        internal nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct GuiThreadInfo
    {
        internal uint CbSize;
        internal uint Flags;
        internal nint HwndActive;
        internal nint HwndFocus;
        internal nint HwndCapture;
        internal nint HwndMenuOwner;
        internal nint HwndMoveSize;
        internal nint HwndCaret;
        internal Rect RcCaret;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct MonitorInfo
    {
        internal uint Size;
        internal Rect Monitor;
        internal Rect WorkArea;
        internal uint Flags;

        internal static MonitorInfo Create() =>
            new()
            {
                Size = (uint)Marshal.SizeOf<MonitorInfo>()
            };
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Message
    {
        internal nint Hwnd;
        internal uint Value;
        internal nuint WParam;
        internal nint LParam;
        internal uint Time;
        internal Point Pt;
        internal uint LPrivate;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Point
    {
        internal int X;
        internal int Y;
    }
}
