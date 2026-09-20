using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FlowIME.App.Services;

/// <summary>
/// Minimal Win32 notification-area host. It owns a hidden native window on a
/// dedicated thread so the tray lifecycle is independent from the WinUI window.
/// </summary>
internal sealed class TrayIconService : IDisposable
{
    private const uint WmDestroy = 0x0002;
    private const uint WmClose = 0x0010;
    private const uint WmNull = 0x0000;
    private const uint WmLButtonUp = 0x0202;
    private const uint WmRButtonUp = 0x0205;
    private const uint WmApp = 0x8000;
    private const uint CallbackMessage = WmApp + 0x31;

    private const uint NimAdd = 0x00000000;
    private const uint NimDelete = 0x00000002;
    private const uint NimSetFocus = 0x00000003;
    private const uint NifMessage = 0x00000001;
    private const uint NifIcon = 0x00000002;
    private const uint NifTip = 0x00000004;

    private const uint MfString = 0x00000000;
    private const uint MfSeparator = 0x00000800;
    private const uint TpmRightButton = 0x0002;
    private const uint TpmReturnCmd = 0x0100;
    private const uint ImageIcon = 1;
    private const uint LrLoadFromFile = 0x0010;
    private const uint LrDefaultSize = 0x0040;

    private const uint OpenCommand = 1001;
    private const uint ToggleAutomationCommand = 1002;
    private const uint ExitCommand = 1003;
    private const uint NotifyForThisSession = 0;


    private readonly Action _openRequested;
    private readonly Func<bool> _isAutomationEnabled;
    private readonly Action _toggleAutomationRequested;
    private readonly Action<string> _systemRecoveryRequested;
    private readonly Action _exitRequested;
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _started = new(false);
    private readonly string _windowClassName;
    private WndProc? _windowProc;
    private nint _window;
    private nint _icon;
    private uint _taskbarCreatedMessage;
    private Exception? _startupException;
    private bool _sessionNotificationsRegistered;
    private bool _disposed;

