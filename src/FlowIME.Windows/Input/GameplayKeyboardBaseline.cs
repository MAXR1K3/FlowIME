using System.Diagnostics;
using FlowIME.Core.Decisions;
using FlowIME.Core.Settings;

namespace FlowIME.Windows.Input;

public enum GameplayKeyboardBaselineOutcome
{
    None,
    Inactive,
    Disabled,
    UsKeyboardUnavailable,
    AlreadyUsKeyboard,
    Requested,
    Applied,
    ApplyFailed
}

public sealed record GameplayKeyboardBaselineSnapshot(
    GameplayKeyboardBaselineSettings Settings,
    bool AutomationEnabled,
    bool GameContext,
    bool GameTextEntry,
    bool UsKeyboardAvailable,
    bool Active,
    string TargetLayout,
    string LastObservedLayout,
    GameplayKeyboardBaselineOutcome LastOutcome,
    string? LastError,
    string? ProcessName,
    DateTimeOffset? LastAttemptAt,
    DateTimeOffset? LastAppliedAt,
    long AppliedCount);

/// <summary>
/// Establishes a pure US-keyboard baseline for positively classified gameplay.
/// It does not install languages/keyboards and never changes the user's Windows
/// language preferences. The standard US keyboard must already be enabled in the
/// current Windows session.
/// </summary>
public sealed class GameplayKeyboardBaseline
{
    public const string StandardUsKlid = "00000409";

    private const ushort EnUsLanguageId = 0x0409;
    private readonly object _sync = new();
    private readonly IGameplayKeyboardNativeApi _native;
    private readonly GameplayKeyboardBaselineState _decisionState;

    private GameplayKeyboardBaselineSettings _settings;
    private bool _automationEnabled = true;
    private bool _gameContext;
    private bool _gameTextEntry;
    private bool _usKeyboardAvailable;
    private nint _usKeyboardLayout;
    private bool _active;
    private nint _lastObservedLayout;
    private GameplayKeyboardBaselineOutcome _lastOutcome;
    private string? _lastError;
    private string? _processName;
    private DateTimeOffset? _lastAttemptAt;
    private DateTimeOffset? _lastAppliedAt;
    private long _appliedCount;

    public GameplayKeyboardBaseline(
        GameplayKeyboardBaselineState decisionState,
        GameplayKeyboardBaselineSettings? settings = null)
        : this(
            decisionState,
            new Win32GameplayKeyboardNativeApi(),
            settings)
    {
    }

    internal GameplayKeyboardBaseline(
        GameplayKeyboardBaselineState decisionState,
        IGameplayKeyboardNativeApi native,
        GameplayKeyboardBaselineSettings? settings = null)
    {
        _decisionState = decisionState ?? throw new ArgumentNullException(nameof(decisionState));
        _native = native ?? throw new ArgumentNullException(nameof(native));
        _settings = settings ?? GameplayKeyboardBaselineSettings.Default;
        RefreshAvailabilityLocked();
        RefreshDecisionStateLocked();
    }

    public void UpdateSettings(GameplayKeyboardBaselineSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        lock (_sync)
        {
            _settings = settings;
            RefreshAvailabilityLocked();
            RefreshDecisionStateLocked();
            if (!settings.Enabled)
            {
                _active = false;
                _lastOutcome = GameplayKeyboardBaselineOutcome.Disabled;
            }
        }
    }

    public void SetAutomationEnabled(bool enabled)
    {
        lock (_sync)
        {
            _automationEnabled = enabled;
            RefreshDecisionStateLocked();
            if (!enabled)
            {
                _active = false;
                _lastOutcome = GameplayKeyboardBaselineOutcome.Inactive;
            }
        }
    }

