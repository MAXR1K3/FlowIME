using System.Diagnostics;
using FlowIME.Core.Abstractions;
using FlowIME.Core.Automation;
using FlowIME.Core.Models;

namespace FlowIME.Windows.Input;

/// <summary>
/// Microsoft Pinyin provider validated on Windows 11.
///
/// The critical requirement is to resolve the target GUI thread's real focused
/// input HWND before calling ImmGetDefaultIMEWnd. Sending IME control messages
/// through the top-level application HWND can report numeric success without
/// changing the text control's real Microsoft Pinyin state.
/// </summary>
public sealed class MicrosoftPinyinProvider : IInputMethodProvider
{
    public const uint ChineseConversionMode = 0x00000401;
    public const uint EnglishConversionMode = 0x00000000;

    public const string ProviderId = InputMethodProviderIds.MicrosoftPinyin;

    private static readonly InputMethodProviderDescriptor ProviderDescriptor =
        new(
            ProviderId,
            "Microsoft Pinyin",
            new InputMethodProviderCapabilities(
                CanDetectActiveProfile: true,
                CanActivateProfile: true,
                CanReadMode: true,
                CanSetChinese: true,
                CanSetEnglish: true,
                RequiresPostActivationSettling: true));

    private const string BackendName = "Microsoft Pinyin / Focused IMM";
    private const int RequiredStableSamplesAfterProfileSwitch = 4;
    private const int MaxProfileSwitchChecks = 12;
    private static readonly TimeSpan ProfileSwitchCheckInterval =
        TimeSpan.FromMilliseconds(25);

    private readonly IFocusWindowResolver _focusResolver;
    private readonly IImeConversionModeAccessor _conversionMode;
    private readonly IKeyboardLayoutInspector _keyboardLayout;
    private readonly IMicrosoftPinyinProfileActivator _profileActivator;
    private readonly IAutomationDelay _delay;
    private readonly IInputProfileInspector _profileInspector;

