using FlowIME.Core.Models;
using FlowIME.Core.Rules;

namespace FlowIME.App.ViewModels;

public sealed class GlobalDefaultViewModel : InputActionSelectionViewModel
{
    private bool _enabled;
    private string _selectedProviderId;

    public GlobalDefaultViewModel(
        GlobalDefaultTarget? current,
        IReadOnlyList<InputMethodProviderDescriptor>? providers = null,
        string? defaultProviderId = null)
        : base(GlobalDefaultActionOptions, ResolveInitialAction(current))
    {
        var inputMethodOptions = providers is { Count: > 0 }
            ? providers.Select(provider =>
                    new InputMethodProviderOption(provider.DisplayName, provider.Id))
                .ToArray()
            : [new InputMethodProviderOption("Microsoft Pinyin", InputMethodProviderIds.MicrosoftPinyin)];
        ProviderOptions =
        [
            .. inputMethodOptions,
            new InputMethodProviderOption(
                "标准美式键盘（US）",
                InputMethodProviderOption.StandardUsKeyboardTargetId)
        ];

        var requestedProvider = current?.ProviderId ?? defaultProviderId;
        var normalizedProvider = InputMethodProviderIds.Normalize(requestedProvider);
        _selectedProviderId = current?.Action == InputAction.StandardUsKeyboard
            ? InputMethodProviderOption.StandardUsKeyboardTargetId
            : inputMethodOptions.Any(option =>
                option.ProviderId.Equals(normalizedProvider, StringComparison.OrdinalIgnoreCase))
                ? normalizedProvider
                : inputMethodOptions[0].ProviderId;
        _enabled = current is not null;
    }

    public IReadOnlyList<InputMethodProviderOption> ProviderOptions { get; }


    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (SetProperty(ref _enabled, value))
            {
                OnPropertyChanged(nameof(CanSelectProvider));
                OnPropertyChanged(nameof(CanSelectInputState));
            }
        }
    }

    public bool CanSelectProvider => Enabled && IsProviderSelectionEnabled;

    public bool CanSelectInputState => Enabled && IsInputStateSelectionEnabled;

    public string SelectedProviderId
    {
        get => _selectedProviderId;
        set
        {
            var normalized = InputMethodProviderOption.IsStandardUsKeyboard(value)
                ? InputMethodProviderOption.StandardUsKeyboardTargetId
                : InputMethodProviderIds.Normalize(value);
            if (!SetProperty(ref _selectedProviderId, normalized))
            {
                return;
            }

            if (InputMethodProviderOption.IsStandardUsKeyboard(normalized))
            {
                SelectedAction = InputAction.StandardUsKeyboard;
            }
            else if (SelectedAction == InputAction.StandardUsKeyboard)
            {
                SelectedAction = InputAction.English;
            }
        }
    }


    public GlobalDefaultTarget? CreateTarget() =>
        Enabled
            ? new GlobalDefaultTarget(
                SelectedAction == InputAction.StandardUsKeyboard
                    ? null
                    : InputMethodProviderIds.Normalize(SelectedProviderId),
                SelectedAction)
            : null;

    protected override void OnSelectedActionChanged()
    {
        if (SelectedAction == InputAction.StandardUsKeyboard)
        {
            SetProperty(
                ref _selectedProviderId,
                InputMethodProviderOption.StandardUsKeyboardTargetId,
                nameof(SelectedProviderId));
        }
        else if (InputMethodProviderOption.IsStandardUsKeyboard(_selectedProviderId))
        {
            SetProperty(
                ref _selectedProviderId,
                ProviderOptions.First(option => !InputMethodProviderOption.IsStandardUsKeyboard(option.ProviderId)).ProviderId,
                nameof(SelectedProviderId));
        }

        OnPropertyChanged(nameof(CanSelectProvider));
        OnPropertyChanged(nameof(CanSelectInputState));
    }

    private static InputAction ResolveInitialAction(GlobalDefaultTarget? current) =>
        current is
        {
            Action: InputAction.Chinese or
                    InputAction.English or
                    InputAction.StandardUsKeyboard
        } valid
            ? valid.Action
            : InputAction.Chinese;
}
