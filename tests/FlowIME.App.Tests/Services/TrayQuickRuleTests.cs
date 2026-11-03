using FlowIME.App.Services;
using FlowIME.Core.Abstractions;
using FlowIME.Core.Models;
using FlowIME.Core.Rules;

namespace FlowIME.App.Tests.Services;

public sealed class TrayQuickRuleTests
{
    private static readonly WindowContext WeChat = new(
        (nint)0x10, 30, 31, "Weixin", @"C:\Apps\Weixin.exe", "微信", "Qt51514QWindowIcon", null);

    private static readonly WindowContext Taskbar = new(
        (nint)0x20, 40, 41, "explorer", @"C:\Windows\explorer.exe", null, "Shell_TrayWnd", null);

    private static readonly WindowContext FileExplorer = Taskbar with { Hwnd = 0x21, WindowClass = "CabinetWClass" };

    [Fact]
    public void Tracker_keeps_the_last_application_when_the_taskbar_takes_focus()
    {
        var tracker = new QuickRuleTargetTracker(static _ => "微信");

        tracker.Observe(Snapshot(WeChat));
        tracker.Observe(Snapshot(Taskbar));

        Assert.Equal(WeChat, tracker.Current?.Window);
        Assert.Equal("微信", tracker.Current?.DisplayName);
    }

    [Fact]
    public void File_explorer_windows_are_valid_targets()
    {
        Assert.True(QuickRuleTargetTracker.IsEligible(FileExplorer));
        Assert.False(QuickRuleTargetTracker.IsEligible(Taskbar));
        Assert.False(QuickRuleTargetTracker.IsEligible(WeChat with { ExecutablePath = null }));
    }

    [Fact]
    public void Tracker_only_reports_application_rules_as_existing()
    {
        var tracker = new QuickRuleTargetTracker(static _ => "微信");
        var rule = Rule(InputAction.Chinese);

        tracker.Observe(Snapshot(WeChat, rule, RuleResolutionSource.GlobalDefault));
        Assert.Null(tracker.Current?.ExistingRule);

        tracker.Observe(Snapshot(WeChat, rule, RuleResolutionSource.ApplicationRule));
        Assert.Equal(rule, tracker.Current?.ExistingRule);
    }

    [Fact]
    public void Menu_opens_with_a_default_open_item_and_ends_with_exit()
    {
        var menu = TrayMenu.Build(new TrayMenuState(true, null, null));

        Assert.Equal(TrayCommand.Open, menu[0].Command);
        Assert.True(menu[0].IsDefault);
        Assert.Equal(TrayCommand.Exit, menu[^1].Command);
        Assert.Single(menu, static item => item.IsDefault);
    }

    [Fact]
    public void Menu_without_target_is_a_disabled_hint()
    {
        var item = QuickRuleItem(TrayMenu.Build(new TrayMenuState(true, null, null)));

        Assert.False(item.IsEnabled);
        Assert.Null(item.Children);
    }

    [Fact]
    public void Quick_rule_submenu_marks_the_current_rule_and_offers_removal()
    {
        var item = QuickRuleItem(TrayMenu.Build(
            new TrayMenuState(true, null, new QuickRuleTarget("微信", WeChat, Rule(InputAction.Chinese)))));

        Assert.Equal("微信\t进入时：中文", item.Label);
        Assert.Equal(
            [TrayCommand.QuickRuleChinese],
            item.Children!.Where(static child => child.IsChecked).Select(static child => child.Command));
        Assert.Contains(item.Children!, static child => child.Command == TrayCommand.QuickRuleRemove);
        Assert.Contains(item.Children!, static child => child.Command == TrayCommand.OpenRules);
    }

