using FlowIME.App.ViewModels;
using FlowIME.Core.Models;
using FlowIME.Core.Rules;

namespace FlowIME.App.Tests.ViewModels;

public sealed class GlobalDefaultViewModelTests
{
    [Fact]
    public void New_default_starts_disabled_and_does_not_offer_keep_action()
    {
        var viewModel = new GlobalDefaultViewModel(
            current: null,
            providers: Providers(),
            defaultProviderId: InputMethodProviderIds.MicrosoftPinyin);

        Assert.False(viewModel.Enabled);
        Assert.Equal(InputMethodProviderIds.MicrosoftPinyin, viewModel.SelectedProviderId);
        Assert.Equal(InputAction.Chinese, viewModel.SelectedAction);
        Assert.DoesNotContain(viewModel.ActionOptions, option => option.Action == InputAction.Keep);
        Assert.Null(viewModel.CreateTarget());
    }

    [Fact]
    public void Existing_default_is_projected_and_created_with_selected_provider_and_mode()
    {
        var current = new GlobalDefaultTarget(
            InputMethodProviderIds.WeChat,
            InputAction.English);
        var viewModel = new GlobalDefaultViewModel(
            current,
            Providers(),
            InputMethodProviderIds.MicrosoftPinyin);

        Assert.True(viewModel.Enabled);
        Assert.Equal(InputMethodProviderIds.WeChat, viewModel.SelectedProviderId);
        Assert.Equal(InputAction.English, viewModel.SelectedAction);
        Assert.Equal(current, viewModel.CreateTarget());
    }

    [Fact]
    public void Selected_action_option_is_an_item_instance_and_updates_the_persisted_action()
    {
        var viewModel = new GlobalDefaultViewModel(
            new GlobalDefaultTarget(InputMethodProviderIds.WeChat, InputAction.Chinese),
            Providers(),
            InputMethodProviderIds.MicrosoftPinyin);

        Assert.Same(
            viewModel.ActionOptions.Single(option => option.Action == InputAction.Chinese),
            viewModel.SelectedActionOption);

        viewModel.SelectedActionOption =
            viewModel.ActionOptions.Single(option => option.Action == InputAction.English);

        Assert.Equal(InputAction.English, viewModel.SelectedAction);
        Assert.Equal(InputAction.English, viewModel.CreateTarget()!.Action);
    }

    [Fact]
    public void Selected_action_change_notifies_item_projection_and_rejects_keep()
    {
        var viewModel = new GlobalDefaultViewModel(
            current: null,
            providers: Providers(),
            defaultProviderId: InputMethodProviderIds.MicrosoftPinyin);
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        viewModel.SelectedAction = InputAction.English;

        Assert.Contains(nameof(viewModel.SelectedAction), changed);
        Assert.Contains(nameof(viewModel.SelectedActionOption), changed);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            viewModel.SelectedAction = InputAction.Keep);
    }

    private static IReadOnlyList<InputMethodProviderDescriptor> Providers() =>
    [
        new(
            InputMethodProviderIds.MicrosoftPinyin,
            "Microsoft Pinyin",
            new InputMethodProviderCapabilities(true, true, true, true, true, true)),
        new(
            InputMethodProviderIds.WeChat,
            "微信输入法",
            new InputMethodProviderCapabilities(true, true, true, true, true, true))
    ];
}
