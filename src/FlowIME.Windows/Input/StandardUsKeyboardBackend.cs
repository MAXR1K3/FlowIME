using System.Diagnostics;
using FlowIME.Core.Abstractions;
using FlowIME.Core.Automation;
using FlowIME.Core.Models;

namespace FlowIME.Windows.Input;

public sealed class StandardUsKeyboardBackend : IStandardUsKeyboardBackend
{
    public const string StandardUsKlid = GameplayKeyboardBaseline.StandardUsKlid;

    private const string BackendName = "Standard US keyboard / target thread HKL";
    private const int VerificationAttempts = 4;
    private static readonly TimeSpan VerificationDelay = TimeSpan.FromMilliseconds(20);
    private static readonly InputState UnknownState =
        new(null, InputMode.Unknown, 0);

    private readonly IFocusWindowResolver _focusResolver;
    private readonly IGameplayKeyboardNativeApi _native;
    private readonly IAutomationDelay _delay;

    public StandardUsKeyboardBackend()
        : this(
            new FocusWindowResolver(),
            new Win32GameplayKeyboardNativeApi(),
            SystemAutomationDelay.Instance)
    {
    }

    internal StandardUsKeyboardBackend(
        IFocusWindowResolver focusResolver,
        IGameplayKeyboardNativeApi native,
        IAutomationDelay delay)
    {
        _focusResolver = focusResolver ?? throw new ArgumentNullException(nameof(focusResolver));
        _native = native ?? throw new ArgumentNullException(nameof(native));
        _delay = delay ?? throw new ArgumentNullException(nameof(delay));
    }

    public async ValueTask<InputOperationResult> ApplyAsync(
        WindowContext window,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(window);
        cancellationToken.ThrowIfCancellationRequested();
        var started = Stopwatch.GetTimestamp();
        var before = UnknownState;

        try
        {
            var standardUsLayout = _native
                .GetKeyboardLayouts()
                .FirstOrDefault(GameplayKeyboardBaseline.IsStandardUsKeyboard);
            if (standardUsLayout == 0)
            {
                return Failure(before, before, "us-keyboard-unavailable", started);
            }

            var focus = _focusResolver.Resolve(window.Hwnd);
            var targetWindow = focus.EffectiveInputWindow != 0
                ? focus.EffectiveInputWindow
                : window.Hwnd;
            if (targetWindow == 0)
            {
                return Failure(before, before, "focus-unavailable", started);
            }

            var targetThreadId = _native.GetWindowThreadId(
                targetWindow,
                out var targetProcessId,
                out var targetThreadError);
            if (targetThreadId == 0)
            {
                var error = targetThreadError == 0
                    ? "target-thread-unavailable"
                    : $"target-thread-win32-{targetThreadError}";
                return Failure(before, before, error, started);
            }

            if (targetThreadId != window.ThreadId || targetProcessId != window.ProcessId)
            {
                return Failure(before, before, "target-thread-mismatch", started);
            }

            var beforeLayout = _native.GetKeyboardLayout(targetThreadId);
            before = CreateState(beforeLayout);
            if (SameLayout(beforeLayout, standardUsLayout))
            {
                return Success(before, before, started);
            }

            if (!_native.RequestInputLanguageChange(
                    targetWindow,
                    standardUsLayout,
                    out var errorCode))
            {
                var error = errorCode == 0
                    ? "input-language-request-failed"
                    : $"win32-{errorCode}";
                return Failure(before, before, error, started);
            }

            var lastObserved = before;
            for (var attempt = 0; attempt < VerificationAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!HasStableTargetIdentity(
                        targetWindow,
                        window,
                        targetThreadId,
                        out var identityError))
                {
                    return Failure(before, lastObserved, identityError, started);
                }

                var observedLayout = _native.GetKeyboardLayout(targetThreadId);
                lastObserved = CreateState(observedLayout);
                if (SameLayout(observedLayout, standardUsLayout))
                {
                    return Success(before, lastObserved, started);
                }

                if (attempt + 1 < VerificationAttempts)
                {
                    await _delay
                        .DelayAsync(VerificationDelay, cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            return Failure(before, lastObserved, "target-layout-not-applied", started);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Failure(
                before,
                before,
                $"native-operation-{ex.GetType().Name}",
                started);
        }
    }

    private bool HasStableTargetIdentity(
        nint targetWindow,
        WindowContext expectedWindow,
        uint expectedThreadId,
        out string errorCode)
    {
        var currentThreadId = _native.GetWindowThreadId(
            targetWindow,
            out var currentProcessId,
            out var nativeError);
        if (currentThreadId == 0)
        {
            errorCode = nativeError == 0
                ? "target-identity-unavailable"
                : $"target-identity-win32-{nativeError}";
            return false;
        }

        if (currentThreadId != expectedThreadId ||
            currentThreadId != expectedWindow.ThreadId ||
            currentProcessId != expectedWindow.ProcessId)
        {
            errorCode = "target-identity-changed";
            return false;
        }

        errorCode = string.Empty;
        return true;
    }

    private static bool SameLayout(nint left, nint right) =>
        unchecked((uint)(nuint)left) == unchecked((uint)(nuint)right);

    private static InputState CreateState(nint layout) =>
        GameplayKeyboardBaseline.IsStandardUsKeyboard(layout)
            ? new InputState("标准美式键盘", InputMode.English, layout)
            : new InputState(null, InputMode.Unknown, layout);

    private static InputOperationResult Success(
        InputState before,
        InputState after,
        long started) =>
        new(
            Success: true,
            Before: before,
            After: after,
            Backend: BackendName,
            ErrorCode: null,
            Duration: Stopwatch.GetElapsedTime(started));

    private static InputOperationResult Failure(
        InputState before,
        InputState after,
        string errorCode,
        long started) =>
        new(
            Success: false,
            Before: before,
            After: after,
            Backend: BackendName,
            ErrorCode: errorCode,
            Duration: Stopwatch.GetElapsedTime(started));
}
