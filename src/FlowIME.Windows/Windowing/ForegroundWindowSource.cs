using System.ComponentModel;
using FlowIME.Core.Abstractions;
using FlowIME.Core.Models;
using FlowIME.Windows.Interop;

namespace FlowIME.Windows.Windowing;

public sealed class ForegroundWindowSource : IForegroundWindowSource, IInputFocusSource
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(3);

    private readonly Thread _messageThread;
    private readonly ManualResetEventSlim _started = new(false);
    private readonly User32Native.WinEventProc _winEventProc;

    private Exception? _startupException;
    private nint _foregroundHook;
    private nint _focusHook;
    private uint _nativeThreadId;
    private int _disposed;

    public ForegroundWindowSource()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Foreground/focus window events require Windows.");
        }

        _winEventProc = OnWinEvent;
        _messageThread = new Thread(MessageLoop)
        {
            IsBackground = true,
            Name = "FlowIME.WindowActivitySource"
        };

        _messageThread.Start();

        if (!_started.Wait(StartupTimeout))
        {
            Dispose();
            throw new TimeoutException("Timed out while starting the window activity event thread.");
        }

        if (_startupException is not null)
        {
            var startupException = _startupException;
            Dispose();
            throw new InvalidOperationException(
                "Unable to initialize the WinEvent hooks.",
                startupException);
        }
    }

    public event EventHandler<ForegroundWindowChangedEventArgs>? ForegroundWindowChanged;

    public event EventHandler<InputFocusChangedEventArgs>? InputFocusChanged;

    public nint GetCurrentForegroundWindow()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        return User32Native.GetForegroundWindow();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        var threadId = Volatile.Read(ref _nativeThreadId);
        if (threadId != 0)
        {
            _ = User32Native.PostThreadMessageW(
                threadId,
                User32Native.WmQuit,
                0,
                0);
        }

        var stopped = !_messageThread.IsAlive || _messageThread.Join(ShutdownTimeout);
        if (stopped)
        {
            _started.Dispose();
        }
        // Do not abort a native message-loop thread. If shutdown times out, leave the
        // startup event undisposed rather than racing the still-running thread. The
        // message thread is background-only, so the process can still exit safely.
    }

    private void MessageLoop()
    {
        try
        {
            _nativeThreadId = Kernel32Native.GetCurrentThreadId();
            _foregroundHook = User32Native.SetWinEventHook(
                User32Native.EventSystemForeground,
                User32Native.EventSystemForeground,
                0,
                _winEventProc,
                0,
                0,
                // Keep own-process foreground transitions: they are important
                // cancellation signals for a rule operation that was started in
                // another application just before the user opened FlowIME.
                User32Native.WineventOutOfContext);

            if (_foregroundHook == 0)
            {
                throw new Win32Exception(
                    System.Runtime.InteropServices.Marshal.GetLastWin32Error(),
                    "SetWinEventHook(EVENT_SYSTEM_FOREGROUND) failed.");
            }

            _focusHook = User32Native.SetWinEventHook(
                User32Native.EventObjectFocus,
                User32Native.EventObjectFocus,
                0,
                _winEventProc,
                0,
                0,
                User32Native.WineventOutOfContext | User32Native.WineventSkipOwnProcess);

            if (_focusHook == 0)
            {
                throw new Win32Exception(
                    System.Runtime.InteropServices.Marshal.GetLastWin32Error(),
                    "SetWinEventHook(EVENT_OBJECT_FOCUS) failed.");
            }

            _started.Set();

            while (true)
            {
                var result = User32Native.GetMessageW(out var message, 0, 0, 0);
                if (result == 0)
                {
                    break;
                }

                if (result < 0)
                {
                    throw new Win32Exception(
                        System.Runtime.InteropServices.Marshal.GetLastWin32Error(),
                        "GetMessage failed in the window activity event thread.");
                }

                _ = User32Native.TranslateMessage(in message);
                _ = User32Native.DispatchMessageW(in message);
            }
        }
        catch (Exception exception)
        {
            if (!_started.IsSet)
            {
                _startupException = exception;
                _started.Set();
            }
        }
        finally
        {
            if (_focusHook != 0)
            {
                _ = User32Native.UnhookWinEvent(_focusHook);
                _focusHook = 0;
            }

            if (_foregroundHook != 0)
            {
                _ = User32Native.UnhookWinEvent(_foregroundHook);
                _foregroundHook = 0;
            }
        }
    }

    private void OnWinEvent(
        nint hWinEventHook,
        uint eventType,
        nint hwnd,
        int idObject,
        int idChild,
        uint idEventThread,
        uint dwmsEventTime)
    {
        _ = hWinEventHook;
        _ = idEventThread;
        _ = dwmsEventTime;

        if (hwnd == 0)
        {
            return;
        }

        try
        {
            if (eventType == User32Native.EventSystemForeground)
            {
                ForegroundWindowChanged?.Invoke(
                    this,
                    new ForegroundWindowChangedEventArgs(hwnd, DateTimeOffset.UtcNow));
                return;
            }

            if (eventType == User32Native.EventObjectFocus)
            {
                // Accessibility focus events can arrive for background windows,
                // menus, tooltips, and helper HWNDs. Keep the filter intentionally
                // process-based rather than OBJID-based so Chromium/Electron/WinUI
                // text controls are not excluded. The coordinator will still
                // re-read the actual foreground HWND before doing any mutation.
                if (!BelongsToCurrentForegroundProcess(hwnd))
                {
                    return;
                }

                InputFocusChanged?.Invoke(
                    this,
                    new InputFocusChangedEventArgs(
                        hwnd,
                        idObject,
                        idChild,
                        DateTimeOffset.UtcNow));
            }
        }
        catch
        {
            // A consumer exception must never tear down the native WinEvent message loop.
        }
    }
    private static bool BelongsToCurrentForegroundProcess(nint hwnd)
    {
        var foreground = User32Native.GetForegroundWindow();
        if (foreground == 0)
        {
            return false;
        }

        _ = User32Native.GetWindowThreadProcessId(foreground, out var foregroundProcessId);
        _ = User32Native.GetWindowThreadProcessId(hwnd, out var eventProcessId);

        return foregroundProcessId != 0 &&
               eventProcessId != 0 &&
               foregroundProcessId == eventProcessId;
    }

}
