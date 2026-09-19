using FlowIME.App.ViewModels;
using FlowIME.Core.Abstractions;
using FlowIME.Core.Models;
using FlowIME.Core.Rules;

namespace FlowIME.App.Tests.ViewModels;

public sealed class RulesViewModelTests
{
    [Fact]
    public void Empty_shell_has_an_empty_rule_collection()
    {
        var viewModel = new RulesViewModel(new FakeRuleRepository());

        Assert.Empty(viewModel.Rules);
        Assert.True(viewModel.IsEmpty);
    }

    [Fact]
    public async Task Load_projects_saved_rules_into_the_list()
    {
        var repository = new FakeRuleRepository([
            Rule(@"C:\Apps\Code.exe", InputAction.English, priority: 200)
        ]);
        var viewModel = new RulesViewModel(repository);

        await viewModel.LoadAsync(TestContext.Current.CancellationToken);

        var item = Assert.Single(viewModel.Rules);
        Assert.Equal("Code", item.DisplayName);
        Assert.Equal(@"C:\Apps\Code.exe", item.ExecutablePath);
        Assert.Equal(InputAction.English, item.Action);
        Assert.Equal(InputMethodProviderIds.MicrosoftPinyin, item.ProviderId);
        Assert.False(viewModel.IsEmpty);
    }

    [Fact]
    public async Task Load_projects_global_default_summary()
    {
        var target = new GlobalDefaultTarget(
            InputMethodProviderIds.WeChat,
            InputAction.Chinese);
        var repository = new FakeRuleRepository(globalDefault: target);
        var providers = new[]
        {
            new InputMethodProviderDescriptor(
                InputMethodProviderIds.MicrosoftPinyin,
                "Microsoft Pinyin",
                new InputMethodProviderCapabilities(true, true, true, true, true, true)),
            new InputMethodProviderDescriptor(
                InputMethodProviderIds.WeChat,
                "微信输入法",
                new InputMethodProviderCapabilities(true, true, true, true, true, true))
        };
        var viewModel = new RulesViewModel(repository, providers);

        await viewModel.LoadAsync(TestContext.Current.CancellationToken);

        Assert.True(viewModel.HasGlobalDefault);
        Assert.Equal(target, viewModel.GlobalDefault);
        Assert.Equal("微信输入法 · 中文", viewModel.GlobalDefaultSummary);
    }

    [Fact]
    public async Task Update_global_default_persists_and_can_disable_it()
    {
        var repository = new FakeRuleRepository();
        var viewModel = new RulesViewModel(repository);
        await viewModel.LoadAsync(TestContext.Current.CancellationToken);
        var target = new GlobalDefaultTarget(
            InputMethodProviderIds.WeChat,
            InputAction.English);

        await viewModel.UpdateGlobalDefaultAsync(
            target,
            TestContext.Current.CancellationToken);

        Assert.Equal(target, repository.GlobalDefault);
        Assert.True(viewModel.HasGlobalDefault);
        Assert.Contains("英文", viewModel.GlobalDefaultSummary, StringComparison.Ordinal);

        await viewModel.UpdateGlobalDefaultAsync(
            null,
            TestContext.Current.CancellationToken);

        Assert.Null(repository.GlobalDefault);
        Assert.False(viewModel.HasGlobalDefault);
        Assert.StartsWith("未设置", viewModel.GlobalDefaultSummary);
    }

    [Fact]
    public async Task Add_rule_persists_and_refreshes_the_list()
    {
        var repository = new FakeRuleRepository();
        var viewModel = new RulesViewModel(repository);
        var rule = Rule(@"C:\Apps\WeChat.exe", InputAction.Chinese, priority: 100);

        await viewModel.AddRuleAsync(rule, TestContext.Current.CancellationToken);

        var item = Assert.Single(viewModel.Rules);
        Assert.Equal("WeChat", item.DisplayName);
        Assert.Equal(InputAction.Chinese, item.Action);
        Assert.Single(repository.Rules);
    }

    [Fact]
    public async Task Upsert_same_executable_updates_existing_rule_without_duplicate()
    {
        var existing = Rule(@"C:\Apps\Code.exe", InputAction.English, priority: 500);
        var repository = new FakeRuleRepository([existing]);
        var viewModel = new RulesViewModel(repository);
        await viewModel.LoadAsync(TestContext.Current.CancellationToken);

        var incoming = Rule(@"c:\apps\CODE.exe", InputAction.Chinese, priority: 900) with
        {
            ProviderId = InputMethodProviderIds.WeChat
        };
        var result = await viewModel.UpsertRuleAsync(
            incoming,
            TestContext.Current.CancellationToken);

        Assert.True(result.ReplacedExisting);
        var saved = Assert.Single(repository.Rules);
        Assert.Equal(existing.Id, saved.Id);
        Assert.Equal(existing.Priority, saved.Priority);
        Assert.Equal(InputAction.Chinese, saved.Action);
        Assert.Equal(InputMethodProviderIds.WeChat, saved.ProviderId);
        Assert.Single(viewModel.Rules);
    }

