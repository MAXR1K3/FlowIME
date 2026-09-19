using FlowIME.App.ViewModels;
using FlowIME.Core.Models;
using FlowIME.Core.Rules;

namespace FlowIME.App.Tests.ViewModels;

public sealed class EditRuleViewModelTests
{
    [Fact]
    public void Selected_action_option_projects_existing_rule_and_updates_selected_action()
    {
        var rule = new RuleListItemViewModel(
            Guid.NewGuid(),
            "Visual Studio Code",
            @"C:\Apps\Code.exe",
            InputMethodProviderIds.WeChat,
            "微信输入法",
            InputAction.Chinese,
            enabled: true);
        var viewModel = new EditRuleViewModel(rule);

        Assert.Same(
            viewModel.ActionOptions.Single(option => option.Action == InputAction.Chinese),
            viewModel.SelectedActionOption);

        viewModel.SelectedActionOption =
            viewModel.ActionOptions.Single(option => option.Action == InputAction.English);

        Assert.Equal(InputAction.English, viewModel.SelectedAction);
    }

    [Fact]
    public void Existing_keep_rule_projects_to_the_canonical_keep_option()
    {
        var rule = new RuleListItemViewModel(
            Guid.NewGuid(),
            "Visual Studio Code",
            @"C:\Apps\Code.exe",
            InputMethodProviderIds.WeChat,
            "微信输入法",
            InputAction.Keep,
            enabled: true);

        var viewModel = new EditRuleViewModel(rule);

        Assert.Equal(InputAction.Keep, viewModel.SelectedAction);
        Assert.Same(
            viewModel.ActionOptions.Single(option => option.Action == InputAction.Keep),
            viewModel.SelectedActionOption);
    }

    [Fact]
    public void Advanced_match_and_priority_are_editable_and_rebuilt()
    {
        var match = new ApplicationMatch(
            ProcessPath: @"C:\Apps\Code.exe",
            ProcessName: "Code",
            WindowTitleContains: "FlowIME",
            WindowClass: "Chrome_WidgetWin_1");
        var rule = new RuleListItemViewModel(
            Guid.NewGuid(), "Code", @"C:\Apps\Code.exe",
            InputMethodProviderIds.WeChat, "微信输入法",
            InputAction.Chinese, enabled: true,
            priority: 300, match: match);
        var viewModel = new EditRuleViewModel(rule);

        viewModel.WindowTitleContains = " Docs ";
        viewModel.Priority = 450;

        var rebuilt = viewModel.CreateUpdatedRule();
        Assert.Equal("Docs", rebuilt.Match.WindowTitleContains);
        Assert.Equal("Code", rebuilt.Match.ProcessName);
        Assert.Equal(450, rebuilt.Priority);
        Assert.Contains("窗口类", viewModel.MatchPreview, StringComparison.Ordinal);
    }
}