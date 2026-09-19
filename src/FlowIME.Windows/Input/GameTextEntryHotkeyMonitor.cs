using System.ComponentModel;
using System.Runtime.InteropServices;
using FlowIME.Core.Context;
using FlowIME.Windows.Interop;

namespace FlowIME.Windows.Input;

public enum GameTextEntryHotkeyTransition
{
    Enter,
    Exit
}

public sealed record GameTextEntryHotkeyTransitionEvent(
    GameTextEntryHotkeyTransition Transition,
    string ProfileId,
    string ProcessName,
    GameTextEntryKeyGesture Gesture,
    DateTimeOffset Timestamp);

public sealed record GameTextEntryHotkeyMonitorSnapshot(
    bool Installed,
    bool Armed,
    bool GameContext,
    bool GameTextEntry,
    string ProfileId,
    string ProcessName,
    long EnterDetectedCount,
    long ExitDetectedCount,
    string LastGesture,
    DateTimeOffset? LastDetectedAt,
    string? LastError);

/// <summary>
/// Non-blocking low-level keyboard observer for per-game chat profiles. Unlike
/// GameplayHotkeyGuard it never consumes input. It only reports configured state
/// transitions, allowing the game to receive its original chat key unchanged.
/// </summary>
public sealed class GameTextEntryHotkeyMonitor : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const uint WmKeyDown = 0x0100;
    private const uint WmKeyUp = 0x0101;
    private const uint WmSysKeyDown = 0x0104;
    private const uint WmSysKeyUp = 0x0105;
    private const uint LlkhfInjected = 0x00000010;
    private const uint LlkhfLowerIlInjected = 0x00000002;

    private readonly object _lifecycleSync = new();
    private readonly object _diagnosticSync = new();
    private readonly ManualResetEventSlim _started = new(false);
    private readonly Thread _thread;
    private readonly HashSet<uint> _keysDown = [];
    private MonitorRuntime _runtime = MonitorRuntime.Empty;
    private User32Native.LowLevelKeyboardProc? _hookProc;
    private nint _hook;
    private uint _threadId;
    private GameplayModifierState _modifiers;
    private uint? _ignoreNextExitKeyUp;
    private Exception? _startupException;
    private string? _runtimeError;
    private bool _startRequested;
    private bool _disposed;
    private long _enterDetectedCount;
    private long _exitDetectedCount;
    private string _lastGesture = "none";
    private DateTimeOffset? _lastDetectedAt;

    public GameTextEntryHotkeyMonitor()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Game text-entry hotkey monitoring requires Windows.");
        }

        _thread = new Thread(ThreadMain)
        {
            IsBackground = true,
            Name = "FlowIME.GameTextEntryHotkeyMonitor"
        };
    }

    public event Action<GameTextEntryHotkeyTransitionEvent>? TransitionDetected;

    public void Start()
    {
        lock (_lifecycleSync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_startRequested)
            {
                return;
            }

            _startRequested = true;
            _thread.Start();
        }

        if (!_started.Wait(TimeSpan.FromSeconds(5)))
        {
            throw new TimeoutException("Game text-entry hotkey monitor initialization timed out.");
        }

        if (_startupException is not null)
        {
            throw new InvalidOperationException(
                "Game text-entry hotkey monitor initialization failed.",
                _startupException);
        }
    }

    public void UpdateContext(
        bool automationEnabled,
        bool gameContext,
        bool gameTextEntry,
        GameTextEntryProfile? profile,
        string? processName)
    {
        var hotkeyEnabled = profile is not null &&
            profile.Enabled &&
            profile.DetectionMode.HasFlag(GameTextEntryDetectionMode.HotkeyProfile);
        var runtime = new MonitorRuntime(
            automationEnabled,
            gameContext,
            gameTextEntry,
            hotkeyEnabled ? profile : null,
            Sanitize(processName));
        Volatile.Write(ref _runtime, runtime);
    }

    public GameTextEntryHotkeyMonitorSnapshot GetSnapshot()
    {
        var runtime = Volatile.Read(ref _runtime);
        string lastGesture;
        DateTimeOffset? lastAt;
        string? error;
        lock (_diagnosticSync)
        {
            lastGesture = _lastGesture;
            lastAt = _lastDetectedAt;
            error = _runtimeError ?? _startupException?.GetType().Name;
        }

        return new GameTextEntryHotkeyMonitorSnapshot(
            Installed: Interlocked.CompareExchange(ref _hook, IntPtr.Zero, IntPtr.Zero) != IntPtr.Zero,
            Armed: runtime.Armed,
            GameContext: runtime.GameContext,
            GameTextEntry: runtime.GameTextEntry,
            ProfileId: runtime.Profile?.Id ?? "none",
            ProcessName: runtime.ProcessName,
            EnterDetectedCount: Interlocked.Read(ref _enterDetectedCount),
            ExitDetectedCount: Interlocked.Read(ref _exitDetectedCount),
            LastGesture: lastGesture,
            LastDetectedAt: lastAt,
            LastError: error);
    }

    public void Dispose()
    {
        uint threadId;
        lock (_lifecycleSync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            threadId = _threadId;
        }

        if (threadId != 0)
        {
            _ = User32Native.PostThreadMessageW(threadId, User32Native.WmQuit, 0, 0);
        }

        var stopped = !_startRequested || !_thread.IsAlive || _thread.Join(TimeSpan.FromSeconds(2));
        if (stopped)
        {
            _started.Dispose();
        }
    }

    private void ThreadMain()
    {
        try
        {
            _threadId = Kernel32Native.GetCurrentThreadId();
            _hookProc = HookCallback;
            var module = Kernel32Native.GetModuleHandleW(null);
            if (module == 0)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            _hook = User32Native.SetWindowsHookExW(WhKeyboardLl, _hookProc, module, 0);
            if (_hook == 0)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
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
                    throw new Win32Exception(Marshal.GetLastPInvokeError());
                }

                _ = User32Native.TranslateMessage(in message);
                _ = User32Native.DispatchMessageW(in message);
            }
        }
        catch (Exception ex)
        {
            lock (_diagnosticSync)
            {
                _startupException ??= ex;
                _runtimeError = $"{ex.GetType().Name}:{ex.Message}";
            }

            _started.Set();
        }
        finally
        {
            if (_hook != 0)
            {
                _ = User32Native.UnhookWindowsHookEx(_hook);
                _hook = 0;
            }

            _threadId = 0;
        }
    }

    private nint HookCallback(int nCode, nuint wParam, nint lParam)
    {
        if (nCode < 0)
        {
            return User32Native.CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        var message = unchecked((uint)wParam);
        var isDown = message is WmKeyDown or WmSysKeyDown;
        var isUp = message is WmKeyUp or WmSysKeyUp;
        if (!isDown && !isUp)
        {
            return User32Native.CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        var key = Marshal.PtrToStructure<User32Native.KbdLlHookStruct>(lParam);
        if ((key.Flags & (LlkhfInjected | LlkhfLowerIlInjected)) != 0)
        {
            return User32Native.CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        var virtualKey = key.VkCode;
        var runtime = Volatile.Read(ref _runtime);

        if (isDown)
        {
            var firstDown = _keysDown.Add(virtualKey);
            _modifiers = GameplayHotkeyChordMatcher.UpdateModifier(_modifiers, virtualKey, true);
            if (firstDown && runtime.Armed && !runtime.GameTextEntry)
            {
                var gesture = GameTextEntryGestureMatcher.Match(
                    runtime.Profile!.EnterGestures,
                    virtualKey,
                    _modifiers);
                if (gesture is not null)
                {
                    _ignoreNextExitKeyUp = virtualKey;
                    QueueTransition(GameTextEntryHotkeyTransition.Enter, runtime, gesture);
                }
            }
        }
        else
        {
            var ignoreAsOpeningKeyUp = _ignoreNextExitKeyUp == virtualKey;
            if (ignoreAsOpeningKeyUp)
            {
                _ignoreNextExitKeyUp = null;
            }
            else if (runtime.Armed && runtime.GameTextEntry)
            {
                var gesture = GameTextEntryGestureMatcher.Match(
                    runtime.Profile!.ExitGestures,
                    virtualKey,
                    _modifiers);
                if (gesture is not null)
                {
                    QueueTransition(GameTextEntryHotkeyTransition.Exit, runtime, gesture);
                }
            }

            _keysDown.Remove(virtualKey);
            _modifiers = GameplayHotkeyChordMatcher.UpdateModifier(_modifiers, virtualKey, false);
        }

        return User32Native.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private void QueueTransition(
        GameTextEntryHotkeyTransition transition,
        MonitorRuntime runtime,
        GameTextEntryKeyGesture gesture)
    {
        if (transition == GameTextEntryHotkeyTransition.Enter)
        {
            Interlocked.Increment(ref _enterDetectedCount);
        }
        else
        {
            Interlocked.Increment(ref _exitDetectedCount);
        }

        var timestamp = DateTimeOffset.UtcNow;
        lock (_diagnosticSync)
        {
            _lastGesture = $"{transition}:{GameTextEntryKeyGestureParser.Format(gesture)}";
            _lastDetectedAt = timestamp;
        }

        var notification = new GameTextEntryHotkeyTransitionEvent(
            transition,
            runtime.Profile!.Id,
            runtime.ProcessName,
            gesture,
            timestamp);
        ThreadPool.UnsafeQueueUserWorkItem(
            static state => state.Item1.RaiseTransition(state.Item2),
            (this, notification),
            preferLocal: false);
    }

    private void RaiseTransition(GameTextEntryHotkeyTransitionEvent notification)
    {
        try
        {
            TransitionDetected?.Invoke(notification);
        }
        catch (Exception ex)
        {
            lock (_diagnosticSync)
            {
                _runtimeError = $"observer:{ex.GetType().Name}";
            }
        }
    }

    private static string Sanitize(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? "none"
            : value.Trim().Replace('|', '_').Replace('\r', ' ').Replace('\n', ' ');

    private sealed record MonitorRuntime(
        bool AutomationEnabled,
        bool GameContext,
        bool GameTextEntry,
        GameTextEntryProfile? Profile,
        string ProcessName)
    {
        internal static MonitorRuntime Empty { get; } =
            new(false, false, false, null, "none");

        internal bool Armed =>
            AutomationEnabled && GameContext && Profile is not null;
    }
}


internal static class GameTextEntryGestureMatcher
{
    internal static GameTextEntryKeyGesture? Match(
        IReadOnlyList<GameTextEntryKeyGesture> gestures,
        uint virtualKey,
        GameplayModifierState modifiers)
    {
        var activeModifiers = GameTextEntryModifierKeys.None;
        if (modifiers.Ctrl) activeModifiers |= GameTextEntryModifierKeys.Control;
        if (modifiers.Alt) activeModifiers |= GameTextEntryModifierKeys.Alt;
        if (modifiers.Shift) activeModifiers |= GameTextEntryModifierKeys.Shift;
        if (modifiers.Win) activeModifiers |= GameTextEntryModifierKeys.Windows;

        return gestures.FirstOrDefault(gesture =>
            gesture.VirtualKey == virtualKey && gesture.Modifiers == activeModifiers);
    }
}
