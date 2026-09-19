using FlowIME.Core.Automation;
using FlowIME.Core.Models;
using FlowIME.Windows.Input;

namespace FlowIME.Windows.Tests.Input;

public sealed class WeChatInputMethodProviderTests
{
    [Fact]
    public void Descriptor_declares_the_capabilities_validated_during_p5b()
    {
        var provider = CreateProvider(isOpen: true, activeProfile: true);

        Assert.Equal(InputMethodProviderIds.WeChat, provider.Descriptor.Id);
        Assert.Equal("微信输入法", provider.Descriptor.DisplayName);
        Assert.True(provider.Descriptor.Capabilities.CanDetectActiveProfile);
        Assert.True(provider.Descriptor.Capabilities.CanActivateProfile);
        Assert.True(provider.Descriptor.Capabilities.CanReadMode);
        Assert.True(provider.Descriptor.Capabilities.CanSetChinese);
        Assert.True(provider.Descriptor.Capabilities.CanSetEnglish);
        Assert.True(provider.Descriptor.Capabilities.RequiresPostActivationSettling);
    }

    [Fact]
    public async Task Detect_matches_only_the_exact_validated_wechat_profile()
    {
        var active = CreateProvider(isOpen: true, activeProfile: true);
        var different = CreateProvider(isOpen: true, activeProfile: false);

        var activeResult = await active.DetectAsync(
            TestWindow(),
            TestContext.Current.CancellationToken);
        var differentResult = await different.DetectAsync(
            TestWindow(),
            TestContext.Current.CancellationToken);

        Assert.True(activeResult.Success);
        Assert.True(activeResult.IsActive);
        Assert.True(differentResult.Success);
        Assert.False(differentResult.IsActive);
    }

    [Theory]
    [InlineData(true, InputMode.Chinese)]
    [InlineData(false, InputMode.English)]
    public async Task State_maps_open_to_chinese_and_closed_to_english(
        bool isOpen,
        InputMode expected)
    {
        var provider = CreateProvider(isOpen, activeProfile: true);

        var state = await provider.GetStateAsync(
            TestWindow(),
            TestContext.Current.CancellationToken);

        Assert.Equal("微信输入法", state.ProfileName);
        Assert.Equal(expected, state.Mode);
    }


    [Fact]
    public async Task State_read_fails_closed_when_wechat_profile_is_not_active()
    {
        var openStatus = new FakeOpenStatusAccessor(initialOpen: true);
        var provider = CreateProvider(
            openStatus,
            new FakeProfileActivator(wasAlreadyActiveFirstCall: true),
            new FakeProfileInspector(OtherProfile()));

        var state = await provider.GetStateAsync(
            TestWindow(),
            TestContext.Current.CancellationToken);

        Assert.Equal(InputMode.Unknown, state.Mode);
        Assert.Equal(0, openStatus.GetCount);
    }

