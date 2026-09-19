using FlowIME.Core.Abstractions;
using FlowIME.Core.InputMethods;
using FlowIME.Core.Models;

namespace FlowIME.Core.Tests.InputMethods;

public sealed class ProviderInputMethodBackendTests
{
    [Fact]
    public async Task Compatibility_apply_without_provider_id_delegates_to_default_provider()
    {
        var provider = new RecordingProvider("default", isActive: true);
        var registry = new InputMethodProviderRegistry([provider], provider.Descriptor.Id);
        var backend = new ProviderInputMethodBackend(registry);
        var window = TestWindow();

        var state = await backend.GetStateAsync(
            window,
            TestContext.Current.CancellationToken);
        var result = await backend.ApplyAsync(
            window,
            InputAction.Chinese,
            TestContext.Current.CancellationToken);

        Assert.Equal(provider.State, state);
        Assert.Same(provider.Result, result);
        Assert.Equal(1, provider.GetStateCount);
        Assert.Equal(1, provider.ApplyCount);
        Assert.Equal(InputAction.Chinese, provider.LastAction);
        Assert.Equal(provider.Descriptor, backend.DefaultProvider);
        Assert.Single(backend.Providers);
    }

    [Fact]
    public async Task State_read_uses_the_provider_that_is_actually_active()
    {
        var microsoft = new RecordingProvider(
            InputMethodProviderIds.MicrosoftPinyin,
            isActive: false,
            state: new InputState("Microsoft Pinyin", InputMode.English, 1));
        var weChat = new RecordingProvider(
            InputMethodProviderIds.WeChat,
            isActive: true,
            state: new InputState("微信输入法", InputMode.Chinese, 2));
        var backend = new ProviderInputMethodBackend(
            new InputMethodProviderRegistry(
                [microsoft, weChat],
                InputMethodProviderIds.MicrosoftPinyin));

        var state = await backend.GetStateAsync(
            TestWindow(),
            TestContext.Current.CancellationToken);

        Assert.Equal("微信输入法", state.ProfileName);
        Assert.Equal(InputMode.Chinese, state.Mode);
        Assert.Equal(0, microsoft.GetStateCount);
        Assert.Equal(1, weChat.GetStateCount);
    }

    [Fact]
    public async Task Provider_specific_apply_dispatches_to_requested_provider()
    {
        var microsoft = new RecordingProvider(InputMethodProviderIds.MicrosoftPinyin, true);
        var weChat = new RecordingProvider(InputMethodProviderIds.WeChat, false);
        var backend = new ProviderInputMethodBackend(
            new InputMethodProviderRegistry(
                [microsoft, weChat],
                InputMethodProviderIds.MicrosoftPinyin));

        var result = await backend.ApplyAsync(
            TestWindow(),
            InputMethodProviderIds.WeChat,
            InputAction.English,
            TestContext.Current.CancellationToken);

        Assert.Same(weChat.Result, result);
        Assert.Equal(0, microsoft.ApplyCount);
        Assert.Equal(1, weChat.ApplyCount);
        Assert.Equal(InputAction.English, weChat.LastAction);
    }

    [Fact]
    public async Task Blank_provider_id_preserves_legacy_microsoft_default()
    {
        var microsoft = new RecordingProvider(InputMethodProviderIds.MicrosoftPinyin, true);
        var weChat = new RecordingProvider(InputMethodProviderIds.WeChat, false);
        var backend = new ProviderInputMethodBackend(
            new InputMethodProviderRegistry(
                [microsoft, weChat],
                InputMethodProviderIds.MicrosoftPinyin));

        _ = await backend.ApplyAsync(
            TestWindow(),
            providerId: null,
            action: InputAction.Chinese,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, microsoft.ApplyCount);
        Assert.Equal(0, weChat.ApplyCount);
    }

