using FlowIME.App.ViewModels;
using FlowIME.Core.Models;
using FlowIME.Core.Rules;

namespace FlowIME.App.Tests.ViewModels;

public sealed class HomeViewModelTests
{
    [Fact]
    public void Defaults_describe_an_enabled_waiting_shell()
    {
        var viewModel = new HomeViewModel();

        Assert.True(viewModel.IsAutomationEnabled);
        Assert.Equal("正在运行", viewModel.AutomationStateLabel);
        Assert.Equal("等待前台应用", viewModel.CurrentApplicationName);
        Assert.Equal("—", viewModel.CurrentProcessName);
        Assert.Equal("等待检测", viewModel.CurrentInputProfile);
        Assert.Equal("未知", viewModel.CurrentInputModeLabel);
        Assert.Equal("等待解析", viewModel.RuleSourceLabel);
        Assert.Equal("—", viewModel.RuleTargetLabel);
        Assert.Equal("尚未检测到前台规则", viewModel.LastRuleSummary);
        Assert.False(viewModel.CanCreateRuleForCurrentApplication);
    }


    [Fact]
    public void Unknown_mode_keeps_known_provider_as_the_primary_user_facing_status()
    {
        var viewModel = new HomeViewModel();
        var window = new WindowContext(
            (nint)0x1234,
            100,
            200,
            "Explorer",
            @"C:\Windows\explorer.exe",
            "Downloads",
            "CabinetWClass",
            null);
        var input = new InputState("微信输入法", InputMode.Unknown, 0);

        viewModel.UpdateCurrentState(window, input, InputAction.Keep);

        Assert.Equal("未知", viewModel.CurrentInputModeLabel);
        Assert.Equal("微信输入法", viewModel.CurrentInputHeadline);
        Assert.Equal("模式暂不可读", viewModel.CurrentInputDetail);
    }

    [Fact]
    public void Automation_label_tracks_enabled_state()
    {
        var viewModel = new HomeViewModel();

        viewModel.IsAutomationEnabled = false;

        Assert.Equal("已暂停", viewModel.AutomationStateLabel);
    }

    [Fact]
    public void UpdateCurrentState_projects_window_input_and_matching_rule_for_the_ui()
    {
        var viewModel = new HomeViewModel();
        var window = new WindowContext(
            Hwnd: (nint)0x1234,
            ProcessId: 100,
            ThreadId: 200,
            ProcessName: "Code",
            ExecutablePath: @"C:\Apps\Code.exe",
            WindowTitle: "Program.cs - FlowIME",
            WindowClass: "Chrome_WidgetWin_1",
            PackageFamilyName: null);
        var input = new InputState(
            ProfileName: "Microsoft Pinyin",
            Mode: InputMode.English,
            KeyboardLayout: (nint)0x08040804);
        var rule = new ApplicationRule(
            Guid.NewGuid(),
            true,
            100,
            new ApplicationMatch(ProcessPath: window.ExecutablePath),
            InputAction.English,
            "Visual Studio Code");

        viewModel.UpdateCurrentState(window, input, InputAction.English, rule);

        Assert.Equal("Visual Studio Code", viewModel.CurrentApplicationName);
        Assert.Equal("Code.exe", viewModel.CurrentProcessName);
        Assert.Equal("Microsoft Pinyin", viewModel.CurrentInputProfile);
        Assert.Equal("英文", viewModel.CurrentInputModeLabel);
        Assert.Equal("应用专属规则", viewModel.RuleSourceLabel);
        Assert.Equal("Microsoft Pinyin · 英文", viewModel.RuleTargetLabel);
        Assert.Equal(
            "Visual Studio Code → Microsoft Pinyin · 英文",
            viewModel.LastRuleSummary);
        Assert.True(viewModel.CanCreateRuleForCurrentApplication);
        Assert.Contains("可执行文件", viewModel.RuleMatchReason, StringComparison.Ordinal);
    }

