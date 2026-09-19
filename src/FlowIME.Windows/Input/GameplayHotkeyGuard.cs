using System.ComponentModel;
using System.Runtime.InteropServices;
using FlowIME.Core.Settings;
using FlowIME.Windows.Interop;

namespace FlowIME.Windows.Input;

public enum GameplayHotkeyChord
{
    None,
    WinSpace,
    CtrlSpace,
    AltShift,
    CtrlShift
}

public sealed record GameplayHotkeyGuardSnapshot(
    bool Installed,
    bool Armed,
    bool AutomationEnabled,
    bool GameContext,
    bool GameTextEntry,
    GameplayHotkeyGuardSettings Settings,
    long SuppressedCount,
    GameplayHotkeyChord LastSuppressedChord,
    DateTimeOffset? LastSuppressedAt,
    string LastSuppressedProcessName,
    string ProcessName,
    string? LastError);

/// <summary>
/// Context-gated low-level keyboard guard for Windows language/IME switching
/// shortcuts. The hook is always narrow: it only suppresses a recognized terminal
/// key while Gameplay is active, FlowIME automation is enabled, and GameTextEntry
/// is not active. It does not inspect typed text or arbitrary key sequences.
/// </summary>
public sealed class GameplayHotkeyGuard : IDisposable
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
    private RuntimeGate _runtime;
    private User32Native.LowLevelKeyboardProc? _hookProc;
    private nint _hook;
    private uint _threadId;
    private Exception? _startupException;
    private string? _runtimeError;
    private bool _startRequested;
    private bool _disposed;

    // Hook-thread-only keyboard state. We deliberately avoid GetAsyncKeyState here:
    // low-level hook callbacks run before the asynchronous key state is updated.
    private GameplayModifierState _modifiers;
    private readonly HashSet<uint> _suppressedKeyUps = new();

    private long _suppressedCount;
    private GameplayHotkeyChord _lastSuppressedChord;
    private DateTimeOffset? _lastSuppressedAt;
    private string _lastSuppressedProcessName = "none";

    public GameplayHotkeyGuard(
        GameplayHotkeyGuardSettings? initialSettings = null)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Gameplay hotkey protection requires Windows.");
        }

        // Start disarmed. AppServices loads the persisted preference and foreground
        // context before the gate can become active.
        _runtime = new RuntimeGate(
            initialSettings ?? GameplayHotkeyGuardSettings.Default,
            AutomationEnabled: false,
            GameContext: false,
            GameTextEntry: false,
            ProcessName: "none");

        _thread = new Thread(ThreadMain)
        {
            IsBackground = true,
            Name = "FlowIME.GameplayHotkeyGuard"
        };
    }

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
            throw new TimeoutException("Gameplay hotkey guard initialization timed out.");
        }

        if (_startupException is not null)
        {
            throw new InvalidOperationException(
                "Gameplay hotkey guard initialization failed.",
                _startupException);
        }
    }

    public void UpdateSettings(GameplayHotkeyGuardSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        UpdateRuntime(current => current with { Settings = settings });
    }

    public void SetAutomationEnabled(bool enabled) =>
        UpdateRuntime(current => current with { AutomationEnabled = enabled });

    public void UpdateContext(
        bool gameContext,
        bool gameTextEntry,
        string? processName)
    {
        var sanitizedProcessName = string.IsNullOrWhiteSpace(processName)
            ? "none"
            : processName.Replace('|', '_').Replace('\r', '_').Replace('\n', '_');

        UpdateRuntime(current => current with
        {
            GameContext = gameContext,
            GameTextEntry = gameTextEntry,
            ProcessName = sanitizedProcessName
        });
    }

    public GameplayHotkeyGuardSnapshot GetSnapshot()
    {
        var runtime = Volatile.Read(ref _runtime);
        GameplayHotkeyChord lastChord;
        DateTimeOffset? lastAt;
        string lastProcessName;
        string? error;
        lock (_diagnosticSync)
        {
            lastChord = _lastSuppressedChord;
            lastAt = _lastSuppressedAt;
            lastProcessName = _lastSuppressedProcessName;
            error = _runtimeError ?? _startupException?.GetType().Name;
        }

        return new GameplayHotkeyGuardSnapshot(
            Installed: Interlocked.CompareExchange(ref _hook, IntPtr.Zero, IntPtr.Zero) != IntPtr.Zero,
            Armed: runtime.Armed,
            AutomationEnabled: runtime.AutomationEnabled,
            GameContext: runtime.GameContext,
            GameTextEntry: runtime.GameTextEntry,
            Settings: runtime.Settings,
            SuppressedCount: Interlocked.Read(ref _suppressedCount),
            LastSuppressedChord: lastChord,
            LastSuppressedAt: lastAt,
            LastSuppressedProcessName: lastProcessName,
            ProcessName: runtime.ProcessName,
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
            _ = User32Native.PostThreadMessageW(
                threadId,
                User32Native.WmQuit,
                0,
                0);
        }

        var stopped = !_startRequested ||
            !_thread.IsAlive ||
            _thread.Join(TimeSpan.FromSeconds(2));
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

            _hook = User32Native.SetWindowsHookExW(
                WhKeyboardLl,
                _hookProc,
                module,
                0);
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

        if (isUp && _suppressedKeyUps.Remove(virtualKey))
        {
            UpdateModifierState(virtualKey, isDown: false);
            return 1;
        }

        if (isDown)
        {
            UpdateModifierState(virtualKey, isDown: true);

            var runtime = Volatile.Read(ref _runtime);
            if (runtime.Armed)
            {
                var chord = GameplayHotkeyChordMatcher.MatchKeyDown(
                    virtualKey,
                    _modifiers,
                    runtime.Settings);
                if (chord != GameplayHotkeyChord.None)
                {
                    _suppressedKeyUps.Add(virtualKey);
                    RecordSuppression(chord, runtime.ProcessName);
                    return 1;
                }
            }
        }
        else
        {
            UpdateModifierState(virtualKey, isDown: false);
        }

        return User32Native.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private void UpdateModifierState(uint virtualKey, bool isDown)
    {
        _modifiers = GameplayHotkeyChordMatcher.UpdateModifier(
            _modifiers,
            virtualKey,
            isDown);
    }

    private void RecordSuppression(
        GameplayHotkeyChord chord,
        string processName)
    {
        Interlocked.Increment(ref _suppressedCount);
        lock (_diagnosticSync)
        {
            _lastSuppressedChord = chord;
            _lastSuppressedAt = DateTimeOffset.UtcNow;
            _lastSuppressedProcessName = processName;
        }
    }

    private void UpdateRuntime(Func<RuntimeGate, RuntimeGate> update)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        while (true)
        {
            var current = Volatile.Read(ref _runtime);
            var next = update(current);
            if (ReferenceEquals(
                Interlocked.CompareExchange(ref _runtime, next, current),
                current))
            {
                return;
            }
        }
    }

    private sealed record RuntimeGate(
        GameplayHotkeyGuardSettings Settings,
        bool AutomationEnabled,
        bool GameContext,
        bool GameTextEntry,
        string ProcessName)
    {
        internal bool Armed => GameplayHotkeyGuardGate.ShouldArm(
            Settings,
            AutomationEnabled,
            GameContext,
            GameTextEntry);
    }
}

