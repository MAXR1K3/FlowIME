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
    public void Menu_without_target_is_a_disabled_hint()
    {
        var entry = Assert.Single(TrayQuickRuleMenu.Build(null));

        Assert.False(entry.IsEnabled);
        Assert.Null(entry.Choice);
    }

    [Fact]
    public void Menu_checks_the_current_rule_and_offers_removal()
    {
        var entries = TrayQuickRuleMenu.Build(new QuickRuleTarget("微信", WeChat, Rule(InputAction.Chinese)));

        Assert.Equal("微信 · 当前：中文", entries[0].Label);
        Assert.False(entries[0].IsEnabled);
        Assert.Equal(
            [QuickRuleChoice.Chinese],
            entries.Where(static entry => entry.IsChecked).Select(static entry => entry.Choice!.Value));
        Assert.Contains(entries, static entry => entry.Choice == QuickRuleChoice.Remove);
    }

    [Fact]
    public void Menu_without_rule_has_no_check_and_no_removal()
    {
        var entries = TrayQuickRuleMenu.Build(new QuickRuleTarget("微信", WeChat, null));

        Assert.Equal("微信 · 未设置规则", entries[0].Label);
        Assert.DoesNotContain(entries, static entry => entry.IsChecked);
        Assert.DoesNotContain(entries, static entry => entry.Choice == QuickRuleChoice.Remove);
        Assert.All(
            entries.Skip(1),
            static entry => Assert.Equal(entry.Choice, TrayQuickRuleMenu.ChoiceFor(entry.CommandId)));
    }

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