    [Fact]
    public async Task Unknown_provider_fails_closed_without_mutating_any_registered_provider()
    {
        var microsoft = new RecordingProvider(InputMethodProviderIds.MicrosoftPinyin, true);
        var weChat = new RecordingProvider(InputMethodProviderIds.WeChat, false);
        var backend = new ProviderInputMethodBackend(
            new InputMethodProviderRegistry(
                [microsoft, weChat],
                InputMethodProviderIds.MicrosoftPinyin));

        var result = await backend.ApplyAsync(
            TestWindow(),
            "future-ime",
            InputAction.Chinese,
            TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal("provider-not-found", result.ErrorCode);
        Assert.Equal(0, microsoft.ApplyCount);
        Assert.Equal(0, weChat.ApplyCount);
    }


    [Fact]
    public async Task Multi_provider_detection_failure_fails_closed_instead_of_reading_microsoft_semantics()
    {
        var microsoft = new RecordingProvider(
            InputMethodProviderIds.MicrosoftPinyin,
            isActive: false,
            detectionSuccess: false);
        var weChat = new RecordingProvider(
            InputMethodProviderIds.WeChat,
            isActive: false,
            detectionSuccess: false);
        var backend = new ProviderInputMethodBackend(
            new InputMethodProviderRegistry(
                [microsoft, weChat],
                InputMethodProviderIds.MicrosoftPinyin));

        var state = await backend.GetStateAsync(
            TestWindow(),
            TestContext.Current.CancellationToken);

        Assert.Equal(InputMode.Unknown, state.Mode);
        Assert.Equal("Input method unavailable", state.ProfileName);
        Assert.Equal(0, microsoft.GetStateCount);
        Assert.Equal(0, weChat.GetStateCount);
    }

    [Fact]
    public async Task Successfully_detected_but_unregistered_active_profile_does_not_get_interpreted_as_microsoft_pinyin()
    {
        var microsoft = new RecordingProvider(InputMethodProviderIds.MicrosoftPinyin, false);
        var weChat = new RecordingProvider(InputMethodProviderIds.WeChat, false);
        var backend = new ProviderInputMethodBackend(
            new InputMethodProviderRegistry(
                [microsoft, weChat],
                InputMethodProviderIds.MicrosoftPinyin));

        var state = await backend.GetStateAsync(
            TestWindow(),
            TestContext.Current.CancellationToken);

        Assert.Equal(InputMode.Unknown, state.Mode);
        Assert.Equal("Other input method", state.ProfileName);
        Assert.Equal(0, microsoft.GetStateCount);
        Assert.Equal(0, weChat.GetStateCount);
    }

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

    private sealed class RecordingProvider : IInputMethodProvider
    {
        private readonly bool _isActive;
        private readonly bool _detectionSuccess;

        public RecordingProvider(
            string id,
            bool isActive,
            InputState? state = null,
            bool detectionSuccess = true)
        {
            _isActive = isActive;
            _detectionSuccess = detectionSuccess;
            Descriptor = new InputMethodProviderDescriptor(
                id,
                id,
                new InputMethodProviderCapabilities(
                    CanDetectActiveProfile: true,
                    CanActivateProfile: true,
                    CanReadMode: true,
                    CanSetChinese: true,
                    CanSetEnglish: true,
                    RequiresPostActivationSettling: false));
            State = state ?? new InputState(id, InputMode.English, (nint)0x4090409);
            Result = new InputOperationResult(
                true,
                State,
                State with { Mode = InputMode.Chinese },
                id,
                null,
                TimeSpan.FromMilliseconds(1));
        }

        public InputMethodProviderDescriptor Descriptor { get; }
        public InputState State { get; }
        public InputOperationResult Result { get; }
        public int GetStateCount { get; private set; }
        public int ApplyCount { get; private set; }
        public InputAction? LastAction { get; private set; }

        public ValueTask<InputMethodProviderDetectionResult> DetectAsync(
            WindowContext window,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(
                new InputMethodProviderDetectionResult(
                    _detectionSuccess,
                    _detectionSuccess && _isActive,
                    _detectionSuccess ? null : "detection-failed"));
        }

        public ValueTask<InputState> GetStateAsync(
            WindowContext window,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GetStateCount++;
            return ValueTask.FromResult(State);
        }

        public ValueTask<InputOperationResult> ApplyAsync(
            WindowContext window,
            InputAction action,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ApplyCount++;
            LastAction = action;
            return ValueTask.FromResult(Result);
        }
    }
}