internal static class GameplayHotkeyGuardGate
{
    internal static bool ShouldArm(
        GameplayHotkeyGuardSettings settings,
        bool automationEnabled,
        bool gameContext,
        bool gameTextEntry)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.Enabled &&
            automationEnabled &&
            gameContext &&
            !gameTextEntry;
    }
}

internal readonly record struct GameplayModifierState(
    bool Ctrl,
    bool Alt,
    bool Shift,
    bool Win);

internal static class GameplayHotkeyChordMatcher
{
    internal const uint VkSpace = 0x20;
    internal const uint VkShift = 0x10;
    internal const uint VkControl = 0x11;
    internal const uint VkMenu = 0x12;
    internal const uint VkLeftShift = 0xA0;
    internal const uint VkRightShift = 0xA1;
    internal const uint VkLeftControl = 0xA2;
    internal const uint VkRightControl = 0xA3;
    internal const uint VkLeftMenu = 0xA4;
    internal const uint VkRightMenu = 0xA5;
    internal const uint VkLeftWin = 0x5B;
    internal const uint VkRightWin = 0x5C;

    internal static GameplayHotkeyChord MatchKeyDown(
        uint virtualKey,
        GameplayModifierState modifiers,
        GameplayHotkeyGuardSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (virtualKey == VkSpace)
        {
            if (settings.BlockWinSpace && modifiers.Win)
            {
                return GameplayHotkeyChord.WinSpace;
            }

            if (settings.BlockCtrlSpace && modifiers.Ctrl)
            {
                return GameplayHotkeyChord.CtrlSpace;
            }
        }

        if (settings.BlockLegacyLanguageHotkeys && IsShift(virtualKey))
        {
            if (modifiers.Alt)
            {
                return GameplayHotkeyChord.AltShift;
            }

            if (modifiers.Ctrl)
            {
                return GameplayHotkeyChord.CtrlShift;
            }
        }

        return GameplayHotkeyChord.None;
    }

    internal static GameplayModifierState UpdateModifier(
        GameplayModifierState state,
        uint virtualKey,
        bool isDown)
    {
        if (IsControl(virtualKey))
        {
            return state with { Ctrl = isDown };
        }

        if (IsAlt(virtualKey))
        {
            return state with { Alt = isDown };
        }

        if (IsShift(virtualKey))
        {
            return state with { Shift = isDown };
        }

        if (IsWin(virtualKey))
        {
            return state with { Win = isDown };
        }

        return state;
    }

    private static bool IsShift(uint virtualKey) =>
        virtualKey is VkShift or VkLeftShift or VkRightShift;

    private static bool IsControl(uint virtualKey) =>
        virtualKey is VkControl or VkLeftControl or VkRightControl;

    private static bool IsAlt(uint virtualKey) =>
        virtualKey is VkMenu or VkLeftMenu or VkRightMenu;

    private static bool IsWin(uint virtualKey) =>
        virtualKey is VkLeftWin or VkRightWin;
}
