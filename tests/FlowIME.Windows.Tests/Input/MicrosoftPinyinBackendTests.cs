using FlowIME.Core.Automation;
using FlowIME.Core.Models;
using FlowIME.Windows.Input;

namespace FlowIME.Windows.Tests.Input;

public sealed class MicrosoftPinyinBackendTests
{
    [Fact]
    public async Task GetState_uses_focused_control_and_maps_0401_to_chinese()
    {
        var focus = ReliableFocus((nint)0x2000);
        var conversion = new FakeConversionModeAccessor();
        conversion.ReadResults.Enqueue(SuccessfulMode(0x00000401));
        var layout = new FakeKeyboardLayoutInspector((nint)0x08040804);
        var backend = new MicrosoftPinyinBackend(
            focus,
            conversion,
            layout,
            SuccessfulProfileActivator(),
            profileInspector: ActiveMicrosoftPinyinProfileInspector());

        var state = await backend.GetStateAsync(
            TestWindow(),
            TestContext.Current.CancellationToken);

        Assert.Equal(InputMode.Chinese, state.Mode);
        Assert.Equal("Microsoft Pinyin", state.ProfileName);
        Assert.Equal((nint)0x08040804, state.KeyboardLayout);
        Assert.Equal((nint)0x2000, conversion.LastGetTarget);
    }

    [Fact]
    public async Task GetState_maps_zero_conversion_mode_to_english()
    {
        var focus = ReliableFocus((nint)0x2000);
        var conversion = new FakeConversionModeAccessor();
        conversion.ReadResults.Enqueue(SuccessfulMode(0));
        var backend = new MicrosoftPinyinBackend(
            focus,
            conversion,
            new FakeKeyboardLayoutInspector((nint)0x08040804),
            SuccessfulProfileActivator(),
            profileInspector: ActiveMicrosoftPinyinProfileInspector());

        var state = await backend.GetStateAsync(
            TestWindow(),
            TestContext.Current.CancellationToken);

        Assert.Equal(InputMode.English, state.Mode);
    }