    [Fact]
    public async Task Upsert_same_executable_with_different_advanced_conditions_creates_distinct_rules()
    {
        var broad = Rule(@"C:\Apps\Chrome.exe", InputAction.Chinese, priority: 100);
        var repository = new FakeRuleRepository([broad]);
        var viewModel = new RulesViewModel(repository);
        await viewModel.LoadAsync(TestContext.Current.CancellationToken);
        var docs = Rule(@"C:\Apps\Chrome.exe", InputAction.English, priority: 200) with
        {
            Match = new ApplicationMatch(
                ProcessPath: @"C:\Apps\Chrome.exe",
                WindowTitleContains: "Docs")
        };

        var result = await viewModel.UpsertRuleAsync(
            docs,
            TestContext.Current.CancellationToken);

        Assert.False(result.ReplacedExisting);
        Assert.Equal(2, repository.Rules.Count);
        Assert.Equal(2, viewModel.Rules.Count);
    }

    [Fact]
    public async Task Full_rule_update_persists_match_and_priority()
    {
        var original = Rule(@"C:\Apps\Code.exe", InputAction.English, priority: 100);
        var repository = new FakeRuleRepository([original]);
        var viewModel = new RulesViewModel(repository);
        await viewModel.LoadAsync(TestContext.Current.CancellationToken);
        var updated = original with
        {
            Priority = 450,
            Match = original.Match with { WindowClass = "Chrome_WidgetWin_1" }
        };

        await viewModel.UpdateRuleAsync(updated, TestContext.Current.CancellationToken);

        var saved = Assert.Single(repository.Rules);
        Assert.Equal(450, saved.Priority);
        Assert.Equal("Chrome_WidgetWin_1", saved.Match.WindowClass);
    }

    [Fact]
    public async Task Upsert_packaged_application_uses_package_identity_across_versioned_paths()
    {
        var existing = Rule(
            @"C:\Program Files\WindowsApps\OpenAI.Codex_26.908.0_x64__abc\app\ChatGPT.exe",
            InputAction.English,
            priority: 500) with
        {
            Match = new ApplicationMatch(
                ProcessPath: @"C:\Program Files\WindowsApps\OpenAI.Codex_26.908.0_x64__abc\app\ChatGPT.exe",
                PackageFamilyName: "OpenAI.Codex_abc")
        };
        var repository = new FakeRuleRepository([existing]);
        var viewModel = new RulesViewModel(repository);
        await viewModel.LoadAsync(TestContext.Current.CancellationToken);

        var incoming = Rule(
            @"C:\Program Files\WindowsApps\OpenAI.Codex_26.909.0_x64__abc\app\ChatGPT.exe",
            InputAction.Chinese,
            priority: 900) with
        {
            Match = new ApplicationMatch(
                ProcessPath: @"C:\Program Files\WindowsApps\OpenAI.Codex_26.909.0_x64__abc\app\ChatGPT.exe",
                PackageFamilyName: "OpenAI.Codex_abc")
        };

        var result = await viewModel.UpsertRuleAsync(
            incoming,
            TestContext.Current.CancellationToken);

        Assert.True(result.ReplacedExisting);
        var saved = Assert.Single(repository.Rules);
        Assert.Equal(existing.Id, saved.Id);
        Assert.Equal(existing.Priority, saved.Priority);
        Assert.Equal(InputAction.Chinese, saved.Action);
        Assert.Contains("26.909.0", saved.Match.ProcessPath!, StringComparison.Ordinal);
    }


