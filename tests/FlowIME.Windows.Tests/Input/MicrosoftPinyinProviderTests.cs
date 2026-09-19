using FlowIME.Core.Automation;
using FlowIME.Core.Models;
using FlowIME.Windows.Input;

namespace FlowIME.Windows.Tests.Input;

public sealed class MicrosoftPinyinProviderTests
{
    [Fact]
    public void Descriptor_declares_only_microsoft_pinyin_owned_capabilities()
    {
        var provider = CreateProvider(ProfileSnapshot(success: true, isMicrosoftPinyin: true));

        Assert.Equal("microsoft-pinyin", provider.Descriptor.Id);
        Assert.Equal("Microsoft Pinyin", provider.Descriptor.DisplayName);
        Assert.True(provider.Descriptor.Capabilities.CanDetectActiveProfile);
        Assert.True(provider.Descriptor.Capabilities.CanActivateProfile);
        Assert.True(provider.Descriptor.Capabilities.CanReadMode);
        Assert.True(provider.Descriptor.Capabilities.CanSetChinese);
        Assert.True(provider.Descriptor.Capabilities.CanSetEnglish);
        Assert.True(provider.Descriptor.Capabilities.RequiresPostActivationSettling);
    }

    [Fact]
    public async Task Detect_reports_active_only_for_exact_microsoft_pinyin_profile_identity()
    {
        var active = CreateProvider(ProfileSnapshot(success: true, isMicrosoftPinyin: true));
        var different = CreateProvider(ProfileSnapshot(success: true, isMicrosoftPinyin: false));

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

    [Fact]
    public async Task Detect_does_not_guess_when_tsf_profile_inspection_fails()
    {
        var provider = CreateProvider(ProfileSnapshot(success: false, isMicrosoftPinyin: false));

        var result = await provider.DetectAsync(
            TestWindow(),
            TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.False(result.IsActive);
        Assert.Equal("profile-detection-failed", result.ErrorCode);
    }

    [Fact]
    public async Task Missing_ime_window_is_classified_as_structurally_unavailable()
    {
        var provider = CreateProvider(
            ProfileSnapshot(success: true, isMicrosoftPinyin: true),
            new MissingImeWindowConversionModeAccessor());

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
        var provider = CreateProvider(
            ProfileSnapshot(success: true, isMicrosoftPinyin: true),
            new AccessDeniedImeWindowConversionModeAccessor());

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
        var provider = CreateProvider(
            ProfileSnapshot(success: true, isMicrosoftPinyin: true),
            new InputContextLostOnWriteConversionModeAccessor());

        var result = await provider.ApplyAsync(
            TestWindow(),
            InputAction.English,
            TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal("input-context-unavailable", result.ErrorCode);
    }

    [Fact]
    public async Task State_is_unknown_when_microsoft_pinyin_profile_is_not_active()
    {
        var provider = CreateProvider(
            ProfileSnapshot(success: true, isMicrosoftPinyin: false),
            new ThrowingConversionModeAccessor());

        var state = await provider.GetStateAsync(
            TestWindow(),
            TestContext.Current.CancellationToken);

        Assert.Equal(InputMode.Unknown, state.Mode);
    }

    [Fact]
    public async Task Keep_does_not_read_conversion_mode_for_a_different_active_profile()
    {
        var provider = CreateProvider(
            ProfileSnapshot(success: true, isMicrosoftPinyin: false),
            new ThrowingConversionModeAccessor());

        var result = await provider.ApplyAsync(
            TestWindow(),
            InputAction.Keep,
            TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal("profile-not-active", result.ErrorCode);
    }

    [Fact]
    public async Task Profile_switch_settle_never_reads_another_profiles_conversion_mode()
    {
        var provider = new MicrosoftPinyinProvider(
            new FakeFocusWindowResolver(),
            new ThrowingConversionModeAccessor(),
            new FakeKeyboardLayoutInspector(),
            new FakeProfileActivator(wasAlreadyActive: false),
            new FakeDelay(),
            new FakeProfileInspector(ProfileSnapshot(success: true, isMicrosoftPinyin: false)));

        var result = await provider.ApplyAsync(
            TestWindow(),
            InputAction.English,
            TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal("profile-not-active", result.ErrorCode);
    }

    private static MicrosoftPinyinProvider CreateProvider(
        TsfProfileSnapshot snapshot,
        IImeConversionModeAccessor? conversionMode = null) =>
        new(
            new FakeFocusWindowResolver(),
            conversionMode ?? new FakeConversionModeAccessor(),
            new FakeKeyboardLayoutInspector(),
            new FakeProfileActivator(),
            new FakeDelay(),
            new FakeProfileInspector(snapshot));

    private static TsfProfileSnapshot ProfileSnapshot(bool success, bool isMicrosoftPinyin) =>
        new(
            Success: success,
            HResult: success ? 0 : unchecked((int)0x80004005),
            ProfileType: 0x0001,
            LanguageId: 0x0804,
            Clsid: isMicrosoftPinyin
                ? new Guid("81D4E9C9-1D3B-41BC-9E6C-4B40BF79E35E")
                : Guid.NewGuid(),
            ProfileGuid: isMicrosoftPinyin
                ? new Guid("FA550B04-5AD7-411F-A5AC-CA038EC515D7")
                : Guid.NewGuid(),
            CategoryId: Guid.Empty,
            SubstituteKeyboardLayout: 0,
            Capabilities: 0,
            KeyboardLayout: 0,
            Flags: 0,
            Error: success ? null : "inspection failed");

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

    private sealed class FakeConversionModeAccessor : IImeConversionModeAccessor
    {
        public ImeConversionModeResult GetConversionMode(nint targetWindow) =>
            new(true, 0, (nint)0x3000, 0, null);

        public ImeSetConversionModeResult SetConversionMode(nint targetWindow, uint conversionMode) =>
            new(true, conversionMode, (nint)0x3000, 0, null);
    }

    private sealed class MissingImeWindowConversionModeAccessor : IImeConversionModeAccessor
    {
        public ImeConversionModeResult GetConversionMode(nint targetWindow) =>
            new(false, 0, 0, 0, "ImmGetDefaultIMEWnd returned NULL.");

        public ImeSetConversionModeResult SetConversionMode(nint targetWindow, uint conversionMode) =>
            throw new InvalidOperationException("A missing input context must not be written.");
    }

    private sealed class AccessDeniedImeWindowConversionModeAccessor : IImeConversionModeAccessor
    {
        public ImeConversionModeResult GetConversionMode(nint targetWindow) =>
            new(false, 0, (nint)0x3000, 5, "WM_IME_CONTROL access denied.");

        public ImeSetConversionModeResult SetConversionMode(nint targetWindow, uint conversionMode) =>
            throw new InvalidOperationException("An inaccessible input context must not be written.");
    }

    private sealed class InputContextLostOnWriteConversionModeAccessor : IImeConversionModeAccessor
    {
        public ImeConversionModeResult GetConversionMode(nint targetWindow) =>
            new(true, MicrosoftPinyinProvider.ChineseConversionMode, (nint)0x3000, 0, null);

        public ImeSetConversionModeResult SetConversionMode(nint targetWindow, uint conversionMode) =>
            new(false, conversionMode, 0, 0, "ImmGetDefaultIMEWnd returned NULL.");
    }

    private sealed class ThrowingConversionModeAccessor : IImeConversionModeAccessor
    {
        public ImeConversionModeResult GetConversionMode(nint targetWindow) =>
            throw new InvalidOperationException("A different active profile must not be read as Microsoft Pinyin.");

        public ImeSetConversionModeResult SetConversionMode(nint targetWindow, uint conversionMode) =>
            throw new InvalidOperationException("A different active profile must not be mutated as Microsoft Pinyin.");
    }

    private sealed class FakeKeyboardLayoutInspector : IKeyboardLayoutInspector
    {
        public nint GetKeyboardLayout(uint threadId) => (nint)0x08040804;
    }

    private sealed class FakeProfileActivator(bool wasAlreadyActive = true)
        : IMicrosoftPinyinProfileActivator
    {
        public MicrosoftPinyinProfileActivationResult ActivateForSession() =>
            new(true, 0, true, wasAlreadyActive, null);
    }

    private sealed class FakeDelay : IAutomationDelay
    {
        public ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }
}
