using FlowIME.Core.Models;
using FlowIME.Core.Rules;

namespace FlowIME.Core.Tests.Rules;

public sealed class RuleEngineTests
{
    private readonly RuleEngine _engine = new();

    [Fact]
    public void Exact_process_path_matches_case_insensitively()
    {
        var rule = Rule(
            new ApplicationMatch(ProcessPath: @"C:\Apps\Code.exe"),
            InputAction.English);

        var result = _engine.Match(
            Window(executablePath: @"c:\apps\CODE.EXE"),
            [rule]);

        Assert.True(result.IsMatch);
        Assert.Equal(InputAction.English, result.Action);
        Assert.Equal(rule.Id, result.Rule!.Id);
    }

    [Fact]
    public void Stale_process_path_falls_back_to_same_executable_name()
    {
        var rule = Rule(
            new ApplicationMatch(ProcessPath: @"C:\Apps\Codex\1.0.0\CodexBrowser.exe"),
            InputAction.Chinese);

        var result = _engine.Match(
            Window(
                processName: "CodexBrowser",
                executablePath: @"C:\Apps\Codex\2.0.0\CodexBrowser.exe"),
            [rule]);

        Assert.True(result.IsMatch);
        Assert.Equal(rule.Id, result.Rule!.Id);
        Assert.Equal(InputAction.Chinese, result.Action);
    }

    [Fact]
    public void Packaged_rule_survives_versioned_path_change_when_package_identity_matches()
    {
        var rule = Rule(
            new ApplicationMatch(
                ProcessPath: @"C:\Program Files\WindowsApps\OpenAI.Codex_26.908.0_x64__abc\app\ChatGPT.exe",
                PackageFamilyName: "OpenAI.Codex_abc"),
            InputAction.Chinese);

        var result = _engine.Match(
            Window(
                processName: "ChatGPT",
                executablePath: @"C:\Program Files\WindowsApps\OpenAI.Codex_26.909.0_x64__abc\app\ChatGPT.exe",
                packageFamilyName: "OpenAI.Codex_abc"),
            [rule]);

        Assert.True(result.IsMatch);
        Assert.Equal(rule.Id, result.Rule!.Id);
    }


    [Fact]
    public void Aumid_rule_survives_versioned_path_change_and_requires_same_app_identity()
    {
        var rule = Rule(
            new ApplicationMatch(
                ProcessPath: @"C:\Program Files\WindowsApps\Vendor.App_1.0_x64__abc\App.exe",
                ApplicationUserModelId: "Vendor.App_abc!Main"),
            InputAction.English);

        var sameApp = _engine.Match(
            Window(
                processName: "App",
                executablePath: @"C:\Program Files\WindowsApps\Vendor.App_2.0_x64__abc\App.exe",
                applicationUserModelId: "Vendor.App_abc!Main"),
            [rule]);
        var differentApp = _engine.Match(
            Window(
                processName: "App",
                executablePath: @"C:\Program Files\WindowsApps\Vendor.App_2.0_x64__abc\App.exe",
                applicationUserModelId: "Vendor.App_abc!Other"),
            [rule]);

        Assert.True(sameApp.IsMatch);
        Assert.False(differentApp.IsMatch);
    }


    [Fact]
    public void Aumid_rule_falls_back_to_package_identity_when_runtime_AUMID_is_unavailable()
    {
        var rule = Rule(
            new ApplicationMatch(
                ProcessPath: @"C:\Program Files\WindowsApps\Vendor.App_1.0_x64__abc\App.exe",
                PackageFamilyName: "Vendor.App_abc",
                ApplicationUserModelId: "Vendor.App_abc!Main"),
            InputAction.English);

        var result = _engine.Match(
            Window(
                processName: "App",
                executablePath: @"C:\Program Files\WindowsApps\Vendor.App_2.0_x64__abc\App.exe",
                packageFamilyName: "Vendor.App_abc",
                applicationUserModelId: null),
            [rule]);

        Assert.True(result.IsMatch);
    }

