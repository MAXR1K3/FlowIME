using FlowIME.Core.Models;
using FlowIME.Core.Rules;
using FlowIME.Infrastructure.Configuration;

namespace FlowIME.Infrastructure.Tests.Configuration;

public sealed class JsonRuleRepositoryTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "FlowIME.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Missing_rules_file_returns_empty_collection()
    {
        var repository = CreateRepository();

        var rules = await repository.GetRulesAsync(TestContext.Current.CancellationToken);

        Assert.Empty(rules);
        Assert.False(File.Exists(Path.Combine(_root, "rules.json")));
        Assert.Equal(RuleRepositoryHealthState.Missing, repository.Diagnostics.State);
    }

    [Fact]
    public async Task Rules_round_trip_with_human_readable_schema()
    {
        var rule = new ApplicationRule(
            Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            Enabled: false,
            Priority: 275,
            Match: new ApplicationMatch(
                ProcessPath: @"C:\Apps\Code.exe",
                ProcessName: "Code",
                WindowTitleContains: "Program.cs",
                WindowClass: "Chrome_WidgetWin_1",
                PackageFamilyName: "Vendor.Code_abc",
                ApplicationUserModelId: "Vendor.Code_abc!Main"),
            Action: InputAction.Chinese);
        var repository = CreateRepository();

        await repository.ReplaceRulesAsync([rule], TestContext.Current.CancellationToken);

        var rulesPath = Path.Combine(_root, "rules.json");
        var json = await File.ReadAllTextAsync(
            rulesPath,
            TestContext.Current.CancellationToken);
        Assert.Contains("\"schemaVersion\": 2", json, StringComparison.Ordinal);
        Assert.Contains("\"action\": \"Chinese\"", json, StringComparison.Ordinal);
        Assert.Contains("\"enabled\": false", json, StringComparison.Ordinal);
        Assert.Contains("\"priority\": 275", json, StringComparison.Ordinal);
        Assert.Contains(
            "\"providerId\": \"microsoft-pinyin\"",
            json,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"applicationUserModelId\": \"Vendor.Code_abc!Main\"",
            json,
            StringComparison.Ordinal);

        var reloaded = CreateRepository();
        var loadedRules = await reloaded.GetRulesAsync(TestContext.Current.CancellationToken);

        var loaded = Assert.Single(loadedRules);
        Assert.Equal(rule, loaded);
        Assert.Equal(RuleRepositoryHealthState.Healthy, reloaded.Diagnostics.State);
    }


    [Fact]
    public async Task Legacy_rule_without_provider_id_is_migrated_to_microsoft_pinyin()
    {
        Directory.CreateDirectory(_root);
        var id = Guid.NewGuid();
        var legacyJson = $$"""
        {
          "schemaVersion": 1,
          "rules": [
            {
              "id": "{{id}}",
              "enabled": true,
              "priority": 100,
              "match": { "processPath": "C:\\Apps\\Code.exe" },
              "action": "English",
              "displayName": "Code"
            }
          ]
        }
        """;
        await File.WriteAllTextAsync(
            Path.Combine(_root, "rules.json"),
            legacyJson,
            TestContext.Current.CancellationToken);
        var repository = CreateRepository();

        var rules = await repository.GetRulesAsync(TestContext.Current.CancellationToken);

        var rule = Assert.Single(rules);
        Assert.Equal(id, rule.Id);
        Assert.Equal(InputMethodProviderIds.MicrosoftPinyin, rule.ProviderId);
    }

    [Fact]
    public async Task Legacy_blank_optional_match_fields_are_normalized_to_null()
    {
        Directory.CreateDirectory(_root);
        var id = Guid.NewGuid();
        var legacyJson = $$"""
        {
          "schemaVersion": 1,
          "rules": [
            {
              "id": "{{id}}",
              "enabled": true,
              "priority": 100,
              "match": {
                "processPath": "C:\\Apps\\Code.exe",
                "processName": "   ",
                "windowTitleContains": "",
                "windowClass": "  "
              },
              "action": "English"
            }
          ]
        }
        """;
        await File.WriteAllTextAsync(
            Path.Combine(_root, "rules.json"),
            legacyJson,
            TestContext.Current.CancellationToken);
        var repository = CreateRepository();

        var rule = Assert.Single(
            await repository.GetRulesAsync(TestContext.Current.CancellationToken));

        Assert.Null(rule.Match.ProcessName);
        Assert.Null(rule.Match.WindowTitleContains);
        Assert.Null(rule.Match.WindowClass);
        Assert.Equal(@"C:\Apps\Code.exe", rule.Match.ProcessPath);
    }

    [Fact]
    public async Task Wechat_provider_id_round_trips_through_rules_json_and_last_good_backup()
    {
        var repository = CreateRepository();
        var rule = Rule(InputAction.Chinese, priority: 100) with
        {
            ProviderId = InputMethodProviderIds.WeChat
        };

        await repository.ReplaceRulesAsync([rule], TestContext.Current.CancellationToken);

        var primaryText = await File.ReadAllTextAsync(
            Path.Combine(_root, "rules.json"),
            TestContext.Current.CancellationToken);
        Assert.Contains(
            "\"providerId\": \"wechat-input-method\"",
            primaryText,
            StringComparison.Ordinal);

        var reloaded = CreateRepository();
        var loaded = Assert.Single(
            await reloaded.GetRulesAsync(TestContext.Current.CancellationToken));
        Assert.Equal(InputMethodProviderIds.WeChat, loaded.ProviderId);
    }

    [Fact]
    public async Task Global_default_round_trips_in_schema_v2_and_last_good_backup()
    {
        var repository = CreateRepository();
        var target = new GlobalDefaultTarget(
            InputMethodProviderIds.WeChat,
            InputAction.Chinese);

        await repository.ReplaceGlobalDefaultAsync(
            target,
            TestContext.Current.CancellationToken);

        var primaryPath = Path.Combine(_root, "rules.json");
        var backupPath = Path.Combine(_root, "rules.last-good.json");
        var primary = await File.ReadAllTextAsync(
            primaryPath,
            TestContext.Current.CancellationToken);
        var backup = await File.ReadAllTextAsync(
            backupPath,
            TestContext.Current.CancellationToken);

        Assert.Contains("\"schemaVersion\": 2", primary, StringComparison.Ordinal);
        Assert.Contains("\"defaultTarget\"", primary, StringComparison.Ordinal);
        Assert.Contains("\"providerId\": \"wechat-input-method\"", primary, StringComparison.Ordinal);
        Assert.Contains("\"action\": \"Chinese\"", primary, StringComparison.Ordinal);
        Assert.Equal(primary, backup);

        var reloaded = CreateRepository();
        Assert.Equal(
            target,
            await reloaded.GetGlobalDefaultAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Replacing_application_rules_preserves_global_default()
    {
        var repository = CreateRepository();
        var target = new GlobalDefaultTarget(
            InputMethodProviderIds.WeChat,
            InputAction.English);
        var first = Rule(InputAction.Chinese, priority: 100);
        var second = Rule(InputAction.English, priority: 200);

        await repository.ReplaceGlobalDefaultAsync(
            target,
            TestContext.Current.CancellationToken);
        await repository.ReplaceRulesAsync(
            [first],
            TestContext.Current.CancellationToken);
        await repository.ReplaceRulesAsync(
            [second],
            TestContext.Current.CancellationToken);

        var reloaded = CreateRepository();
        Assert.Equal(
            target,
            await reloaded.GetGlobalDefaultAsync(TestContext.Current.CancellationToken));
        Assert.Equal(
            second,
            Assert.Single(await reloaded.GetRulesAsync(TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task Replacing_global_default_preserves_application_rules()
    {
        var rule = Rule(InputAction.English, priority: 300);
        var repository = CreateRepository();
        await repository.ReplaceRulesAsync(
            [rule],
            TestContext.Current.CancellationToken);

        var target = new GlobalDefaultTarget(
            InputMethodProviderIds.WeChat,
            InputAction.Chinese);
        await repository.ReplaceGlobalDefaultAsync(
            target,
            TestContext.Current.CancellationToken);

        var reloaded = CreateRepository();
        Assert.Equal(
            rule,
            Assert.Single(await reloaded.GetRulesAsync(TestContext.Current.CancellationToken)));
        Assert.Equal(
            target,
            await reloaded.GetGlobalDefaultAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Disabling_global_default_removes_default_target_without_touching_rules()
    {
        var rule = Rule(InputAction.Chinese, priority: 100);
        var repository = CreateRepository();
        await repository.ReplaceRulesAsync(
            [rule],
            TestContext.Current.CancellationToken);
        await repository.ReplaceGlobalDefaultAsync(
            new GlobalDefaultTarget(InputMethodProviderIds.WeChat, InputAction.English),
            TestContext.Current.CancellationToken);

        await repository.ReplaceGlobalDefaultAsync(
            null,
            TestContext.Current.CancellationToken);

        var reloaded = CreateRepository();
        Assert.Null(await reloaded.GetGlobalDefaultAsync(TestContext.Current.CancellationToken));
        Assert.Equal(
            rule,
            Assert.Single(await reloaded.GetRulesAsync(TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task Global_default_rejects_keep_action()
    {
        var repository = CreateRepository();

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await repository.ReplaceGlobalDefaultAsync(
                new GlobalDefaultTarget(InputMethodProviderIds.WeChat, InputAction.Keep),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Existing_primary_from_pre_p4_install_gets_last_good_copy_on_first_load()
    {
        var rule = Rule(InputAction.English, priority: 120);
        var writer = CreateRepository();
        await writer.ReplaceRulesAsync([rule], TestContext.Current.CancellationToken);
        writer.Dispose();
        File.Delete(Path.Combine(_root, "rules.last-good.json"));

        var repository = CreateRepository();
        var rules = await repository.GetRulesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(rule, Assert.Single(rules));
        Assert.True(File.Exists(Path.Combine(_root, "rules.last-good.json")));
        Assert.Equal(RuleRepositoryHealthState.Healthy, repository.Diagnostics.State);
    }

    [Fact]
    public async Task Successful_save_also_writes_latest_last_good_copy()
    {
        var repository = CreateRepository();
        var rule = Rule(InputAction.Chinese, priority: 100);

        await repository.ReplaceRulesAsync([rule], TestContext.Current.CancellationToken);

        var primary = Path.Combine(_root, "rules.json");
        var backup = Path.Combine(_root, "rules.last-good.json");
        Assert.True(File.Exists(primary));
        Assert.True(File.Exists(backup));
        Assert.Equal(
            await File.ReadAllTextAsync(primary, TestContext.Current.CancellationToken),
            await File.ReadAllTextAsync(backup, TestContext.Current.CancellationToken));
        Assert.Equal(RuleRepositoryHealthState.Healthy, repository.Diagnostics.State);
    }

    [Fact]
    public async Task Replacing_rules_atomically_replaces_previous_document()
    {
        var first = Rule(InputAction.Chinese, priority: 100);
        var second = Rule(InputAction.English, priority: 500);
        var repository = CreateRepository();

        await repository.ReplaceRulesAsync([first], TestContext.Current.CancellationToken);
        await repository.ReplaceRulesAsync([second], TestContext.Current.CancellationToken);

        var reloaded = CreateRepository();
        var rules = await reloaded.GetRulesAsync(TestContext.Current.CancellationToken);
        var loaded = Assert.Single(rules);
        Assert.Equal(second, loaded);
        Assert.Empty(Directory.EnumerateFiles(_root, "*.tmp", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task Corrupt_primary_is_backed_up_and_last_good_copy_is_restored()
    {
        var rule = Rule(InputAction.English, priority: 500);
        var writer = CreateRepository();
        await writer.ReplaceRulesAsync([rule], TestContext.Current.CancellationToken);
        writer.Dispose();

        var rulesPath = Path.Combine(_root, "rules.json");
        await File.WriteAllTextAsync(
            rulesPath,
            "{ this is not valid json",
            TestContext.Current.CancellationToken);
        var clock = new FixedTimeProvider(
            new DateTimeOffset(2026, 9, 18, 1, 23, 45, TimeSpan.Zero));
        var repository = CreateRepository(clock);

        var rules = await repository.GetRulesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(rule, Assert.Single(rules));
        Assert.True(File.Exists(rulesPath));
        Assert.Equal(
            RuleRepositoryHealthState.RecoveredFromBackup,
            repository.Diagnostics.State);
        var artifact = Path.Combine(_root, "rules.corrupt-20260918T012345Z.json");
        Assert.True(File.Exists(artifact));
        Assert.Equal(
            "{ this is not valid json",
            await File.ReadAllTextAsync(artifact, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Missing_primary_is_restored_from_last_good_copy()
    {
        var rule = Rule(InputAction.Chinese, priority: 300);
        var writer = CreateRepository();
        await writer.ReplaceRulesAsync([rule], TestContext.Current.CancellationToken);
        writer.Dispose();
        File.Delete(Path.Combine(_root, "rules.json"));

        var repository = CreateRepository();
        var rules = await repository.GetRulesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(rule, Assert.Single(rules));
        Assert.True(File.Exists(Path.Combine(_root, "rules.json")));
        Assert.Equal(
            RuleRepositoryHealthState.RecoveredFromBackup,
            repository.Diagnostics.State);
    }

    [Fact]
    public async Task Corrupt_rules_file_without_backup_is_preserved_and_repository_recovers_empty()
    {
        Directory.CreateDirectory(_root);
        var rulesPath = Path.Combine(_root, "rules.json");
        await File.WriteAllTextAsync(
            rulesPath,
            "{ this is not valid json",
            TestContext.Current.CancellationToken);
        var clock = new FixedTimeProvider(
            new DateTimeOffset(2026, 9, 18, 1, 23, 45, TimeSpan.Zero));
        var repository = CreateRepository(clock);

        var rules = await repository.GetRulesAsync(TestContext.Current.CancellationToken);

        Assert.Empty(rules);
        Assert.False(File.Exists(rulesPath));
        var artifact = Path.Combine(_root, "rules.corrupt-20260918T012345Z.json");
        Assert.True(File.Exists(artifact));
        Assert.Equal(
            "{ this is not valid json",
            await File.ReadAllTextAsync(artifact, TestContext.Current.CancellationToken));
        Assert.Equal(
            RuleRepositoryHealthState.ResetAfterCorruption,
            repository.Diagnostics.State);
    }

    [Fact]
    public async Task Corrupt_primary_and_backup_fail_closed_without_deleting_evidence()
    {
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(
            Path.Combine(_root, "rules.json"),
            "bad-primary",
            TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(_root, "rules.last-good.json"),
            "bad-backup",
            TestContext.Current.CancellationToken);
        var clock = new FixedTimeProvider(
            new DateTimeOffset(2026, 9, 18, 2, 0, 0, TimeSpan.Zero));
        var repository = CreateRepository(clock);

        var rules = await repository.GetRulesAsync(TestContext.Current.CancellationToken);

        Assert.Empty(rules);
        Assert.False(File.Exists(Path.Combine(_root, "rules.json")));
        Assert.False(File.Exists(Path.Combine(_root, "rules.last-good.json")));
        Assert.True(File.Exists(Path.Combine(_root, "rules.corrupt-20260918T020000Z.json")));
        Assert.True(File.Exists(Path.Combine(_root, "rules.last-good.corrupt-20260918T020000Z.json")));
        Assert.Equal(
            RuleRepositoryHealthState.ResetAfterCorruption,
            repository.Diagnostics.State);
    }

    [Fact]
    public async Task Recovery_prunes_old_corrupt_artifacts_to_bounded_retention()
    {
        Directory.CreateDirectory(_root);
        for (var index = 0; index < 7; index++)
        {
            var path = Path.Combine(
                _root,
                $"rules.corrupt-20260917T00000{index}Z.json");
            await File.WriteAllTextAsync(
                path,
                "old",
                TestContext.Current.CancellationToken);
            File.SetLastWriteTimeUtc(path, new DateTime(2026, 9, 17, 0, 0, index, DateTimeKind.Utc));
        }

        await File.WriteAllTextAsync(
            Path.Combine(_root, "rules.json"),
            "broken",
            TestContext.Current.CancellationToken);
        var repository = CreateRepository(
            new FixedTimeProvider(
                new DateTimeOffset(2026, 9, 18, 3, 0, 0, TimeSpan.Zero)));

        _ = await repository.GetRulesAsync(TestContext.Current.CancellationToken);

        Assert.True(
            Directory.EnumerateFiles(_root, "*.corrupt-*.json").Count() <= 5);
    }

    [Fact]
    public async Task Stale_flowime_temp_files_are_cleaned_before_loading()
    {
        Directory.CreateDirectory(_root);
        var stalePrimary = Path.Combine(_root, "rules.json.123.deadbeef.tmp");
        var staleBackup = Path.Combine(_root, "rules.last-good.json.123.deadbeef.tmp");
        await File.WriteAllTextAsync(stalePrimary, "partial", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(staleBackup, "partial", TestContext.Current.CancellationToken);
        var repository = CreateRepository();

        _ = await repository.GetRulesAsync(TestContext.Current.CancellationToken);

        Assert.False(File.Exists(stalePrimary));
        Assert.False(File.Exists(staleBackup));
    }

    [Fact]
    public async Task Subsequent_reads_use_updated_in_memory_snapshot()
    {
        var repository = CreateRepository();
        var first = Rule(InputAction.Chinese, priority: 100);
        var second = Rule(InputAction.English, priority: 200);

        await repository.ReplaceRulesAsync([first], TestContext.Current.CancellationToken);
        var before = await repository.GetRulesAsync(TestContext.Current.CancellationToken);
        await repository.ReplaceRulesAsync([second], TestContext.Current.CancellationToken);
        var after = await repository.GetRulesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(first, Assert.Single(before));
        Assert.Equal(second, Assert.Single(after));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private JsonRuleRepository CreateRepository(TimeProvider? timeProvider = null) =>
        new(new AppPaths(_root), timeProvider);

    private static ApplicationRule Rule(InputAction action, int priority) =>
        new(
            Guid.NewGuid(),
            Enabled: true,
            Priority: priority,
            Match: new ApplicationMatch(ProcessPath: @"C:\Apps\Example.exe"),
            Action: action);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