    public MicrosoftPinyinProvider()
        : this(
            new FocusWindowResolver(),
            new LegacyImeConversionModeAccessor(),
            new KeyboardLayoutInspector(),
            new TsfMicrosoftPinyinProfileActivator(),
            SystemAutomationDelay.Instance,
            new TsfInputProfileInspector())
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Microsoft Pinyin automation requires Windows.");
        }
    }

    internal MicrosoftPinyinProvider(
        IFocusWindowResolver focusResolver,
        IImeConversionModeAccessor conversionMode,
        IKeyboardLayoutInspector keyboardLayout,
        IMicrosoftPinyinProfileActivator profileActivator,
        IAutomationDelay? delay = null,
        IInputProfileInspector? profileInspector = null)
    {
        _focusResolver = focusResolver ?? throw new ArgumentNullException(nameof(focusResolver));
        _conversionMode = conversionMode ?? throw new ArgumentNullException(nameof(conversionMode));
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
                    IsActive: MicrosoftPinyinProfileIdentity.IsMatch(profile))
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
        if (!MicrosoftPinyinProfileIdentity.IsMatch(profile))
        {
            return ValueTask.FromResult(UnknownState(layout));
        }

        if (!TryResolveReliableInputTarget(window.Hwnd, out var inputHwnd))
        {
            return ValueTask.FromResult(UnknownState(layout));
        }

        var result = _conversionMode.GetConversionMode(inputHwnd);
        return ValueTask.FromResult(
            result.Success
                ? StateFromConversionMode(result.ConversionMode, layout)
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

        var profileActivation = _profileActivator.ActivateForSession();
        if (!profileActivation.Success || !profileActivation.VerifiedActive)
        {
            var unknown = UnknownState(layout);
            return Result(
                false,
                unknown,
                unknown,
                "profile-activation-failed",
                started);
        }

        var requestedMode = action switch
        {
            InputAction.Chinese => ChineseConversionMode,
            InputAction.English => EnglishConversionMode,
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
        };

        if (profileActivation.WasAlreadyActive)
        {
            return ApplyToAlreadyActiveProfile(
                window,
                layout,
                requestedMode,
                started,
                cancellationToken);
        }

        return await ApplyAfterProfileSwitchAsync(
                window,
                layout,
                requestedMode,
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
        if (!MicrosoftPinyinProfileIdentity.IsMatch(profile))
        {
            var unknown = UnknownState(layout);
            return Result(false, unknown, unknown, "profile-not-active", started);
        }

        if (!TryResolveReliableInputTarget(window.Hwnd, out var inputHwnd))
        {
            var unknown = UnknownState(layout);
            return Result(false, unknown, unknown, "focus-unavailable", started);
        }

        var native = _conversionMode.GetConversionMode(inputHwnd);
        if (!native.Success)
        {
            var unknown = UnknownState(layout);
            return Result(false, unknown, unknown, ReadFailureCode(native), started);
        }

        var state = StateFromConversionMode(native.ConversionMode, layout);
        return Result(true, state, state, null, started);
    }

    private InputOperationResult ApplyToAlreadyActiveProfile(
        WindowContext window,
        nint layout,
        uint requestedMode,
        long started,
        CancellationToken cancellationToken)
    {
        if (!TryResolveReliableInputTarget(window.Hwnd, out var inputHwnd))
        {
            var unknown = UnknownState(layout);
            TraceTarget("focus-unavailable", window.Hwnd, 0, requestedMode, null);
            return Result(false, unknown, unknown, "focus-unavailable", started);
        }

        var beforeNative = _conversionMode.GetConversionMode(inputHwnd);
        if (!beforeNative.Success)
        {
            var unknown = UnknownState(layout);
            var errorCode = ReadFailureCode(beforeNative);
            TraceTarget(errorCode, window.Hwnd, inputHwnd, requestedMode, null);
            return Result(false, unknown, unknown, errorCode, started);
        }

        var before = StateFromConversionMode(beforeNative.ConversionMode, layout);
        TraceTarget("read-before", window.Hwnd, inputHwnd, requestedMode, beforeNative.ConversionMode);
        if (beforeNative.ConversionMode == requestedMode)
        {
            return Result(true, before, before, null, started);
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Focus can move between the initial read and mutation even while the
        // top-level application remains foreground. Re-resolve immediately before
        // writing so a stale text-control HWND is never intentionally reused.
        if (!TryResolveReliableInputTarget(window.Hwnd, out var mutationHwnd))
        {
            return Result(false, before, UnknownState(layout), "focus-unavailable", started);
        }

        if (mutationHwnd != inputHwnd)
        {
            var currentNative = _conversionMode.GetConversionMode(mutationHwnd);
            if (!currentNative.Success)
            {
                return Result(
                    false,
                    before,
                    UnknownState(layout),
                    ReadFailureCode(currentNative),
                    started);
            }

            var current = StateFromConversionMode(currentNative.ConversionMode, layout);
            if (currentNative.ConversionMode == requestedMode)
            {
                return Result(true, before, current, null, started);
            }
        }

        // Re-confirm the active TSF profile immediately before conversion-mode
        // mutation. If another IME (for example WeChat) is active, ActivateForSession
        // must first make Microsoft Pinyin the verified profile. Conversion values
        // 0x401/0x0 are never written after an unverified profile result.
        var profileGuard = _profileActivator.ActivateForSession();
        if (!profileGuard.Success || !profileGuard.VerifiedActive)
        {
            return Result(false, before, UnknownState(layout), "profile-activation-failed", started);
        }

        cancellationToken.ThrowIfCancellationRequested();

        // If the guard itself had to switch profiles, the target thread's IME
        // context may be rebinding. Let the coordinator retry instead of writing
        // Microsoft-Pinyin-specific conversion flags into a transitional context.
        if (!profileGuard.WasAlreadyActive)
        {
            return Result(false, before, UnknownState(layout), "profile-rebound", started);
        }

        var set = _conversionMode.SetConversionMode(mutationHwnd, requestedMode);
        TraceTarget("write", window.Hwnd, mutationHwnd, requestedMode, beforeNative.ConversionMode);
        if (!set.Success)
        {
            return Result(false, before, before, WriteFailureCode(set), started);
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Verify the state of the currently focused control, not merely the HWND
        // that happened to receive the write. A focus move during the mutation is
        // therefore surfaced as a retryable mismatch instead of false success.
        if (!TryResolveReliableInputTarget(window.Hwnd, out var verifyHwnd))
        {
            return Result(false, before, UnknownState(layout), "focus-unavailable", started);
        }

        var afterNative = _conversionMode.GetConversionMode(verifyHwnd);
        if (!afterNative.Success)
        {
            return Result(
                false,
                before,
                UnknownState(layout),
                ReadFailureCode(afterNative, "verify-read-failed"),
                started);
        }

        var after = StateFromConversionMode(afterNative.ConversionMode, layout);
        TraceTarget("read-after", window.Hwnd, verifyHwnd, requestedMode, afterNative.ConversionMode);
        if (afterNative.ConversionMode == requestedMode)
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

    /// <summary>
    /// A session-wide TSF profile change is observable before every target GUI
    /// thread has finished rebinding its IME context. If conversion mode is written
    /// during that propagation window, the write can land on the old IME window or
    /// be overwritten when Microsoft Pinyin initializes. Re-resolve the focus HWND
    /// on every sample and require the requested mode to remain stable before
    /// reporting success.
    /// </summary>
    private async ValueTask<InputOperationResult> ApplyAfterProfileSwitchAsync(
        WindowContext window,
        nint layout,
        uint requestedMode,
        long started,
        CancellationToken cancellationToken)
    {
        var before = UnknownState(layout);
        var after = UnknownState(layout);
        var haveBefore = false;
        var stableSamples = 0;
        var lastError = "profile-settle-timeout";

        for (var check = 0; check < MaxProfileSwitchChecks; check++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Never mutate immediately after a TSF profile switch. The session-wide
            // active-profile signal can lead the target GUI thread's IME context.
            // Give that thread a short settle interval, then resolve hwndFocus again.
            await _delay
                .DelayAsync(ProfileSwitchCheckInterval, cancellationToken)
                .ConfigureAwait(false);

            var profile = _profileInspector.GetActiveKeyboardProfile();
            if (!MicrosoftPinyinProfileIdentity.IsMatch(profile))
            {
                stableSamples = 0;
                lastError = "profile-not-active";

                var reactivation = _profileActivator.ActivateForSession();
                if (!reactivation.Success || !reactivation.VerifiedActive)
                {
                    lastError = "profile-activation-failed";
                }

                continue;
            }

            // The IME window can change while the newly activated profile is
            // propagating to the target thread, so never reuse a previous HWND.
            if (!TryResolveReliableInputTarget(window.Hwnd, out var inputHwnd))
            {
                stableSamples = 0;
                lastError = "focus-unavailable";
                continue;
            }

            var currentNative = _conversionMode.GetConversionMode(inputHwnd);
            if (!currentNative.Success)
            {
                stableSamples = 0;
                lastError = ReadFailureCode(currentNative);
                continue;
            }

            var current = StateFromConversionMode(currentNative.ConversionMode, layout);
            if (!haveBefore)
            {
                before = current;
                haveBefore = true;
            }

            after = current;

            if (currentNative.ConversionMode == requestedMode)
            {
                stableSamples++;
                if (stableSamples >= RequiredStableSamplesAfterProfileSwitch)
                {
                    TraceTarget(
                        "profile-settled",
                        window.Hwnd,
                        inputHwnd,
                        requestedMode,
                        currentNative.ConversionMode);
                    return Result(true, before, after, null, started);
                }

                continue;
            }

            stableSamples = 0;

            var profileGuard = _profileActivator.ActivateForSession();
            if (!profileGuard.Success || !profileGuard.VerifiedActive)
            {
                lastError = "profile-activation-failed";
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (!profileGuard.WasAlreadyActive)
            {
                // The profile changed again during settling. Do not send
                // Microsoft-Pinyin conversion flags to the old IME context.
                lastError = "profile-rebound";
                continue;
            }

            var set = _conversionMode.SetConversionMode(inputHwnd, requestedMode);
            TraceTarget(
                "profile-settle-write",
                window.Hwnd,
                inputHwnd,
                requestedMode,
                currentNative.ConversionMode);
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

    private static InputState StateFromConversionMode(uint conversionMode, nint layout) =>
        new(
            "Microsoft Pinyin",
            (conversionMode & LegacyImeConversionModeAccessor.ImeCmodeNative) != 0
                ? InputMode.Chinese
                : InputMode.English,
            layout);

    private static InputState UnknownState(nint layout) =>
        new("Microsoft Pinyin", InputMode.Unknown, layout);

    private static string ReadFailureCode(
        ImeConversionModeResult result,
        string fallback = "read-failed") =>
        result switch
        {
            { ImeWindow: 0, NativeError: 0 } => "input-context-unavailable",
            { NativeError: 5 } => "input-context-inaccessible",
            _ => fallback
        };

    private static string WriteFailureCode(ImeSetConversionModeResult result) =>
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
        uint requestedMode,
        uint? actualMode)
    {
        Trace.WriteLine(
            $"[FlowIME.MicrosoftPinyin] utc={DateTimeOffset.UtcNow:O} stage={stage} " +
            $"foreground=0x{topLevelHwnd:X} focus=0x{focusHwnd:X} " +
            $"requested=0x{requestedMode:X} actual=" +
            (actualMode is null ? "unknown" : $"0x{actualMode.Value:X}"));
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