    [Fact]
    public void Stale_process_path_does_not_match_different_executable_name()
    {
        var rule = Rule(
            new ApplicationMatch(ProcessPath: @"C:\Apps\Codex\CodexBrowser.exe"),
            InputAction.Chinese);

        var result = _engine.Match(
            Window(
                processName: "notepad",
                executablePath: @"C:\Windows\System32\notepad.exe"),
            [rule]);

        Assert.False(result.IsMatch);
    }

    [Fact]
    public void Disabled_rule_does_not_match()
    {
        var rule = Rule(
            new ApplicationMatch(ProcessName: "Code"),
            InputAction.English,
            enabled: false);

        var result = _engine.Match(Window(processName: "Code"), [rule]);

        Assert.False(result.IsMatch);
        Assert.Equal(InputAction.Keep, result.Action);
    }

    [Fact]
    public void Higher_priority_wins_before_specificity()
    {
        var highPriority = Rule(
            new ApplicationMatch(ProcessName: "Code"),
            InputAction.Chinese,
            priority: 200);
        var lowerPriority = Rule(
            new ApplicationMatch(
                ProcessPath: @"C:\Apps\Code.exe",
                WindowTitleContains: "FlowIME"),
            InputAction.English,
            priority: 100);

        var result = _engine.Match(
            Window(
                processName: "Code",
                executablePath: @"C:\Apps\Code.exe",
                windowTitle: "FlowIME - Visual Studio Code"),
            [lowerPriority, highPriority]);

        Assert.Equal(highPriority.Id, result.Rule!.Id);
        Assert.Equal(InputAction.Chinese, result.Action);
    }

    [Fact]
    public void More_specific_title_rule_wins_when_priority_is_equal()
    {
        var processRule = Rule(
            new ApplicationMatch(ProcessName: "chrome"),
            InputAction.Keep);
        var titleRule = Rule(
            new ApplicationMatch(
                ProcessName: "chrome",
                WindowTitleContains: "Visual Studio Code for Web"),
            InputAction.English);

        var result = _engine.Match(
            Window(
                processName: "chrome",
                windowTitle: "repo - Visual Studio Code for Web"),
            [processRule, titleRule]);

        Assert.Equal(titleRule.Id, result.Rule!.Id);
        Assert.Equal(InputAction.English, result.Action);
    }

    [Fact]
    public void Process_name_fallback_matches()
    {
        var rule = Rule(
            new ApplicationMatch(ProcessName: "WeChat"),
            InputAction.Chinese);

        var result = _engine.Match(Window(processName: "wechat"), [rule]);

        Assert.Equal(InputAction.Chinese, result.Action);
    }

    [Fact]
    public void No_rule_returns_keep_without_claiming_a_match()
    {
        var result = _engine.Match(Window(processName: "notepad"), []);

        Assert.False(result.IsMatch);
        Assert.Null(result.Rule);
        Assert.Equal(InputAction.Keep, result.Action);
    }

    [Fact]
    public void Resolve_uses_global_default_when_no_application_rule_matches()
    {
        var fallback = new GlobalDefaultTarget(
            InputMethodProviderIds.WeChat,
            InputAction.Chinese);

        var result = _engine.Resolve(
            Window(processName: "notepad"),
            [],
            fallback);

        Assert.True(result.HasTarget);
        Assert.Equal(RuleResolutionSource.GlobalDefault, result.Source);
        Assert.Equal(InputMethodProviderIds.WeChat, result.ProviderId);
        Assert.Equal(InputAction.Chinese, result.Action);
        Assert.Null(result.Rule);
    }