    [Fact]
    public async Task Upsert_uses_AUMID_as_stable_identity_before_versioned_path()
    {
        var existing = Rule(
            @"C:\Program Files\WindowsApps\Vendor.App_1.0_x64__abc\App.exe",
            InputAction.English,
            priority: 500) with
        {
            Match = new ApplicationMatch(
                ProcessPath: @"C:\Program Files\WindowsApps\Vendor.App_1.0_x64__abc\App.exe",
                ApplicationUserModelId: "Vendor.App_abc!Main")
        };
        var repository = new FakeRuleRepository([existing]);
        var viewModel = new RulesViewModel(repository);
        await viewModel.LoadAsync(TestContext.Current.CancellationToken);

        var incoming = Rule(
            @"C:\Program Files\WindowsApps\Vendor.App_2.0_x64__abc\App.exe",
            InputAction.Chinese,
            priority: 900) with
        {
            Match = new ApplicationMatch(
                ProcessPath: @"C:\Program Files\WindowsApps\Vendor.App_2.0_x64__abc\App.exe",
                ApplicationUserModelId: "Vendor.App_abc!Main")
        };

        var result = await viewModel.UpsertRuleAsync(
            incoming,
            TestContext.Current.CancellationToken);

        Assert.True(result.ReplacedExisting);
        var saved = Assert.Single(repository.Rules);
        Assert.Equal(existing.Id, saved.Id);
        Assert.Equal("Vendor.App_abc!Main", saved.Match.ApplicationUserModelId);
        Assert.Contains("2.0", saved.Match.ProcessPath!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Load_reports_conflicting_exact_overlap_groups()
    {
        var first = Rule(@"C:\Apps\Code.exe", InputAction.Chinese, priority: 200) with
        {
            ProviderId = InputMethodProviderIds.WeChat
        };
        var second = Rule(@"c:\apps\CODE.exe", InputAction.English, priority: 100);
        var repository = new FakeRuleRepository([first, second]);
        var viewModel = new RulesViewModel(repository);

        await viewModel.LoadAsync(TestContext.Current.CancellationToken);

        Assert.True(viewModel.HasRuleDiagnostics);
        Assert.Equal(1, viewModel.RuleConflictGroupCount);
        Assert.Equal(0, viewModel.RuleRedundantGroupCount);
        Assert.Contains("冲突规则", viewModel.RuleDiagnosticsSummary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Load_reports_redundant_exact_overlap_groups()
    {
        var first = Rule(@"C:\Apps\Code.exe", InputAction.Chinese, priority: 200);
        var second = Rule(@"c:\apps\CODE.exe", InputAction.Chinese, priority: 100);
        var repository = new FakeRuleRepository([first, second]);
        var viewModel = new RulesViewModel(repository);

        await viewModel.LoadAsync(TestContext.Current.CancellationToken);

        Assert.True(viewModel.HasRuleDiagnostics);
        Assert.Equal(0, viewModel.RuleConflictGroupCount);
        Assert.Equal(1, viewModel.RuleRedundantGroupCount);
        Assert.Contains("重复规则", viewModel.RuleDiagnosticsSummary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Update_and_delete_rule_refresh_persistence_and_projection()
    {
        var rule = Rule(@"C:\Apps\Code.exe", InputAction.English, priority: 100);
        var repository = new FakeRuleRepository([rule]);
        var viewModel = new RulesViewModel(repository);
        await viewModel.LoadAsync(TestContext.Current.CancellationToken);

        await viewModel.UpdateRuleAsync(
            rule.Id,
            InputAction.Chinese,
            enabled: false,
            providerId: InputMethodProviderIds.WeChat,
            cancellationToken: TestContext.Current.CancellationToken);

        var updated = Assert.Single(repository.Rules);
        Assert.False(updated.Enabled);
        Assert.Equal(InputAction.Chinese, updated.Action);
        Assert.Equal(InputMethodProviderIds.WeChat, updated.ProviderId);
        Assert.False(Assert.Single(viewModel.Rules).Enabled);

        await viewModel.DeleteRuleAsync(rule.Id, TestContext.Current.CancellationToken);

        Assert.Empty(repository.Rules);
        Assert.Empty(viewModel.Rules);
        Assert.True(viewModel.IsEmpty);
    }

    [Fact]
    public async Task Process_only_rule_remains_visible_and_editable()
    {
        var rule = new ApplicationRule(
            Guid.NewGuid(),
            true,
            100,
            new ApplicationMatch(ProcessName: "portable-tool"),
            InputAction.English,
            "Portable Tool");
        var repository = new FakeRuleRepository([rule]);
        var viewModel = new RulesViewModel(repository);

        await viewModel.LoadAsync(TestContext.Current.CancellationToken);

        var item = Assert.Single(viewModel.Rules);
        Assert.Equal(rule.Id, item.Id);
        Assert.Equal("portable-tool", item.ExecutableName);
        Assert.Equal(string.Empty, item.ExecutablePath);
    }

    private static ApplicationRule Rule(
        string path,
        InputAction action,
        int priority) =>
        new(
            Guid.NewGuid(),
            true,
            priority,
            new ApplicationMatch(ProcessPath: path),
            action);

    private sealed class FakeRuleRepository(
        IReadOnlyList<ApplicationRule>? initial = null,
        GlobalDefaultTarget? globalDefault = null) : IRuleRepository
    {
        public IReadOnlyList<ApplicationRule> Rules { get; private set; } =
            initial ?? Array.Empty<ApplicationRule>();

        public GlobalDefaultTarget? GlobalDefault { get; private set; } = globalDefault;

        public ValueTask<RuleConfigurationSnapshot> GetConfigurationAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(
                new RuleConfigurationSnapshot(Rules, GlobalDefault));
        }

        public ValueTask<IReadOnlyList<ApplicationRule>> GetRulesAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Rules);
        }

        public ValueTask ReplaceRulesAsync(
            IReadOnlyList<ApplicationRule> rules,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Rules = rules.ToArray();
            return ValueTask.CompletedTask;
        }

        public ValueTask<GlobalDefaultTarget?> GetGlobalDefaultAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(GlobalDefault);
        }

        public ValueTask ReplaceGlobalDefaultAsync(
            GlobalDefaultTarget? target,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GlobalDefault = target;
            return ValueTask.CompletedTask;
        }
    }
}