    public void UpdateContext(
        bool gameContext,
        bool gameTextEntry,
        nint hwnd,
        nint focusHwnd,
        uint threadId,
        string? processName)
    {
        lock (_sync)
        {
            _gameContext = gameContext;
            _gameTextEntry = gameTextEntry;
            if (gameContext)
            {
                _processName = NormalizeToken(processName);
            }

            RefreshAvailabilityLocked();
            RefreshDecisionStateLocked();

            if (!_automationEnabled || !_settings.Enabled)
            {
                _active = false;
                _lastOutcome = _settings.Enabled
                    ? GameplayKeyboardBaselineOutcome.Inactive
                    : GameplayKeyboardBaselineOutcome.Disabled;
                return;
            }

            if (!gameContext || gameTextEntry || hwnd == 0 || threadId == 0)
            {
                // Preserve the last gameplay outcome/process for diagnostics after
                // Alt+Tab. Current activity is expressed by Active/GameContext;
                // overwriting the last result here would make post-game validation
                // impossible, just like the original current-foreground-only issue.
                _active = false;
                return;
            }

            _active = true;
            _lastAttemptAt = DateTimeOffset.UtcNow;

            if (!_usKeyboardAvailable || _usKeyboardLayout == 0)
            {
                _lastOutcome = GameplayKeyboardBaselineOutcome.UsKeyboardUnavailable;
                _lastError = "us-keyboard-unavailable";
                return;
            }

            var current = _native.GetKeyboardLayout(threadId);
            _lastObservedLayout = current;
            if (SameLayout(current, _usKeyboardLayout))
            {
                _lastOutcome = GameplayKeyboardBaselineOutcome.AlreadyUsKeyboard;
                _lastError = null;
                return;
            }

            var targetHwnd = focusHwnd != 0 ? focusHwnd : hwnd;
            if (!_native.RequestInputLanguageChange(targetHwnd, _usKeyboardLayout, out var errorCode))
            {
                _lastOutcome = GameplayKeyboardBaselineOutcome.ApplyFailed;
                _lastError = errorCode == 0
                    ? "input-language-request-failed"
                    : $"win32-{errorCode}";
                Trace.WriteLine(
                    $"[FlowIME.GameplayKeyboard] utc={DateTimeOffset.UtcNow:O} " +
                    $"stage=request result=failed process={_processName ?? "unknown"} " +
                    $"error={_lastError}");
                return;
            }

            // WM_INPUTLANGCHANGEREQUEST is posted to the focused window. The target
            // consumes it asynchronously, so an immediate GetKeyboardLayout check
            // is allowed to still show the previous layout. A later context sample
            // will observe the activated US layout and mark it AlreadyUsKeyboard.
            var verified = _native.GetKeyboardLayout(threadId);
            _lastObservedLayout = verified;
            if (SameLayout(verified, _usKeyboardLayout))
            {
                _lastOutcome = GameplayKeyboardBaselineOutcome.Applied;
                _lastAppliedAt = DateTimeOffset.UtcNow;
                _appliedCount++;
            }
            else
            {
                _lastOutcome = GameplayKeyboardBaselineOutcome.Requested;
            }

            _lastError = null;
            Trace.WriteLine(
                $"[FlowIME.GameplayKeyboard] utc={DateTimeOffset.UtcNow:O} " +
                $"stage=request result=success process={_processName ?? "unknown"} " +
                $"layout={StandardUsKlid} outcome={_lastOutcome}");
        }
    }

    public GameplayKeyboardBaselineSnapshot GetSnapshot()
    {
        lock (_sync)
        {
            RefreshAvailabilityLocked();
            RefreshDecisionStateLocked();
            return new GameplayKeyboardBaselineSnapshot(
                _settings,
                _automationEnabled,
                _gameContext,
                _gameTextEntry,
                _usKeyboardAvailable,
                _active,
                StandardUsKlid,
                FormatLayout(_lastObservedLayout),
                _lastOutcome,
                _lastError,
                _processName,
                _lastAttemptAt,
                _lastAppliedAt,
                _appliedCount);
        }
    }

    public static bool IsStandardUsKeyboard(nint keyboardLayout)
    {
        if (keyboardLayout == 0)
        {
            return false;
        }

        var value = unchecked((uint)(nuint)keyboardLayout);
        var languageId = (ushort)(value & 0xFFFF);
        var deviceId = (ushort)((value >> 16) & 0xFFFF);

        // Standard US commonly appears as HKL 0x04090409. Accepting the raw
        // 0x00000409 form keeps tests and legacy representations compatible,
        // while excluding US-International and other en-US keyboard variants.
        return languageId == EnUsLanguageId &&
               (deviceId == 0 || deviceId == EnUsLanguageId);
    }

    private void RefreshAvailabilityLocked()
    {
        try
        {
            _usKeyboardLayout = _native
                .GetKeyboardLayouts()
                .FirstOrDefault(IsStandardUsKeyboard);
            _usKeyboardAvailable = _usKeyboardLayout != 0;
        }
        catch (Exception ex)
        {
            _usKeyboardLayout = 0;
            _usKeyboardAvailable = false;
            _lastError = $"layout-enumeration-{ex.GetType().Name}";
        }
    }

    private void RefreshDecisionStateLocked()
    {
        _decisionState.SetReady(
            _settings.Enabled &&
            _automationEnabled &&
            _usKeyboardAvailable);
    }

    private static bool SameLayout(nint left, nint right) =>
        unchecked((uint)(nuint)left) == unchecked((uint)(nuint)right);

    private static string FormatLayout(nint layout)
    {
        if (layout == 0)
        {
            return "none";
        }

        return $"0x{unchecked((uint)(nuint)layout):X8}";
    }

    private static string? NormalizeToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().Replace('|', '_').Replace('\r', ' ').Replace('\n', ' ');
    }
}
