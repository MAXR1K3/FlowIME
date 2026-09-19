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
        ProviderOptions = providers is { Count: > 0 }
            ? providers.Select(provider =>
                    new InputMethodProviderOption(provider.DisplayName, provider.Id))
                .ToArray()
            : [new InputMethodProviderOption("Microsoft Pinyin", InputMethodProviderIds.MicrosoftPinyin)];

        var requestedProvider = current?.ProviderId ?? defaultProviderId;
        var normalizedProvider = InputMethodProviderIds.Normalize(requestedProvider);
        _selectedProviderId = ProviderOptions.Any(option =>
                option.ProviderId.Equals(normalizedProvider, StringComparison.OrdinalIgnoreCase))
            ? normalizedProvider
            : ProviderOptions[0].ProviderId;
        _enabled = current is not null;
    }

    public IReadOnlyList<InputMethodProviderOption> ProviderOptions { get; }


    public bool Enabled
    {
        get => _enabled;
        set => SetProperty(ref _enabled, value);
    }

    public string SelectedProviderId
    {
        get => _selectedProviderId;
        set => SetProperty(ref _selectedProviderId, InputMethodProviderIds.Normalize(value));
    }


    public GlobalDefaultTarget? CreateTarget() =>
        Enabled
            ? new GlobalDefaultTarget(
                InputMethodProviderIds.Normalize(SelectedProviderId),
                SelectedAction)
            : null;

    private static InputAction ResolveInitialAction(GlobalDefaultTarget? current) =>
        current is { Action: InputAction.Chinese or InputAction.English } valid
            ? valid.Action
            : InputAction.Chinese;
}