    [Fact]
    public async Task Apply_activates_microsoft_pinyin_profile_before_reading_mode()
    {
        var conversion = new FakeConversionModeAccessor();
        conversion.ReadResults.Enqueue(SuccessfulMode(0x00000401));
        var profile = SuccessfulProfileActivator();
        var backend = CreateBackend(conversion, profileActivator: profile);

        var result = await backend.ApplyAsync(
            TestWindow(),
            InputAction.Chinese,
            TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal(1, profile.ActivationCount);
        Assert.Equal(0, conversion.SetCount);
        Assert.Equal(InputMode.Chinese, result.After.Mode);
    }

    [Fact]
    public async Task Apply_stops_when_microsoft_pinyin_profile_activation_fails()
    {
        var conversion = new FakeConversionModeAccessor();
        var profile = new FakeProfileActivator(
            new MicrosoftPinyinProfileActivationResult(
                Success: false,
                HResult: unchecked((int)0x80004005),
                VerifiedActive: false,
                WasAlreadyActive: false,
                Error: "activation failed"));
        var backend = CreateBackend(conversion, profileActivator: profile);

        var result = await backend.ApplyAsync(
            TestWindow(),
            InputAction.English,
            TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal("profile-activation-failed", result.ErrorCode);
        Assert.Equal(1, profile.ActivationCount);
        Assert.Equal(0, conversion.GetCount);
        Assert.Equal(0, conversion.SetCount);
    }

    [Fact]
    public async Task Apply_chinese_is_noop_when_target_is_already_chinese()
    {
        var conversion = new FakeConversionModeAccessor();
        conversion.ReadResults.Enqueue(SuccessfulMode(0x00000401));
        var backend = CreateBackend(conversion);

        var result = await backend.ApplyAsync(
            TestWindow(),
            InputAction.Chinese,
            TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal(InputMode.Chinese, result.Before.Mode);
        Assert.Equal(InputMode.Chinese, result.After.Mode);
        Assert.Equal(0, conversion.SetCount);
    }

    [Fact]
    public async Task Apply_chinese_sets_0401_on_focused_control_and_verifies()
    {
        var conversion = new FakeConversionModeAccessor();
        conversion.ReadResults.Enqueue(SuccessfulMode(0));
        conversion.ReadResults.Enqueue(SuccessfulMode(0x00000401));
        var backend = CreateBackend(conversion, focusedHwnd: (nint)0x2468);

        var result = await backend.ApplyAsync(
            TestWindow(),
            InputAction.Chinese,
            TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal((nint)0x2468, conversion.LastSetTarget);
        Assert.Equal((uint)0x00000401, conversion.LastSetMode);
        Assert.Equal(InputMode.English, result.Before.Mode);
        Assert.Equal(InputMode.Chinese, result.After.Mode);
        Assert.Equal("Microsoft Pinyin / Focused IMM", result.Backend);
    }

    [Fact]
    public async Task Apply_english_sets_zero_on_focused_control_and_verifies()
    {
        var conversion = new FakeConversionModeAccessor();
        conversion.ReadResults.Enqueue(SuccessfulMode(0x00000401));
        conversion.ReadResults.Enqueue(SuccessfulMode(0));
        var backend = CreateBackend(conversion);

        var result = await backend.ApplyAsync(
            TestWindow(),
            InputAction.English,
            TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal((uint)0, conversion.LastSetMode);
        Assert.Equal(InputMode.Chinese, result.Before.Mode);
        Assert.Equal(InputMode.English, result.After.Mode);
    }

    [Fact]
    public async Task Apply_refuses_to_mutate_when_only_top_level_fallback_is_available()
    {
        var focus = new FakeFocusWindowResolver(new FocusWindowResult(
            TopLevelWindow: (nint)0x1000,
            ThreadId: 10,
            ActiveWindow: (nint)0x1000,
            FocusWindow: 0,
            CaretWindow: 0,
            EffectiveInputWindow: (nint)0x1000,
            Source: FocusWindowSource.TopLevelFallback,
            GuiThreadInfoAvailable: true,
            NativeError: 0,
            Error: "No focused control."));
        var conversion = new FakeConversionModeAccessor();
        var backend = new MicrosoftPinyinBackend(
            focus,
            conversion,
            new FakeKeyboardLayoutInspector((nint)0x08040804),
            SuccessfulProfileActivator(),
            profileInspector: ActiveMicrosoftPinyinProfileInspector());

        var result = await backend.ApplyAsync(
            TestWindow(),
            InputAction.Chinese,
            TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal("focus-unavailable", result.ErrorCode);
        Assert.Equal(0, conversion.GetCount);
        Assert.Equal(0, conversion.SetCount);
    }

    [Fact]
    public async Task Apply_reports_verification_failure_when_native_state_does_not_change()
    {
        var conversion = new FakeConversionModeAccessor();
        conversion.ReadResults.Enqueue(SuccessfulMode(0));
        conversion.ReadResults.Enqueue(SuccessfulMode(0));
        var backend = CreateBackend(conversion);

        var result = await backend.ApplyAsync(
            TestWindow(),
            InputAction.Chinese,
            TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal("verification-failed", result.ErrorCode);
        Assert.Equal(InputMode.English, result.After.Mode);
    }

    [Fact]
    public async Task Apply_english_after_profile_switch_reapplies_until_mode_is_stable()
    {
        var conversion = new FakeConversionModeAccessor();
        conversion.ReadResults.Enqueue(SuccessfulMode(0x00000401));
        conversion.ReadResults.Enqueue(SuccessfulMode(0x00000401));
        conversion.ReadResults.Enqueue(SuccessfulMode(0));
        conversion.ReadResults.Enqueue(SuccessfulMode(0));
        conversion.ReadResults.Enqueue(SuccessfulMode(0));
        conversion.ReadResults.Enqueue(SuccessfulMode(0));
        var delay = new FakeDelay();
        var backend = CreateBackend(
            conversion,
            profileActivator: SwitchedProfileActivator(),
            delay: delay);

        var result = await backend.ApplyAsync(
            TestWindow(),
            InputAction.English,
            TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal(InputMode.English, result.After.Mode);
        Assert.Equal(2, conversion.SetCount);
        Assert.Equal(6, delay.CallCount);
    }

    [Fact]
    public async Task Apply_after_profile_switch_re_resolves_focus_before_reapplying()
    {
        var focus = new SequencedFocusWindowResolver(
            ReliableFocusResult((nint)0x2000),
            ReliableFocusResult((nint)0x3000));
        var conversion = new FakeConversionModeAccessor();
        conversion.ReadResults.Enqueue(SuccessfulMode(0x00000401));
        conversion.ReadResults.Enqueue(SuccessfulMode(0x00000401));
        conversion.ReadResults.Enqueue(SuccessfulMode(0));
        conversion.ReadResults.Enqueue(SuccessfulMode(0));
        conversion.ReadResults.Enqueue(SuccessfulMode(0));
        conversion.ReadResults.Enqueue(SuccessfulMode(0));
        var backend = new MicrosoftPinyinBackend(
            focus,
            conversion,
            new FakeKeyboardLayoutInspector((nint)0x08040804),
            SwitchedProfileActivator(),
            new FakeDelay(),
            ActiveMicrosoftPinyinProfileInspector());

        var result = await backend.ApplyAsync(
            TestWindow(),
            InputAction.English,
            TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Contains((nint)0x2000, conversion.SetTargets);
        Assert.Contains((nint)0x3000, conversion.SetTargets);
        Assert.True(focus.ResolveCount >= 2);
    }

    [Fact]
    public async Task Apply_re_resolves_focus_immediately_before_write_and_verification()
    {
        var focus = new SequencedFocusWindowResolver(
            ReliableFocusResult((nint)0x2000),
            ReliableFocusResult((nint)0x3000),
            ReliableFocusResult((nint)0x3000));
        var conversion = new FakeConversionModeAccessor();
        conversion.ReadResults.Enqueue(SuccessfulMode(0));
        conversion.ReadResults.Enqueue(SuccessfulMode(0));
        conversion.ReadResults.Enqueue(SuccessfulMode(0x00000401));
        var backend = new MicrosoftPinyinBackend(
            focus,
            conversion,
            new FakeKeyboardLayoutInspector((nint)0x08040804),
            SuccessfulProfileActivator(),
            new FakeDelay(),
            ActiveMicrosoftPinyinProfileInspector());

        var result = await backend.ApplyAsync(
            TestWindow(),
            InputAction.Chinese,
            TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal((nint)0x3000, conversion.LastSetTarget);
        Assert.True(focus.ResolveCount >= 3);
    }

    [Fact]
    public async Task Apply_never_writes_conversion_mode_when_profile_guard_is_unverified()
    {
        var conversion = new FakeConversionModeAccessor();
        conversion.ReadResults.Enqueue(SuccessfulMode(0));
        var profile = new FakeProfileActivator(
            ActiveProfileResult(wasAlreadyActive: true),
            new MicrosoftPinyinProfileActivationResult(
                Success: false,
                HResult: unchecked((int)0x80004005),
                VerifiedActive: false,
                WasAlreadyActive: false,
                Error: "profile changed"));
        var backend = CreateBackend(conversion, profileActivator: profile);

        var result = await backend.ApplyAsync(
            TestWindow(),
            InputAction.Chinese,
            TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal("profile-activation-failed", result.ErrorCode);
        Assert.Equal(0, conversion.SetCount);
    }

    [Fact]
    public async Task Apply_keep_never_mutates_native_state()
    {
        var conversion = new FakeConversionModeAccessor();
        conversion.ReadResults.Enqueue(SuccessfulMode(0));
        var backend = CreateBackend(conversion);

        var result = await backend.ApplyAsync(
            TestWindow(),
            InputAction.Keep,
            TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal(0, conversion.SetCount);
        Assert.Equal(result.Before, result.After);
    }

    private static MicrosoftPinyinBackend CreateBackend(
        FakeConversionModeAccessor conversion,
        nint focusedHwnd = default,
        FakeProfileActivator? profileActivator = null,
        IAutomationDelay? delay = null)
    {
        if (focusedHwnd == 0)
        {
            focusedHwnd = (nint)0x2000;
        }

        return new MicrosoftPinyinBackend(
            ReliableFocus(focusedHwnd),
            conversion,
            new FakeKeyboardLayoutInspector((nint)0x08040804),
            profileActivator ?? SuccessfulProfileActivator(),
            delay,
            ActiveMicrosoftPinyinProfileInspector());
    }

    private static FakeProfileActivator SuccessfulProfileActivator() =>
        new(ActiveProfileResult(wasAlreadyActive: true));

    private static FakeProfileActivator SwitchedProfileActivator() =>
        new(
            ActiveProfileResult(wasAlreadyActive: false),
            ActiveProfileResult(wasAlreadyActive: true));

    private static MicrosoftPinyinProfileActivationResult ActiveProfileResult(
        bool wasAlreadyActive) =>
        new(
            Success: true,
            HResult: 0,
            VerifiedActive: true,
            WasAlreadyActive: wasAlreadyActive,
            Error: null);

    private static IInputProfileInspector ActiveMicrosoftPinyinProfileInspector() =>
        new FakeProfileInspector(new TsfProfileSnapshot(
            Success: true,
            HResult: 0,
            ProfileType: 0x0001,
            LanguageId: 0x0804,
            Clsid: new Guid("81D4E9C9-1D3B-41BC-9E6C-4B40BF79E35E"),
            ProfileGuid: new Guid("FA550B04-5AD7-411F-A5AC-CA038EC515D7"),
            CategoryId: Guid.Empty,
            SubstituteKeyboardLayout: 0,
            Capabilities: 0,
            KeyboardLayout: 0,
            Flags: 0,
            Error: null));

    private static FakeFocusWindowResolver ReliableFocus(nint focusedHwnd) =>
        new(new FocusWindowResult(
            TopLevelWindow: (nint)0x1000,
            ThreadId: 10,
            ActiveWindow: (nint)0x1000,
            FocusWindow: focusedHwnd,
            CaretWindow: focusedHwnd,
            EffectiveInputWindow: focusedHwnd,
            Source: FocusWindowSource.Focus,
            GuiThreadInfoAvailable: true,
            NativeError: 0,
            Error: null));

    private static ImeConversionModeResult SuccessfulMode(uint mode) =>
        new(
            Success: true,
            ConversionMode: mode,
            ImeWindow: (nint)0x3000,
            NativeError: 0,
            Error: null);

    private static WindowContext TestWindow() =>
        new(
            Hwnd: (nint)0x1000,
            ProcessId: 20,
            ThreadId: 10,
            ProcessName: "Notepad",
            ExecutablePath: @"C:\Windows\notepad.exe",
            WindowTitle: "Untitled - Notepad",
            WindowClass: "Notepad",
            PackageFamilyName: null);

    private sealed class FakeFocusWindowResolver : IFocusWindowResolver
    {
        private readonly FocusWindowResult _result;

        public FakeFocusWindowResolver(FocusWindowResult result)
        {
            _result = result;
        }

        public FocusWindowResult Resolve(nint topLevelWindow) => _result;
    }

    private static FocusWindowResult ReliableFocusResult(nint focusedHwnd) =>
        new(
            TopLevelWindow: (nint)0x1000,
            ThreadId: 10,
            ActiveWindow: (nint)0x1000,
            FocusWindow: focusedHwnd,
            CaretWindow: focusedHwnd,
            EffectiveInputWindow: focusedHwnd,
            Source: FocusWindowSource.Focus,
            GuiThreadInfoAvailable: true,
            NativeError: 0,
            Error: null);

    private sealed class SequencedFocusWindowResolver : IFocusWindowResolver
    {
        private readonly Queue<FocusWindowResult> _results;
        private FocusWindowResult _last;

        public SequencedFocusWindowResolver(params FocusWindowResult[] results)
        {
            _results = new Queue<FocusWindowResult>(results);
            _last = results[^1];
        }

        public int ResolveCount { get; private set; }

        public FocusWindowResult Resolve(nint topLevelWindow)
        {
            ResolveCount++;
            if (_results.Count > 0)
            {
                _last = _results.Dequeue();
            }

            return _last;
        }
    }

    private sealed class FakeDelay : IAutomationDelay
    {
        public int CallCount { get; private set; }

        public ValueTask DelayAsync(
            TimeSpan delay,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeConversionModeAccessor : IImeConversionModeAccessor
    {
        public Queue<ImeConversionModeResult> ReadResults { get; } = new();
        public bool SetSucceeds { get; init; } = true;
        public int GetCount { get; private set; }
        public int SetCount { get; private set; }
        public nint LastGetTarget { get; private set; }
        public nint LastSetTarget { get; private set; }
        public uint LastSetMode { get; private set; }
        public List<nint> SetTargets { get; } = new();

        public ImeConversionModeResult GetConversionMode(nint targetWindow)
        {
            GetCount++;
            LastGetTarget = targetWindow;
            return ReadResults.Dequeue();
        }

        public ImeSetConversionModeResult SetConversionMode(
            nint targetWindow,
            uint conversionMode)
        {
            SetCount++;
            LastSetTarget = targetWindow;
            LastSetMode = conversionMode;
            SetTargets.Add(targetWindow);
            return new ImeSetConversionModeResult(
                SetSucceeds,
                conversionMode,
                (nint)0x3000,
                0,
                SetSucceeds ? null : "set failed");
        }
    }

    private sealed class FakeProfileActivator : IMicrosoftPinyinProfileActivator
    {
        private readonly Queue<MicrosoftPinyinProfileActivationResult> _results;
        private MicrosoftPinyinProfileActivationResult _last;

        public FakeProfileActivator(params MicrosoftPinyinProfileActivationResult[] results)
        {
            if (results.Length == 0)
            {
                throw new ArgumentException("At least one activation result is required.", nameof(results));
            }

            _results = new Queue<MicrosoftPinyinProfileActivationResult>(results);
            _last = results[^1];
        }

        public int ActivationCount { get; private set; }

        public MicrosoftPinyinProfileActivationResult ActivateForSession()
        {
            ActivationCount++;
            if (_results.Count > 0)
            {
                _last = _results.Dequeue();
            }

            return _last;
        }
    }

    private sealed class FakeProfileInspector(TsfProfileSnapshot snapshot) : IInputProfileInspector
    {
        public TsfProfileSnapshot GetActiveKeyboardProfile() => snapshot;
    }

    private sealed class FakeKeyboardLayoutInspector : IKeyboardLayoutInspector
    {
        private readonly nint _layout;

        public FakeKeyboardLayoutInspector(nint layout)
        {
            _layout = layout;
        }

        public nint GetKeyboardLayout(uint threadId) => _layout;
    }
}
