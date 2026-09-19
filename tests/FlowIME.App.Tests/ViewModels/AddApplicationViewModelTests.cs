using FlowIME.App.ViewModels;
using FlowIME.Core.Models;
using FlowIME.Core.Rules;

namespace FlowIME.App.Tests.ViewModels;

public sealed class AddApplicationViewModelTests
{
    [Fact]
    public void Defaults_to_keep_and_microsoft_pinyin_until_an_application_is_selected()
    {
        var viewModel = new AddApplicationViewModel([
            App("Visual Studio Code", "Code", @"C:\Apps\Code.exe")
        ]);

        Assert.Equal(InputAction.Keep, viewModel.SelectedAction);
        Assert.Equal(InputMethodProviderIds.MicrosoftPinyin, viewModel.SelectedProviderId);
        Assert.Null(viewModel.SelectedApplication);
        Assert.False(viewModel.CanCreateRule);
    }

    [Fact]
    public void Provider_options_expose_registered_microsoft_and_wechat_providers()
    {
        var viewModel = new AddApplicationViewModel(
            [App("Visual Studio Code", "Code", @"C:\Apps\Code.exe")],
            [
                Provider(InputMethodProviderIds.MicrosoftPinyin, "Microsoft Pinyin"),
                Provider(InputMethodProviderIds.WeChat, "微信输入法")
            ]);

        Assert.Equal(2, viewModel.ProviderOptions.Count);
        Assert.Contains(
            viewModel.ProviderOptions,
            option => option.ProviderId == InputMethodProviderIds.WeChat && option.Label == "微信输入法");
    }

    [Fact]
    public void Creates_a_path_based_rule_with_selected_provider_for_the_selected_application()
    {
        var app = App("Visual Studio Code", "Code", @"C:\Apps\Code.exe");
        var viewModel = new AddApplicationViewModel(
            [app],
            [
                Provider(InputMethodProviderIds.MicrosoftPinyin, "Microsoft Pinyin"),
                Provider(InputMethodProviderIds.WeChat, "微信输入法")
            ])
        {
            SelectedApplication = app,
            SelectedAction = InputAction.English,
            SelectedProviderId = InputMethodProviderIds.WeChat
        };

        var rule = viewModel.CreateRule(priority: 300);

        Assert.Equal(InputAction.English, rule.Action);
        Assert.Equal(InputMethodProviderIds.WeChat, rule.ProviderId);
        Assert.Equal("Visual Studio Code", rule.DisplayName);
        Assert.Equal(300, rule.Priority);
        Assert.True(rule.Enabled);
        Assert.Equal(@"C:\Apps\Code.exe", rule.Match.ProcessPath);
        Assert.Null(rule.Match.ProcessName);
    }

    [Fact]
    public void Packaged_application_rule_keeps_display_path_and_adds_stable_package_identity()
    {
        var app = new RunningApplication(
            "Codex",
            "ChatGPT",
            @"C:\Program Files\WindowsApps\OpenAI.Codex_26.908.0_x64__abc\app\ChatGPT.exe",
            MainWindowHandle: (nint)0x1234,
            ProcessId: 42,
            PackageFamilyName: "OpenAI.Codex_abc",
            ApplicationUserModelId: "OpenAI.Codex_abc!Main");
        var viewModel = new AddApplicationViewModel([app])
        {
            SelectedApplication = app,
            SelectedAction = InputAction.Chinese
        };

        var rule = viewModel.CreateRule(priority: 100);

        Assert.Equal(app.ExecutablePath, rule.Match.ProcessPath);
        Assert.Equal("OpenAI.Codex_abc", rule.Match.PackageFamilyName);
        Assert.Equal("OpenAI.Codex_abc!Main", rule.Match.ApplicationUserModelId);
    }

    [Fact]
    public void Selected_action_option_is_an_item_instance_and_updates_created_rule()
    {
        var app = App("Visual Studio Code", "Code", @"C:\Apps\Code.exe");
        var viewModel = new AddApplicationViewModel([app])
        {
            SelectedApplication = app
        };

        Assert.Same(
            viewModel.ActionOptions.Single(option => option.Action == InputAction.Keep),
            viewModel.SelectedActionOption);

        viewModel.SelectedActionOption =
            viewModel.ActionOptions.Single(option => option.Action == InputAction.English);

        Assert.Equal(InputAction.English, viewModel.SelectedAction);
        Assert.Equal(InputAction.English, viewModel.CreateRule(priority: 100).Action);
    }

    [Theory]
    [InlineData("studio", "Visual Studio Code")]
    [InlineData("code", "Visual Studio Code")]
    [InlineData("chrome.exe", "Google Chrome")]
    [InlineData("C:\\Apps\\Chrome", "Google Chrome")]
    public void Search_filters_by_display_process_filename_and_path(
        string query,
        string expectedDisplayName)
    {
        var viewModel = new AddApplicationViewModel([
            App("Visual Studio Code", "Code", @"C:\Apps\Code.exe"),
            App("Google Chrome", "chrome", @"C:\Apps\Chrome\chrome.exe")
        ]);

        viewModel.SearchQuery = query;

        var item = Assert.Single(viewModel.ApplicationItems);
        Assert.Equal(expectedDisplayName, item.DisplayName);
    }