    [Fact]
    public async Task Missing_ime_window_is_classified_as_structurally_unavailable()
    {
        var provider = new WeChatInputMethodProvider(
            new FakeFocusWindowResolver(),
            new MissingImeWindowOpenStatusAccessor(),
            new FakeKeyboardLayoutInspector(),
            new FakeProfileActivator(wasAlreadyActiveFirstCall: true),
            new CountingDelay(),
            new FakeProfileInspector(WeChatProfile()));

        var result = await provider.ApplyAsync(
            TestWindow(),
            InputAction.Keep,
            TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal("input-context-unavailable", result.ErrorCode);
    }

    [Fact]
    public async Task Access_denied_ime_window_is_classified_as_structurally_inaccessible()
    {
        var provider = new WeChatInputMethodProvider(
            new FakeFocusWindowResolver(),
            new AccessDeniedImeWindowOpenStatusAccessor(),
            new FakeKeyboardLayoutInspector(),
            new FakeProfileActivator(wasAlreadyActiveFirstCall: true),
            new CountingDelay(),
            new FakeProfileInspector(WeChatProfile()));

        var result = await provider.ApplyAsync(
            TestWindow(),
            InputAction.Keep,
            TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal("input-context-inaccessible", result.ErrorCode);
    }

    [Fact]
    public async Task Input_context_disappearing_before_write_is_classified_as_unavailable()
    {
        var provider = new WeChatInputMethodProvider(
            new FakeFocusWindowResolver(),
            new InputContextLostOnWriteOpenStatusAccessor(),
            new FakeKeyboardLayoutInspector(),
            new FakeProfileActivator(wasAlreadyActiveFirstCall: true),
            new CountingDelay(),
            new FakeProfileInspector(WeChatProfile()));

        var result = await provider.ApplyAsync(
            TestWindow(),
            InputAction.Chinese,
            TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal("input-context-unavailable", result.ErrorCode);
    }

    [Fact]
    public async Task Already_active_profile_sets_open_status_and_verifies_chinese()
    {
        var openStatus = new FakeOpenStatusAccessor(initialOpen: false);
        var provider = CreateProvider(
            openStatus,
            new FakeProfileActivator(wasAlreadyActiveFirstCall: true),
            new FakeProfileInspector(WeChatProfile()));

        var result = await provider.ApplyAsync(
            TestWindow(),
            InputAction.Chinese,
            TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal(InputMode.English, result.Before.Mode);
        Assert.Equal(InputMode.Chinese, result.After.Mode);
        Assert.True(openStatus.IsOpen);
        Assert.Equal(1, openStatus.SetCount);
    }

    [Fact]
    public async Task Already_active_profile_sets_closed_status_and_verifies_english()
    {
        var openStatus = new FakeOpenStatusAccessor(initialOpen: true);
        var provider = CreateProvider(
            openStatus,
            new FakeProfileActivator(wasAlreadyActiveFirstCall: true),
            new FakeProfileInspector(WeChatProfile()));

        var result = await provider.ApplyAsync(
            TestWindow(),
            InputAction.English,
            TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal(InputMode.Chinese, result.Before.Mode);
        Assert.Equal(InputMode.English, result.After.Mode);
        Assert.False(openStatus.IsOpen);
        Assert.Equal(1, openStatus.SetCount);
    }

    [Fact]
    public async Task Profile_switch_observes_settle_window_before_first_open_status_write()
    {
        var openStatus = new FakeOpenStatusAccessor(initialOpen: false);
        var delay = new CountingDelay();
        var activator = new FakeProfileActivator(wasAlreadyActiveFirstCall: false);
        var provider = new WeChatInputMethodProvider(
            new FakeFocusWindowResolver(),
            openStatus,
            new FakeKeyboardLayoutInspector(),
            activator,
            delay,
            new FakeProfileInspector(WeChatProfile()));

        var result = await provider.ApplyAsync(
            TestWindow(),
            InputAction.Chinese,
            TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.True(openStatus.IsOpen);
        Assert.Equal(1, openStatus.SetCount);
        Assert.True(delay.CallCount >= 8);
        Assert.True(openStatus.GetCount >= 8);
    }

    [Fact]
    public async Task Already_active_session_waits_until_target_thread_leaves_standard_us_layout()
    {
        var events = new List<string>();
        var openStatus = new FakeOpenStatusAccessor(initialOpen: false, events);
        var delay = new CountingDelay();
        var keyboardLayout = new SequencedKeyboardLayoutInspector(
            events,
            (nint)0x04090409,
            (nint)0x04090409,
            unchecked((nint)0xE0200804u),
            unchecked((nint)0xE0200804u),
            unchecked((nint)0xE0200804u),
            unchecked((nint)0xE0200804u));
        var provider = new WeChatInputMethodProvider(
            new FakeFocusWindowResolver(),
            openStatus,
            keyboardLayout,
            new FakeProfileActivator(wasAlreadyActiveFirstCall: true),
            delay,
            new FakeProfileInspector(WeChatProfile()));

        var result = await provider.ApplyAsync(
            TestWindow(),
            InputAction.Chinese,
            TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal(InputMode.Chinese, result.After.Mode);
        Assert.False(GameplayKeyboardBaseline.IsStandardUsKeyboard(result.After.KeyboardLayout));
        Assert.True(delay.CallCount >= 8);

        var firstWrite = events.IndexOf("open-set");
        Assert.True(firstWrite >= 0);
        Assert.True(events.Take(firstWrite).Count(entry => entry == "open-get") >= 5);
        Assert.DoesNotContain("layout-us", events.SkipWhile(entry => entry != "layout-zh").TakeWhile(entry => entry != "open-set"));
    }

    [Fact]
    public async Task Already_active_session_fails_when_target_thread_remains_on_standard_us_layout()
    {
        var openStatus = new FakeOpenStatusAccessor(initialOpen: true);
        var provider = new WeChatInputMethodProvider(
            new FakeFocusWindowResolver(),
            openStatus,
            new FakeKeyboardLayoutInspector((nint)0x04090409),
            new FakeProfileActivator(wasAlreadyActiveFirstCall: true),
            new CountingDelay(),
            new FakeProfileInspector(WeChatProfile()));

        var result = await provider.ApplyAsync(
            TestWindow(),
            InputAction.Chinese,
            TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal("target-profile-not-bound", result.ErrorCode);
        Assert.Equal(0, openStatus.GetCount);
    }

    [Fact]
    public async Task Failed_profile_activation_never_writes_wechat_open_status()
    {
        var openStatus = new FakeOpenStatusAccessor(initialOpen: false);
        var activator = new FakeProfileActivator(
            wasAlreadyActiveFirstCall: false,
            activationSucceeds: false);
        var provider = CreateProvider(
            openStatus,
            activator,
            new FakeProfileInspector(WeChatProfile()));

        var result = await provider.ApplyAsync(
            TestWindow(),
            InputAction.Chinese,
            TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal("profile-activation-failed", result.ErrorCode);
        Assert.Equal(0, openStatus.SetCount);
    }

    private static WeChatInputMethodProvider CreateProvider(
        bool isOpen,
        bool activeProfile) =>
        CreateProvider(
            new FakeOpenStatusAccessor(isOpen),
            new FakeProfileActivator(wasAlreadyActiveFirstCall: true),
            new FakeProfileInspector(activeProfile ? WeChatProfile() : OtherProfile()));

    private static WeChatInputMethodProvider CreateProvider(
        FakeOpenStatusAccessor openStatus,
        FakeProfileActivator activator,
        FakeProfileInspector inspector) =>
        new(
            new FakeFocusWindowResolver(),
            openStatus,
            new FakeKeyboardLayoutInspector(),
            activator,
            new CountingDelay(),
            inspector);

    private static WindowContext TestWindow() =>
        new(
            (nint)0x1000,
            20,
            10,
            "Notepad",
            @"C:\Windows\notepad.exe",
            "Untitled - Notepad",
            "Notepad",
            null);

    private static TsfProfileSnapshot WeChatProfile() =>
        new(
            Success: true,
            HResult: 0,
            ProfileType: 0x0001,
            LanguageId: 0x0804,
            Clsid: WeChatInputMethodProfileIdentity.Clsid,
            ProfileGuid: WeChatInputMethodProfileIdentity.ProfileGuid,
            CategoryId: Guid.Empty,
            SubstituteKeyboardLayout: 0,
            Capabilities: 0,
            KeyboardLayout: 0,
            Flags: 0,
            Error: null);

    private static TsfProfileSnapshot OtherProfile() =>
        WeChatProfile() with
        {
            Clsid = Guid.NewGuid(),
            ProfileGuid = Guid.NewGuid()
        };

    private sealed class FakeProfileInspector(TsfProfileSnapshot snapshot) : IInputProfileInspector
    {
        public TsfProfileSnapshot GetActiveKeyboardProfile() => snapshot;
    }

    private sealed class FakeFocusWindowResolver : IFocusWindowResolver
    {
        public FocusWindowResult Resolve(nint topLevelWindow) =>
            new(
                topLevelWindow,
                10,
                topLevelWindow,
                (nint)0x2000,
                (nint)0x2000,
                (nint)0x2000,
                FocusWindowSource.Focus,
                true,
                0,
                null);
    }

    private sealed class FakeOpenStatusAccessor(
        bool initialOpen,
        List<string>? events = null) : IInputStateAccessor
    {
        public bool IsOpen { get; private set; } = initialOpen;
        public int GetCount { get; private set; }
        public int SetCount { get; private set; }

        public ImeOpenStatusResult GetOpenStatus(nint targetWindow)
        {
            events?.Add("open-get");
            GetCount++;
            return new ImeOpenStatusResult(true, IsOpen, (nint)0x3000, 0, null);
        }

        public ImeSetOpenStatusResult SetOpenStatus(nint targetWindow, bool open)
        {
            events?.Add("open-set");
            SetCount++;
            IsOpen = open;
            return new ImeSetOpenStatusResult(true, open, (nint)0x3000, 0, null);
        }
    }

    private sealed class MissingImeWindowOpenStatusAccessor : IInputStateAccessor
    {
        public ImeOpenStatusResult GetOpenStatus(nint targetWindow) =>
            new(false, false, 0, 0, "ImmGetDefaultIMEWnd returned NULL.");

        public ImeSetOpenStatusResult SetOpenStatus(nint targetWindow, bool open) =>
            throw new InvalidOperationException("A missing input context must not be written.");
    }

    private sealed class AccessDeniedImeWindowOpenStatusAccessor : IInputStateAccessor
    {
        public ImeOpenStatusResult GetOpenStatus(nint targetWindow) =>
            new(false, false, (nint)0x3000, 5, "WM_IME_CONTROL access denied.");

        public ImeSetOpenStatusResult SetOpenStatus(nint targetWindow, bool open) =>
            throw new InvalidOperationException("An inaccessible input context must not be written.");
    }

    private sealed class InputContextLostOnWriteOpenStatusAccessor : IInputStateAccessor
    {
        public ImeOpenStatusResult GetOpenStatus(nint targetWindow) =>
            new(true, false, (nint)0x3000, 0, null);

        public ImeSetOpenStatusResult SetOpenStatus(nint targetWindow, bool open) =>
            new(false, open, 0, 0, "ImmGetDefaultIMEWnd returned NULL.");
    }

    private sealed class FakeKeyboardLayoutInspector(
        nint keyboardLayout = default) : IKeyboardLayoutInspector
    {
        private readonly nint _keyboardLayout = keyboardLayout == default
            ? (nint)0x08040804
            : keyboardLayout;

        public nint GetKeyboardLayout(uint threadId) => _keyboardLayout;
    }

    private sealed class SequencedKeyboardLayoutInspector : IKeyboardLayoutInspector
    {
        private readonly List<string> _events;
        private readonly nint[] _keyboardLayouts;
        private int _index;

        public SequencedKeyboardLayoutInspector(
            List<string> events,
            params nint[] keyboardLayouts)
        {
            _events = events;
            _keyboardLayouts = keyboardLayouts;
        }

        public nint GetKeyboardLayout(uint threadId)
        {
            var index = Math.Min(_index, _keyboardLayouts.Length - 1);
            _index++;
            var keyboardLayout = _keyboardLayouts[index];
            _events.Add(
                GameplayKeyboardBaseline.IsStandardUsKeyboard(keyboardLayout)
                    ? "layout-us"
                    : "layout-zh");
            return keyboardLayout;
        }
    }

    private sealed class FakeProfileActivator(
        bool wasAlreadyActiveFirstCall,
        bool activationSucceeds = true) : IWeChatInputMethodProfileActivator
    {
        private int _calls;

        public WeChatInputMethodProfileActivationResult ActivateForSession()
        {
            _calls++;
            if (!activationSucceeds)
            {
                return new WeChatInputMethodProfileActivationResult(
                    false,
                    unchecked((int)0x80004005),
                    false,
                    false,
                    "activation failed");
            }

            return new WeChatInputMethodProfileActivationResult(
                true,
                0,
                true,
                _calls == 1 ? wasAlreadyActiveFirstCall : true,
                null);
        }
    }

    private sealed class CountingDelay : IAutomationDelay
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
}