    [Fact]
    public void Quick_rule_submenu_without_rule_has_no_mark_and_no_removal()
    {
        var item = QuickRuleItem(TrayMenu.Build(
            new TrayMenuState(true, null, new QuickRuleTarget("微信", WeChat, null))));

        Assert.Equal("微信\t未设置", item.Label);
        Assert.DoesNotContain(item.Children!, static child => child.IsChecked);
        Assert.DoesNotContain(item.Children!, static child => child.Command == TrayCommand.QuickRuleRemove);
        Assert.Equal(
            [QuickRuleChoice.Chinese, QuickRuleChoice.English, QuickRuleChoice.Keep],
            item.Children!.Select(static child => TrayMenu.ChoiceFor(child.Command)).OfType<QuickRuleChoice>());
    }

    [Fact]
    public void Long_application_names_are_shortened_and_ampersands_are_not_mnemonics()
    {
        var item = QuickRuleItem(TrayMenu.Build(new TrayMenuState(
            true,
            null,
            new QuickRuleTarget("Tom & Jerry Extremely Long Application Name", WeChat, null))));

        var name = item.Label.Split('\t')[0];
        Assert.StartsWith("Tom && Jerry", name, StringComparison.Ordinal);
        Assert.EndsWith("…", name, StringComparison.Ordinal);
    }

    [Fact]
    public void Running_automation_offers_timed_pauses()
    {
        var pause = TrayMenu.Build(new TrayMenuState(true, null, null))
            .Single(static item => item.Label == "暂停自动切换");

        Assert.Equal(
            [TrayCommand.PauseFifteenMinutes, TrayCommand.PauseOneHour, TrayCommand.PauseUntilResumed],
            pause.Children!.Select(static child => child.Command));
        Assert.Equal(TimeSpan.FromMinutes(15), TrayMenu.PauseDurationFor(TrayCommand.PauseFifteenMinutes));
        Assert.Equal(TimeSpan.FromHours(1), TrayMenu.PauseDurationFor(TrayCommand.PauseOneHour));
        Assert.Null(TrayMenu.PauseDurationFor(TrayCommand.PauseUntilResumed));
    }

    [Fact]
    public void Paused_automation_offers_resume_with_the_resume_time()
    {
        var until = new DateTimeOffset(2026, 11, 3, 14, 30, 0, TimeSpan.Zero);

        var timed = TrayMenu.Build(new TrayMenuState(false, until, null), TimeZoneInfo.Utc)
            .Single(static item => item.Command == TrayCommand.Resume);
        var open = TrayMenu.Build(new TrayMenuState(false, null, null), TimeZoneInfo.Utc)
            .Single(static item => item.Command == TrayCommand.Resume);

        Assert.Equal("恢复自动切换\t14:30 自动恢复", timed.Label);
        Assert.Equal("恢复自动切换\t已暂停", open.Label);
        Assert.Equal("FlowIME · 已暂停，14:30 自动恢复", TrayMenu.DescribeStatus(false, until, TimeZoneInfo.Utc));
        Assert.Equal("FlowIME · 自动切换运行中", TrayMenu.DescribeStatus(true, null));
        Assert.Equal("14:30 自动恢复", TrayMenu.DescribeResume(until, TimeZoneInfo.Utc));
        Assert.Equal("需要手动恢复", TrayMenu.DescribeResume(null));
    }

    private static TrayMenuItem QuickRuleItem(IReadOnlyList<TrayMenuItem> menu) =>
        menu.Where(static item => !item.IsSeparator).ElementAt(1);

    [Fact]
    public async Task New_quick_rule_is_scoped_to_the_executable_with_the_default_provider()
    {
        var repository = new MemoryRuleRepository([Rule(InputAction.English, path: @"C:\Apps\Code.exe", priority: 300)]);
        var service = Service(repository);

        var result = await service.ApplyAsync(
            new QuickRuleTarget("微信", WeChat, null),
            QuickRuleChoice.Chinese,
            TestContext.Current.CancellationToken);

        var created = Assert.Single(repository.Rules, static rule => rule.Match.ProcessPath == WeChat.ExecutablePath);
        Assert.Equal(InputAction.Chinese, created.Action);
        Assert.Equal(400, created.Priority);
        Assert.Equal("微信", created.DisplayName);
        Assert.Equal(InputMethodProviderIds.WeChat, created.ProviderId);
        Assert.Null(created.Match.WindowTitleContains);
        Assert.Equal(created, result.Rule);
    }