    [Fact]
    public void Resolve_application_rule_overrides_global_default()
    {
        var rule = Rule(
            new ApplicationMatch(ProcessName: "Code"),
            InputAction.English);
        var fallback = new GlobalDefaultTarget(
            InputMethodProviderIds.WeChat,
            InputAction.Chinese);

        var result = _engine.Resolve(
            Window(processName: "Code"),
            [rule],
            fallback);

        Assert.Equal(RuleResolutionSource.ApplicationRule, result.Source);
        Assert.Equal(rule.Id, result.Rule!.Id);
        Assert.Equal(InputMethodProviderIds.MicrosoftPinyin, result.ProviderId);
        Assert.Equal(InputAction.English, result.Action);
    }

    [Fact]
    public void Resolve_explicit_keep_rule_suppresses_global_default()
    {
        var rule = Rule(
            new ApplicationMatch(ProcessName: "Code"),
            InputAction.Keep);
        var fallback = new GlobalDefaultTarget(
            InputMethodProviderIds.WeChat,
            InputAction.Chinese);

        var result = _engine.Resolve(
            Window(processName: "Code"),
            [rule],
            fallback);

        Assert.Equal(RuleResolutionSource.ApplicationRule, result.Source);
        Assert.Equal(InputAction.Keep, result.Action);
        Assert.Equal(rule.Id, result.Rule!.Id);
    }

    [Fact]
    public void Resolve_disabled_rule_allows_global_default()
    {
        var rule = Rule(
            new ApplicationMatch(ProcessName: "Code"),
            InputAction.English,
            enabled: false);
        var fallback = new GlobalDefaultTarget(
            InputMethodProviderIds.WeChat,
            InputAction.Chinese);

        var result = _engine.Resolve(
            Window(processName: "Code"),
            [rule],
            fallback);

        Assert.Equal(RuleResolutionSource.GlobalDefault, result.Source);
        Assert.Equal(InputAction.Chinese, result.Action);
    }

    [Fact]
    public void Null_executable_path_is_safe()
    {
        var rule = Rule(
            new ApplicationMatch(ProcessPath: @"C:\Apps\Admin.exe"),
            InputAction.English);

        var result = _engine.Match(Window(executablePath: null), [rule]);

        Assert.False(result.IsMatch);
    }

    [Fact]
    public void Empty_match_is_not_a_global_wildcard()
    {
        var rule = Rule(new ApplicationMatch(), InputAction.English);

        var result = _engine.Match(Window(), [rule]);

        Assert.False(result.IsMatch);
    }

    [Fact]
    public void Every_specified_match_field_must_match()
    {
        var rule = Rule(
            new ApplicationMatch(
                ProcessName: "Code",
                WindowClass: "Chrome_WidgetWin_1"),
            InputAction.English);

        var result = _engine.Match(
            Window(processName: "Code", windowClass: "DifferentClass"),
            [rule]);

        Assert.False(result.IsMatch);
    }

    [Fact]
    public void Blank_optional_match_fields_are_ignored_defensively()
    {
        var rule = Rule(
            new ApplicationMatch(
                ProcessPath: @"C:\Apps\Code.exe",
                ProcessName: "   ",
                WindowClass: "",
                WindowTitleContains: "  "),
            InputAction.English);

        var result = _engine.Match(
            Window(processName: "Code", executablePath: @"C:\Apps\Code.exe"),
            [rule]);

        Assert.True(result.IsMatch);
    }

    private static ApplicationRule Rule(
        ApplicationMatch match,
        InputAction action,
        int priority = 100,
        bool enabled = true) =>
        new(Guid.NewGuid(), enabled, priority, match, action);

    private static WindowContext Window(
        string processName = "notepad",
        string? executablePath = @"C:\Windows\notepad.exe",
        string? windowTitle = "Untitled - Notepad",
        string? windowClass = "Notepad",
        string? packageFamilyName = null,
        string? applicationUserModelId = null) =>
        new(
            Hwnd: (nint)0x1234,
            ProcessId: 100,
            ThreadId: 200,
            ProcessName: processName,
            ExecutablePath: executablePath,
            WindowTitle: windowTitle,
            WindowClass: windowClass,
            PackageFamilyName: packageFamilyName,
            ApplicationUserModelId: applicationUserModelId);
}