    [Fact]
    public void Manual_executable_is_selected_and_custom_name_is_persisted()
    {
        var viewModel = new AddApplicationViewModel(Array.Empty<RunningApplication>());
        var manual = new RunningApplication(
            "My Tool",
            "MyTool",
            @"C:\Tools\MyTool.exe",
            MainWindowHandle: 0,
            ProcessId: 0);

        viewModel.AddOrSelectApplication(manual);
        viewModel.CustomDisplayName = "  Writing Tool  ";

        Assert.True(viewModel.CanCreateRule);
        Assert.Equal("手动选择", Assert.Single(viewModel.ApplicationItems).SourceLabel);
        var rule = viewModel.CreateRule(priority: 100);
        Assert.Equal("Writing Tool", rule.DisplayName);
        Assert.Equal(@"C:\Tools\MyTool.exe", rule.Match.ProcessPath);
    }

    [Fact]
    public void Blank_custom_name_falls_back_to_resolved_display_name()
    {
        var app = App("Visual Studio Code", "Code", @"C:\Apps\Code.exe");
        var viewModel = new AddApplicationViewModel([app])
        {
            SelectedApplication = app,
            CustomDisplayName = "   "
        };

        Assert.Equal("Visual Studio Code", viewModel.CreateRule(priority: 100).DisplayName);
    }

    [Fact]
    public void Advanced_match_fields_are_trimmed_and_included_in_preview_and_rule()
    {
        var app = App("Google Chrome", "chrome", @"C:\Apps\Chrome.exe");
        var viewModel = new AddApplicationViewModel([app])
        {
            SelectedApplication = app,
            ProcessName = " chrome ",
            WindowClass = " Chrome_WidgetWin_1 ",
            WindowTitleContains = " Docs "
        };

        var rule = viewModel.CreateRule(priority: 200);

        Assert.Equal("chrome", rule.Match.ProcessName);
        Assert.Equal("Chrome_WidgetWin_1", rule.Match.WindowClass);
        Assert.Equal("Docs", rule.Match.WindowTitleContains);
        Assert.Contains("进程名 = chrome", viewModel.MatchPreview, StringComparison.Ordinal);
        Assert.Contains("标题包含 Docs", viewModel.MatchPreview, StringComparison.Ordinal);
    }

    [Fact]
    public void Candidate_conflict_preview_warns_when_higher_priority_broad_rule_shadows_it()
    {
        var app = App("Google Chrome", "chrome", @"C:\Apps\Chrome.exe");
        var broad = new ApplicationRule(
            Guid.NewGuid(), true, 500,
            new ApplicationMatch(ProcessPath: app.ExecutablePath),
            InputAction.Chinese, "Chrome");
        var viewModel = new AddApplicationViewModel([app], existingRules: [broad])
        {
            SelectedApplication = app,
            WindowTitleContains = "Docs",
            PreviewPriority = 100
        };

        Assert.True(viewModel.HasConflictPreview);
        Assert.Contains("优先级", viewModel.ConflictPreview, StringComparison.Ordinal);
        Assert.Contains("不会生效", viewModel.ConflictPreview, StringComparison.Ordinal);
    }

    [Fact]
    public void Loading_existing_rule_preserves_advanced_match_priority_and_target()
    {
        var app = App("Google Chrome", "chrome", @"C:\Apps\Chrome.exe");
        var existing = new ApplicationRule(
            Guid.NewGuid(),
            true,
            725,
            new ApplicationMatch(
                ProcessPath: app.ExecutablePath,
                ProcessName: "chrome",
                WindowTitleContains: "Docs",
                WindowClass: "Chrome_WidgetWin_1"),
            InputAction.English,
            "Docs rule",
            InputMethodProviderIds.WeChat);
        var viewModel = new AddApplicationViewModel(
            [app],
            [Provider(InputMethodProviderIds.MicrosoftPinyin, "Microsoft Pinyin"),
             Provider(InputMethodProviderIds.WeChat, "WeChat")],
            existingRules: [existing])
        {
            SelectedApplication = app
        };

        viewModel.LoadExistingRule(existing);

        Assert.Equal("Docs rule", viewModel.CustomDisplayName);
        Assert.Equal("chrome", viewModel.ProcessName);
        Assert.Equal("Chrome_WidgetWin_1", viewModel.WindowClass);
        Assert.Equal("Docs", viewModel.WindowTitleContains);
        Assert.Equal(725, viewModel.PreviewPriority);
        Assert.Equal(InputAction.English, viewModel.SelectedAction);
        Assert.Equal(InputMethodProviderIds.WeChat, viewModel.SelectedProviderId);
    }

    private static InputMethodProviderDescriptor Provider(string id, string name) =>
        new(
            id,
            name,
            new InputMethodProviderCapabilities(true, true, true, true, true, true));

    private static RunningApplication App(
        string displayName,
        string processName,
        string path) =>
        new(
            displayName,
            processName,
            path,
            MainWindowHandle: (nint)0x1234,
            ProcessId: 42);
}
