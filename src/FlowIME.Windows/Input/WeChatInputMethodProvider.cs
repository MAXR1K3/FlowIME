using System.Diagnostics;
using FlowIME.Core.Abstractions;
using FlowIME.Core.Automation;
using FlowIME.Core.Models;

namespace FlowIME.Windows.Input;

/// <summary>
/// WeChat Input Method provider validated during P5B discovery on Windows 11.
///
/// Unlike Microsoft Pinyin, WeChat exposes Chinese/English through IMM open
/// status while its conversion mode remains 0x00000001 in both states:
/// Open = Chinese, Closed = English.
/// </summary>
public sealed class WeChatInputMethodProvider : IInputMethodProvider
{
    public const string ProviderId = InputMethodProviderIds.WeChat;
    private const ushort ChineseSimplifiedLanguageId = 0x0804;

    private static readonly InputMethodProviderDescriptor ProviderDescriptor =
        new(
            ProviderId,
            "微信输入法",
            new InputMethodProviderCapabilities(
                CanDetectActiveProfile: true,
                CanActivateProfile: true,
                CanReadMode: true,
                CanSetChinese: true,
                CanSetEnglish: true,
                RequiresPostActivationSettling: true));

    private const string BackendName = "WeChat Input Method / Focused IMM OpenStatus";
    private const int RequiredStableSamplesAfterProfileSwitch = 4;
    private const int ObserveOnlySamplesAfterProfileSwitch = 4;
    private const int MaxProfileSwitchChecks = 16;
    private static readonly TimeSpan ProfileSwitchCheckInterval =
        TimeSpan.FromMilliseconds(25);

    private readonly IFocusWindowResolver _focusResolver;
    private readonly IInputStateAccessor _openStatus;
    private readonly IKeyboardLayoutInspector _keyboardLayout;
    private readonly IWeChatInputMethodProfileActivator _profileActivator;
    private readonly IAutomationDelay _delay;
    private readonly IInputProfileInspector _profileInspector;