    [Fact]
    public async Task New_quick_rule_follows_the_global_default_input_method()
    {
        var repository = new MemoryRuleRepository(
            [],
            new GlobalDefaultTarget(InputMethodProviderIds.WeChat, InputAction.Chinese));
        var service = new QuickRuleService(repository, [], static () => InputMethodProviderIds.MicrosoftPinyin);

        await service.ApplyAsync(
            new QuickRuleTarget("微信", WeChat, null),
            QuickRuleChoice.English,
            TestContext.Current.CancellationToken);

        Assert.Equal(InputMethodProviderIds.WeChat, Assert.Single(repository.Rules).ProviderId);
    }

    [Fact]
    public async Task Quick_rule_updates_an_existing_rule_in_place()
    {
        var existing = Rule(InputAction.Chinese, enabled: false) with
        {
            Match = new ApplicationMatch(ProcessPath: WeChat.ExecutablePath, WindowTitleContains: "聊天"),
            ProviderId = InputMethodProviderIds.MicrosoftPinyin
        };
        var repository = new MemoryRuleRepository([existing]);

        await Service(repository).ApplyAsync(
            new QuickRuleTarget("微信", WeChat, existing),
            QuickRuleChoice.English,
            TestContext.Current.CancellationToken);

        var updated = Assert.Single(repository.Rules);
        Assert.Equal(existing.Id, updated.Id);
        Assert.Equal(existing.Priority, updated.Priority);
        Assert.Equal("聊天", updated.Match.WindowTitleContains);
        Assert.Equal(InputMethodProviderIds.MicrosoftPinyin, updated.ProviderId);
        Assert.Equal(InputAction.English, updated.Action);
        Assert.True(updated.Enabled);
    }

    [Fact]
    public async Task Remove_deletes_the_application_rule()
    {
        var existing = Rule(InputAction.Chinese);
        var repository = new MemoryRuleRepository([existing]);

        var result = await Service(repository).ApplyAsync(
            new QuickRuleTarget("微信", WeChat, null),
            QuickRuleChoice.Remove,
            TestContext.Current.CancellationToken);

        Assert.Empty(repository.Rules);
        Assert.Null(result.Rule);
    }

    private static QuickRuleService Service(IRuleRepository repository) =>
        new(repository, [], static () => InputMethodProviderIds.WeChat);

    private static ApplicationRule Rule(
        InputAction action,
        string? path = null,
        int priority = 100,
        bool enabled = true) =>
        new(
            Guid.NewGuid(),
            enabled,
            priority,
            new ApplicationMatch(ProcessPath: path ?? WeChat.ExecutablePath),
            action,
            "微信");

    private static CurrentStateSnapshot Snapshot(
        WindowContext window,
        ApplicationRule? rule = null,
        RuleResolutionSource source = RuleResolutionSource.None) =>
        new(
            window,
            new InputState("Microsoft Pinyin", InputMode.Chinese, (nint)0x08040804),
            rule?.Action,
            rule,
            ResolutionSource: source);

    private sealed class MemoryRuleRepository(
        IReadOnlyList<ApplicationRule> initial,
        GlobalDefaultTarget? globalDefault = null) : IRuleRepository
    {
        public IReadOnlyList<ApplicationRule> Rules { get; private set; } = initial;

        public ValueTask<RuleConfigurationSnapshot> GetConfigurationAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new RuleConfigurationSnapshot(Rules, globalDefault));

        public ValueTask<IReadOnlyList<ApplicationRule>> GetRulesAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Rules);

        public ValueTask ReplaceRulesAsync(
            IReadOnlyList<ApplicationRule> rules,
            CancellationToken cancellationToken = default)
        {
            Rules = rules.ToArray();
            return ValueTask.CompletedTask;
        }

        public ValueTask<GlobalDefaultTarget?> GetGlobalDefaultAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(globalDefault);

        public ValueTask ReplaceGlobalDefaultAsync(
            GlobalDefaultTarget? target,
            CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }
}