    [Fact]
    public void Match_reason_lists_the_advanced_conditions_that_were_satisfied()
    {
        var viewModel = new HomeViewModel();
        var window = new WindowContext(
            (nint)0x1234, 100, 200, "chrome", @"C:\Apps\Chrome.exe",
            "Docs - Chrome", "Chrome_WidgetWin_1", null);
        var rule = new ApplicationRule(
            Guid.NewGuid(), true, 100,
            new ApplicationMatch(
                ProcessPath: window.ExecutablePath,
                ProcessName: "chrome",
                WindowTitleContains: "Docs",
                WindowClass: "Chrome_WidgetWin_1"),
            InputAction.English, "Docs");

        viewModel.UpdateCurrentState(
            window,
            new InputState("Microsoft Pinyin", InputMode.English, 0),
            InputAction.English,
            rule);

        Assert.Contains("进程名 chrome", viewModel.RuleMatchReason, StringComparison.Ordinal);
        Assert.Contains("窗口类 Chrome_WidgetWin_1", viewModel.RuleMatchReason, StringComparison.Ordinal);
        Assert.Contains("标题包含 Docs", viewModel.RuleMatchReason, StringComparison.Ordinal);
    }


    [Fact]
    public void Matching_rule_summary_uses_target_provider_not_the_pre_apply_current_profile()
    {
        var viewModel = new HomeViewModel();
        var window = new WindowContext(
            (nint)0x1234,
            100,
            200,
            "Code",
            @"C:\Apps\Code.exe",
            "Code",
            "Chrome_WidgetWin_1",
            null);
        var input = new InputState("Microsoft Pinyin", InputMode.English, 0);
        var rule = new ApplicationRule(
            Guid.NewGuid(),
            true,
            100,
            new ApplicationMatch(ProcessPath: window.ExecutablePath),
            InputAction.Chinese,
            "Code",
            InputMethodProviderIds.WeChat);

        viewModel.UpdateCurrentState(
            window,
            input,
            InputAction.Chinese,
            rule,
            "微信输入法");

        Assert.Equal("应用专属规则", viewModel.RuleSourceLabel);
        Assert.Equal("微信输入法 · 中文", viewModel.RuleTargetLabel);
        Assert.Equal("Code → 微信输入法 · 中文", viewModel.LastRuleSummary);
        Assert.Equal("Microsoft Pinyin", viewModel.CurrentInputProfile);
    }

    [Fact]
    public void Global_default_summary_is_distinguished_from_application_rule()
    {
        var viewModel = new HomeViewModel();
        var window = new WindowContext(
            (nint)0x1234,
            100,
            200,
            "Explorer",
            @"C:\Windows\explorer.exe",
            "Downloads",
            "CabinetWClass",
            null);
        var input = new InputState("Microsoft Pinyin", InputMode.English, 0);

        viewModel.UpdateCurrentState(
            window,
            input,
            InputAction.Chinese,
            matchedRule: null,
            matchedProviderDisplayName: "微信输入法",
            resolutionSource: RuleResolutionSource.GlobalDefault);

        Assert.Equal("全局默认", viewModel.RuleSourceLabel);
        Assert.Equal("微信输入法 · 中文", viewModel.RuleTargetLabel);
        Assert.Equal("全局默认 → 微信输入法 · 中文", viewModel.LastRuleSummary);
        Assert.Equal("Explorer", viewModel.CurrentApplicationName);
    }


    [Fact]
    public void Context_policy_summary_is_distinguished_from_application_rule()
    {
        var viewModel = new HomeViewModel();
        var window = new WindowContext(
            (nint)0x1234,
            100,
            200,
            "msedge",
            @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
            "New tab",
            "Chrome_WidgetWin_1",
            null);
        var input = new InputState("Microsoft Pinyin", InputMode.Chinese, 0);

        viewModel.UpdateCurrentState(
            window,
            input,
            InputAction.English,
            matchedRule: null,
            matchedProviderDisplayName: "Microsoft Pinyin",
            resolutionSource: RuleResolutionSource.ContextPolicy);

        Assert.Equal("场景规则", viewModel.RuleSourceLabel);
        Assert.Equal("Microsoft Pinyin · 英文", viewModel.RuleTargetLabel);
        Assert.Equal("当前场景 → Microsoft Pinyin · 英文", viewModel.LastRuleSummary);
    }

    [Fact]
    public void UpdateCurrentState_explicitly_reports_no_matching_rule()
    {
        var viewModel = new HomeViewModel();
        var window = new WindowContext(
            (nint)0x1234,
            100,
            200,
            "Notepad",
            @"C:\Windows\notepad.exe",
            "Untitled - Notepad",
            "Notepad",
            null);
        var input = new InputState("Microsoft Pinyin", InputMode.Chinese, 0);

        viewModel.UpdateCurrentState(window, input, matchedAction: null);

        Assert.Equal("未匹配", viewModel.RuleSourceLabel);
        Assert.Equal("不干预", viewModel.RuleTargetLabel);
        Assert.Equal("当前未匹配规则", viewModel.LastRuleSummary);
    }
}