    public WeChatInputMethodProvider()
        : this(
            new FocusWindowResolver(),
            new LegacyImeOpenStatusAccessor(),
            new KeyboardLayoutInspector(),
            new TsfWeChatInputMethodProfileActivator(),
            SystemAutomationDelay.Instance,
            new TsfInputProfileInspector())
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "WeChat Input Method automation requires Windows.");
        }
    }

    internal WeChatInputMethodProvider(
        IFocusWindowResolver focusResolver,
        IInputStateAccessor openStatus,
        IKeyboardLayoutInspector keyboardLayout,
        IWeChatInputMethodProfileActivator profileActivator,
        IAutomationDelay? delay = null,
        IInputProfileInspector? profileInspector = null)
    {
        _focusResolver = focusResolver ?? throw new ArgumentNullException(nameof(focusResolver));
        _openStatus = openStatus ?? throw new ArgumentNullException(nameof(openStatus));
        _keyboardLayout = keyboardLayout ?? throw new ArgumentNullException(nameof(keyboardLayout));
        _profileActivator = profileActivator ?? throw new ArgumentNullException(nameof(profileActivator));
        _delay = delay ?? SystemAutomationDelay.Instance;
        _profileInspector = profileInspector ?? new TsfInputProfileInspector();
    }

    public InputMethodProviderDescriptor Descriptor => ProviderDescriptor;

    public ValueTask<InputMethodProviderDetectionResult> DetectAsync(
        WindowContext window,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(window);
        cancellationToken.ThrowIfCancellationRequested();

        var profile = _profileInspector.GetActiveKeyboardProfile();
        return ValueTask.FromResult(
            profile.Success
                ? new InputMethodProviderDetectionResult(
                    Success: true,
                    IsActive: WeChatInputMethodProfileIdentity.IsMatch(profile))
                : new InputMethodProviderDetectionResult(
                    Success: false,
                    IsActive: false,
                    ErrorCode: "profile-detection-failed"));
    }

    public ValueTask<InputState> GetStateAsync(
        WindowContext window,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(window);
        cancellationToken.ThrowIfCancellationRequested();

        var layout = _keyboardLayout.GetKeyboardLayout(window.ThreadId);
        var profile = _profileInspector.GetActiveKeyboardProfile();
        if (!WeChatInputMethodProfileIdentity.IsMatch(profile) ||
            !TryResolveReliableInputTarget(window.Hwnd, out var inputHwnd))
        {
            return ValueTask.FromResult(UnknownState(layout));
        }

        var result = _openStatus.GetOpenStatus(inputHwnd);
        return ValueTask.FromResult(
            result.Success
                ? StateFromOpenStatus(result.IsOpen, layout)
                : UnknownState(layout));
    }

    public async ValueTask<InputOperationResult> ApplyAsync(
        WindowContext window,
        InputAction action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(window);
        cancellationToken.ThrowIfCancellationRequested();

        var started = Stopwatch.GetTimestamp();
        var layout = _keyboardLayout.GetKeyboardLayout(window.ThreadId);

        if (action == InputAction.Keep)
        {
            return ReadKeepResult(window, layout, started);
        }

        var activation = _profileActivator.ActivateForSession();
        if (!activation.Success || !activation.VerifiedActive)
        {
            var unknown = UnknownState(layout);
            return Result(
                false,
                unknown,
                unknown,
                "profile-activation-failed",
                started);
        }

        var requestedOpen = action switch
        {
            InputAction.Chinese => true,
            InputAction.English => false,
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
        };

        if (activation.WasAlreadyActive && IsCompatibleTargetThreadLayout(layout))
        {
            return ApplyToAlreadyActiveProfile(
                window,
                layout,
                requestedOpen,
                started,
                cancellationToken);
        }

        return await ApplyAfterProfileSwitchAsync(
                window,
                layout,
                requestedOpen,
                started,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private InputOperationResult ReadKeepResult(
        WindowContext window,
        nint layout,
        long started)
    {
        var profile = _profileInspector.GetActiveKeyboardProfile();
        if (!WeChatInputMethodProfileIdentity.IsMatch(profile))
        {
            var unknown = UnknownState(layout);
            return Result(false, unknown, unknown, "profile-not-active", started);
        }

        if (!TryResolveReliableInputTarget(window.Hwnd, out var inputHwnd))
        {
            var unknown = UnknownState(layout);
            return Result(false, unknown, unknown, "focus-unavailable", started);
        }

        var native = _openStatus.GetOpenStatus(inputHwnd);
        if (!native.Success)
        {
            var unknown = UnknownState(layout);
            return Result(false, unknown, unknown, ReadFailureCode(native), started);
        }

        var state = StateFromOpenStatus(native.IsOpen, layout);
        return Result(true, state, state, null, started);
    }

    private InputOperationResult ApplyToAlreadyActiveProfile(
        WindowContext window,
        nint layout,
        bool requestedOpen,
        long started,
        CancellationToken cancellationToken)
    {
        if (!TryResolveReliableInputTarget(window.Hwnd, out var inputHwnd))
        {
            var unknown = UnknownState(layout);
            TraceTarget("focus-unavailable", window.Hwnd, 0, requestedOpen, null);
            return Result(false, unknown, unknown, "focus-unavailable", started);
        }

        var beforeNative = _openStatus.GetOpenStatus(inputHwnd);
        if (!beforeNative.Success)
        {
            var unknown = UnknownState(layout);
            var errorCode = ReadFailureCode(beforeNative);
            TraceTarget(errorCode, window.Hwnd, inputHwnd, requestedOpen, null);
            return Result(false, unknown, unknown, errorCode, started);
        }

        var before = StateFromOpenStatus(beforeNative.IsOpen, layout);
        TraceTarget("read-before", window.Hwnd, inputHwnd, requestedOpen, beforeNative.IsOpen);
        if (beforeNative.IsOpen == requestedOpen)
        {
            return Result(true, before, before, null, started);
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (!TryResolveReliableInputTarget(window.Hwnd, out var mutationHwnd))
        {
            return Result(false, before, UnknownState(layout), "focus-unavailable", started);
        }

        if (mutationHwnd != inputHwnd)
        {
            var currentNative = _openStatus.GetOpenStatus(mutationHwnd);
            if (!currentNative.Success)
            {
                return Result(
                    false,
                    before,
                    UnknownState(layout),
                    ReadFailureCode(currentNative),
                    started);
            }

            var current = StateFromOpenStatus(currentNative.IsOpen, layout);
            if (currentNative.IsOpen == requestedOpen)
            {
                return Result(true, before, current, null, started);
            }
        }

        var profileGuard = _profileActivator.ActivateForSession();
        if (!profileGuard.Success || !profileGuard.VerifiedActive)
        {
            return Result(false, before, UnknownState(layout), "profile-activation-failed", started);
        }

        cancellationToken.ThrowIfCancellationRequested();

        // If the guard had to switch profiles, the resolved IME context may belong
        // to the previous provider. Let the coordinator retry rather than writing
        // WeChat-specific OpenStatus into a transitional context.
        if (!profileGuard.WasAlreadyActive)
        {
            return Result(false, before, UnknownState(layout), "profile-rebound", started);
        }

        var set = _openStatus.SetOpenStatus(mutationHwnd, requestedOpen);
        TraceTarget("write", window.Hwnd, mutationHwnd, requestedOpen, beforeNative.IsOpen);
        if (!set.Success)
        {
            return Result(false, before, before, WriteFailureCode(set), started);
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (!TryResolveReliableInputTarget(window.Hwnd, out var verifyHwnd))
        {
            return Result(false, before, UnknownState(layout), "focus-unavailable", started);
        }

        var afterNative = _openStatus.GetOpenStatus(verifyHwnd);
        if (!afterNative.Success)
        {
            return Result(
                false,
                before,
                UnknownState(layout),
                ReadFailureCode(afterNative, "verify-read-failed"),
                started);
        }

        var after = StateFromOpenStatus(afterNative.IsOpen, layout);
        TraceTarget("read-after", window.Hwnd, verifyHwnd, requestedOpen, afterNative.IsOpen);
        if (afterNative.IsOpen == requestedOpen)
        {
            return Result(true, before, after, null, started);
        }

        return Result(
            false,
            before,
            after,
            verifyHwnd == mutationHwnd ? "verification-failed" : "focus-changed",
            started);
    }

    private async ValueTask<InputOperationResult> ApplyAfterProfileSwitchAsync(
        WindowContext window,
        nint layout,
        bool requestedOpen,
        long started,
        CancellationToken cancellationToken)
    {
        var before = UnknownState(layout);
        var after = UnknownState(layout);
        var haveBefore = false;
        var stableSamples = 0;
        var observedActiveSamples = 0;
        var lastError = "profile-settle-timeout";

        for (var check = 0; check < MaxProfileSwitchChecks; check++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _delay
                .DelayAsync(ProfileSwitchCheckInterval, cancellationToken)
                .ConfigureAwait(false);

            var profile = _profileInspector.GetActiveKeyboardProfile();
            if (!WeChatInputMethodProfileIdentity.IsMatch(profile))
            {
                stableSamples = 0;
                observedActiveSamples = 0;
                lastError = "profile-not-active";

                var reactivation = _profileActivator.ActivateForSession();
                if (!reactivation.Success || !reactivation.VerifiedActive)
                {
                    lastError = "profile-activation-failed";
                }
                continue;
            }

            var currentLayout = _keyboardLayout.GetKeyboardLayout(window.ThreadId);
            if (!IsCompatibleTargetThreadLayout(currentLayout))
            {
                // A session-level TSF profile can already report WeChat while the
                // target GUI thread still owns another input language (notably the
                // standard US keyboard). Reading IMM OpenStatus in that state
                // produces a false Applied result. An 0x0804 HKL plus the exact
                // session-level TSF identity is the strongest public cross-process
                // binding check currently available here; it intentionally does not
                // claim to distinguish two Simplified-Chinese TIPs by HKL alone.
                stableSamples = 0;
                observedActiveSamples = 0;
                lastError = "target-profile-not-bound";
                continue;
            }

            if (!TryResolveReliableInputTarget(window.Hwnd, out var inputHwnd))
            {
                stableSamples = 0;
                observedActiveSamples = 0;
                lastError = "focus-unavailable";
                continue;
            }

            var currentNative = _openStatus.GetOpenStatus(inputHwnd);
            if (!currentNative.Success)
            {
                stableSamples = 0;
                observedActiveSamples = 0;
                lastError = ReadFailureCode(currentNative);
                continue;
            }

            // Count only samples for which the exact WeChat profile is active,
            // a real focused input HWND is available, and OpenStatus is readable.
            // A profile-only sample must never advance the post-activation settle
            // window because the target GUI thread may still own the previous IME.
            observedActiveSamples++;

            var current = StateFromOpenStatus(currentNative.IsOpen, currentLayout);
            if (!haveBefore)
            {
                before = current;
                haveBefore = true;
            }
            after = current;

            if (currentNative.IsOpen == requestedOpen)
            {
                stableSamples++;
                if (stableSamples >= RequiredStableSamplesAfterProfileSwitch)
                {
                    TraceTarget(
                        "profile-settled",
                        window.Hwnd,
                        inputHwnd,
                        requestedOpen,
                        currentNative.IsOpen);
                    return Result(true, before, after, null, started);
                }
                continue;
            }

            stableSamples = 0;

            // P5B-2 proved OpenStatus mutation safe while WeChat is active, but a
            // session-wide TSF activation can become visible before the target GUI
            // thread finishes rebinding. Require four active/readable observations
            // (about 100 ms) before the first write after a profile transition.
            if (observedActiveSamples <= ObserveOnlySamplesAfterProfileSwitch)
            {
                lastError = "profile-settling";
                continue;
            }

            var profileGuard = _profileActivator.ActivateForSession();
            if (!profileGuard.Success || !profileGuard.VerifiedActive)
            {
                lastError = "profile-activation-failed";
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (!profileGuard.WasAlreadyActive)
            {
                observedActiveSamples = 0;
                lastError = "profile-rebound";
                continue;
            }

            var set = _openStatus.SetOpenStatus(inputHwnd, requestedOpen);
            TraceTarget(
                "profile-settle-write",
                window.Hwnd,
                inputHwnd,
                requestedOpen,
                currentNative.IsOpen);
            if (!set.Success)
            {
                lastError = WriteFailureCode(set);
                continue;
            }

            lastError = "verification-failed";
        }

        return Result(false, before, after, lastError, started);
    }

    private bool TryResolveReliableInputTarget(nint topLevelWindow, out nint inputHwnd)
    {
        var focus = _focusResolver.Resolve(topLevelWindow);
        inputHwnd = focus.EffectiveInputWindow;
        return focus.GuiThreadInfoAvailable &&
               focus.Source != FocusWindowSource.TopLevelFallback &&
               inputHwnd != 0;
    }

    private static bool IsCompatibleTargetThreadLayout(nint keyboardLayout)
    {
        if (keyboardLayout == 0)
        {
            return false;
        }

        var value = unchecked((uint)(nuint)keyboardLayout);
        return (ushort)(value & 0xFFFF) == ChineseSimplifiedLanguageId;
    }

    private static InputState StateFromOpenStatus(bool isOpen, nint layout) =>
        new(
            "微信输入法",
            isOpen ? InputMode.Chinese : InputMode.English,
            layout);

    private static InputState UnknownState(nint layout) =>
        new("微信输入法", InputMode.Unknown, layout);

    private static string ReadFailureCode(
        ImeOpenStatusResult result,
        string fallback = "read-failed") =>
        result switch
        {
            { ImeWindow: 0, NativeError: 0 } => "input-context-unavailable",
            { NativeError: 5 } => "input-context-inaccessible",
            _ => fallback
        };

    private static string WriteFailureCode(ImeSetOpenStatusResult result) =>
        result switch
        {
            { ImeWindow: 0, NativeError: 0 } => "input-context-unavailable",
            { NativeError: 5 } => "input-context-inaccessible",
            _ => "write-failed"
        };

    private static void TraceTarget(
        string stage,
        nint topLevelHwnd,
        nint focusHwnd,
        bool requestedOpen,
        bool? actualOpen)
    {
        Trace.WriteLine(
            $"[FlowIME.WeChat] utc={DateTimeOffset.UtcNow:O} stage={stage} " +
            $"foreground=0x{topLevelHwnd:X} focus=0x{focusHwnd:X} " +
            $"requested={(requestedOpen ? "Open" : "Closed")} actual=" +
            (actualOpen is null ? "unknown" : (actualOpen.Value ? "Open" : "Closed")));
    }

    private static InputOperationResult Result(
        bool success,
        InputState before,
        InputState after,
        string? errorCode,
        long started) =>
        new(
            success,
            before,
            after,
            BackendName,
            errorCode,
            Stopwatch.GetElapsedTime(started));
}