    internal TrayIconService(
        Action openRequested,
        Func<bool> isAutomationEnabled,
        Action toggleAutomationRequested,
        Action<string> systemRecoveryRequested,
        Action exitRequested)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "FlowIME tray integration requires Windows.");
        }

        _openRequested = openRequested ?? throw new ArgumentNullException(nameof(openRequested));
        _isAutomationEnabled = isAutomationEnabled ??
            throw new ArgumentNullException(nameof(isAutomationEnabled));
        _toggleAutomationRequested = toggleAutomationRequested ??
            throw new ArgumentNullException(nameof(toggleAutomationRequested));
        _systemRecoveryRequested = systemRecoveryRequested ??
            throw new ArgumentNullException(nameof(systemRecoveryRequested));
        _exitRequested = exitRequested ?? throw new ArgumentNullException(nameof(exitRequested));
        _windowClassName = $"FlowIME.Tray.{Environment.ProcessId}";

        _thread = new Thread(ThreadMain)
        {
            IsBackground = true,
            Name = "FlowIME.Tray"
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();

        if (!_started.Wait(TimeSpan.FromSeconds(5)))
        {
            throw new TimeoutException("FlowIME tray initialization timed out.");
        }

        if (_startupException is not null)
        {
            throw new InvalidOperationException(
                "FlowIME tray initialization failed.",
                _startupException);
        }
    }

    private void ThreadMain()
    {
        nint module = 0;
        try
        {
            module = GetModuleHandleW(null);
            if (module == 0)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            _icon = LoadImageW(
                0,
                Path.Combine(
                    AppContext.BaseDirectory,
                    "Assets",
                    "Brand",
                    "Generated",
                    "FlowIME.ico"),
                ImageIcon,
                0,
                0,
                LrLoadFromFile | LrDefaultSize);
            if (_icon == 0)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            _windowProc = WindowProc;

            var windowClass = new WndClassEx
            {
                CbSize = checked((uint)Marshal.SizeOf<WndClassEx>()),
                LpfnWndProc = Marshal.GetFunctionPointerForDelegate(_windowProc),
                HInstance = module,
                LpszClassName = _windowClassName,
                HIcon = _icon,
                HIconSm = _icon
            };

            if (RegisterClassExW(ref windowClass) == 0)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            _window = CreateWindowExW(
                0,
                _windowClassName,
                "FlowIME",
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                module,
                0);

            if (_window == 0)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            _taskbarCreatedMessage = RegisterWindowMessageW("TaskbarCreated");

            // The hidden tray HWND already survives while the visible WinUI window
            // is hidden, making it a reliable place to observe user-session unlock.
            // Failure to register WTS notifications is non-fatal; power-resume and
            // the normal foreground/focus hooks still keep FlowIME usable.
            _sessionNotificationsRegistered =
                WTSRegisterSessionNotification(_window, NotifyForThisSession);
            if (!_sessionNotificationsRegistered)
            {
                Trace.WriteLine(
                    $"[FlowIME.Lifecycle] utc={DateTimeOffset.UtcNow:O} " +
                    $"stage=wts-session-register result=failed " +
                    $"nativeError={Marshal.GetLastPInvokeError()}");
            }

            AddIcon(throwOnFailure: true);
            _started.Set();

            while (true)
            {
                var result = GetMessageW(out var message, 0, 0, 0);
                if (result == 0)
                {
                    break;
                }

                if (result < 0)
                {
                    throw new Win32Exception(Marshal.GetLastPInvokeError());
                }

                _ = TranslateMessage(in message);
                _ = DispatchMessageW(in message);
            }
        }
        catch (Exception ex)
        {
            _startupException ??= ex;
            _started.Set();
        }
        finally
        {
            RemoveIcon();

            if (_sessionNotificationsRegistered && _window != 0)
            {
                _ = WTSUnRegisterSessionNotification(_window);
                _sessionNotificationsRegistered = false;
            }

            if (_window != 0)
            {
                _ = DestroyWindow(_window);
                _window = 0;
            }

            if (module != 0)
            {
                _ = UnregisterClassW(_windowClassName, module);
            }

            if (_icon != 0)
            {
                _ = DestroyIcon(_icon);
                _icon = 0;
            }
        }
    }

    private nint WindowProc(nint hwnd, uint message, nuint wParam, nint lParam)
    {
        if (_taskbarCreatedMessage != 0 && message == _taskbarCreatedMessage)
        {
            AddIcon(throwOnFailure: false);
            return 0;
        }

        if (SystemRecoverySignal.IsRecoveryMessage(message, wParam))
        {
            var reason = SystemRecoverySignal.Describe(message, wParam);
            SafeInvoke(() => _systemRecoveryRequested(reason));
            return message == SystemRecoverySignal.WmPowerBroadcast ? 1 : 0;
        }

        if (message == CallbackMessage)
        {
            var mouseMessage = unchecked((uint)lParam.ToInt64());
            switch (mouseMessage)
            {
                case WmLButtonUp:
                    SafeInvoke(_openRequested);
                    return 0;
                case WmRButtonUp:
                    ShowContextMenu(hwnd);
                    return 0;
            }
        }

        switch (message)
        {
            case WmClose:
                if (_sessionNotificationsRegistered)
                {
                    _ = WTSUnRegisterSessionNotification(hwnd);
                    _sessionNotificationsRegistered = false;
                }

                RemoveIcon();
                _ = DestroyWindow(hwnd);
                return 0;
            case WmDestroy:
                if (_window == hwnd)
                {
                    _window = 0;
                }

                PostQuitMessage(0);
                return 0;
            default:
                return DefWindowProcW(hwnd, message, wParam, lParam);
        }
    }

    private void ShowContextMenu(nint hwnd)
    {
        var menu = CreatePopupMenu();
        if (menu == 0)
        {
            return;
        }

        try
        {
            _ = AppendMenuW(menu, MfString, OpenCommand, "打开 FlowIME");
            _ = AppendMenuW(
                menu,
                MfString,
                ToggleAutomationCommand,
                GetAutomationToggleLabel());
            _ = AppendMenuW(menu, MfSeparator, 0, null);
            _ = AppendMenuW(menu, MfString, ExitCommand, "退出");

            if (!GetCursorPos(out var point))
            {
                return;
            }

            _ = SetForegroundWindow(hwnd);
            var selected = TrackPopupMenu(
                menu,
                TpmRightButton | TpmReturnCmd,
                point.X,
                point.Y,
                0,
                hwnd,
                0);

            switch (selected)
            {
                case OpenCommand:
                    SafeInvoke(_openRequested);
                    break;
                case ToggleAutomationCommand:
                    SafeInvoke(_toggleAutomationRequested);
                    break;
                case ExitCommand:
                    SafeInvoke(_exitRequested);
                    break;
            }

            // Required by the notification-area context-menu guidance so a
            // dismissed popup reliably releases foreground-menu state.
            _ = PostMessageW(hwnd, WmNull, 0, 0);
            var focusData = CreateIconData();
            _ = Shell_NotifyIconW(NimSetFocus, ref focusData);
        }
        finally
        {
            _ = DestroyMenu(menu);
        }
    }

    private string GetAutomationToggleLabel()
    {
        try
        {
            return _isAutomationEnabled() ? "暂停自动切换" : "恢复自动切换";
        }
        catch
        {
            return "暂停自动切换";
        }
    }

    private void AddIcon(bool throwOnFailure)
    {
        if (_window == 0)
        {
            return;
        }

        var iconData = CreateIconData();
        if (Shell_NotifyIconW(NimAdd, ref iconData))
        {
            return;
        }

        if (throwOnFailure)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
    }

    private void RemoveIcon()
    {
        if (_window == 0)
        {
            return;
        }

        var iconData = CreateIconData();
        _ = Shell_NotifyIconW(NimDelete, ref iconData);
    }

    private NotifyIconData CreateIconData() =>
        new()
        {
            CbSize = checked((uint)Marshal.SizeOf<NotifyIconData>()),
            HWnd = _window,
            UId = 1,
            UFlags = NifMessage | NifIcon | NifTip,
            UCallbackMessage = CallbackMessage,
            HIcon = _icon,
            SzTip = "FlowIME · 输入状态自动化",
            SzInfo = string.Empty,
            SzInfoTitle = string.Empty
        };

    private static void SafeInvoke(Action action)
    {
        try
        {
            action();
        }
        catch
        {
            // Tray callbacks are advisory UI actions. A callback failure must not
            // terminate the native tray thread or the automation process.
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_window != 0)
        {
            _ = PostMessageW(_window, WmClose, 0, 0);
        }

        _thread.Join(TimeSpan.FromSeconds(2));
        _started.Dispose();
    }

    private delegate nint WndProc(nint hwnd, uint message, nuint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClassEx
    {
        internal uint CbSize;
        internal uint Style;
        internal nint LpfnWndProc;
        internal int CbClsExtra;
        internal int CbWndExtra;
        internal nint HInstance;
        internal nint HIcon;
        internal nint HCursor;
        internal nint HbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] internal string? LpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] internal string LpszClassName;
        internal nint HIconSm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        internal uint CbSize;
        internal nint HWnd;
        internal uint UId;
        internal uint UFlags;
        internal uint UCallbackMessage;
        internal nint HIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string SzTip;
        internal uint DwState;
        internal uint DwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] internal string SzInfo;
        internal uint TimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] internal string SzInfoTitle;
        internal uint DwInfoFlags;
        internal Guid GuidItem;
        internal nint HBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        internal nint HWnd;
        internal uint Message;
        internal nuint WParam;
        internal nint LParam;
        internal uint Time;
        internal NativePoint Point;
        internal uint Private;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        internal int X;
        internal int Y;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint GetModuleHandleW(string? moduleName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassExW(ref WndClassEx windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterClassW(string className, nint instance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowExW(
        uint exStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        nint parent,
        nint menu,
        nint instance,
        nint parameter);

    [DllImport("user32.dll")]
    private static extern nint DefWindowProcW(
        nint hwnd,
        uint message,
        nuint wParam,
        nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(nint hwnd);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int exitCode);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetMessageW(
        out NativeMessage message,
        nint hwnd,
        uint filterMin,
        uint filterMax);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(in NativeMessage message);

    [DllImport("user32.dll")]
    private static extern nint DispatchMessageW(in NativeMessage message);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessageW(string message);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessageW(
        nint hwnd,
        uint message,
        nuint wParam,
        nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint LoadImageW(
        nint instance,
        string name,
        uint type,
        int width,
        int height,
        uint loadFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint icon);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIconW(uint message, ref NotifyIconData data);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenuW(
        nint menu,
        uint flags,
        nuint itemId,
        string? text);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(nint menu);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint TrackPopupMenu(
        nint menu,
        uint flags,
        int x,
        int y,
        int reserved,
        nint hwnd,
        nint rect);
    [DllImport("wtsapi32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSRegisterSessionNotification(nint window, uint flags);

    [DllImport("wtsapi32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSUnRegisterSessionNotification(nint window);

}
