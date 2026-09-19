using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FlowIME.Core.Abstractions;
using FlowIME.Core.Models;
using FlowIME.Core.Rules;

namespace FlowIME.App.ViewModels;

public sealed class RulesViewModel : ObservableObject
{
    private readonly IRuleRepository _repository;
    private readonly IReadOnlyDictionary<string, string> _providerNames;
    private IReadOnlyList<ApplicationRule> _sourceRules = Array.Empty<ApplicationRule>();
    private GlobalDefaultTarget? _globalDefault;
    private RuleSetDiagnostics _ruleDiagnostics = RuleSetDiagnostics.Empty;

    public RulesViewModel(
        IRuleRepository repository,
        IReadOnlyList<InputMethodProviderDescriptor>? providers = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _providerNames = (providers ?? Array.Empty<InputMethodProviderDescriptor>())
            .ToDictionary(
                provider => provider.Id,
                provider => provider.DisplayName,
                StringComparer.OrdinalIgnoreCase);
    }

    public ObservableCollection<RuleListItemViewModel> Rules { get; } = [];

    public IReadOnlyList<ApplicationRule> SourceRules => _sourceRules;

    public bool IsEmpty => Rules.Count == 0;

    public int RuleCount => Rules.Count;

    public int EnabledRuleCount => Rules.Count(rule => rule.Enabled);

    public string RuleCountSummary => RuleCount == 0
        ? "暂无应用专属规则"
        : $"{RuleCount} 条规则 · {EnabledRuleCount} 条启用";

    public GlobalDefaultTarget? GlobalDefault => _globalDefault;

    public bool HasGlobalDefault => _globalDefault is not null;

    public string GlobalDefaultStatusLabel => HasGlobalDefault ? "已启用" : "未启用";

    public bool HasRuleDiagnostics => _ruleDiagnostics.HasIssues;

    public int RuleConflictGroupCount => _ruleDiagnostics.ConflictingTargetGroupCount;

    public int RuleRedundantGroupCount => _ruleDiagnostics.RedundantTargetGroupCount;

    public int RuleShadowedPairCount => _ruleDiagnostics.ShadowedRulePairCount;

    public int RuleCompetitionPairCount => _ruleDiagnostics.EqualPriorityCompetitionPairCount;

    public string RuleDiagnosticsSummary
    {
        get
        {
            if (!_ruleDiagnostics.HasIssues)
            {
                return "未发现重复、覆盖或竞争规则";
            }

            if (_ruleDiagnostics.ShadowedRulePairCount > 0)
            {
                return $"检测到 {_ruleDiagnostics.ShadowedRulePairCount} 组优先级覆盖；" +
                    "较低优先级规则可能在重叠窗口中不会生效。";
            }

            if (_ruleDiagnostics.EqualPriorityCompetitionPairCount > 0)
            {
                return $"检测到 {_ruleDiagnostics.EqualPriorityCompetitionPairCount} 组同优先级竞争；" +
                    "将按条件具体程度和规则 ID 稳定选择。";
            }

            if (_ruleDiagnostics.ConflictingTargetGroupCount > 0 &&
                _ruleDiagnostics.RedundantTargetGroupCount > 0)
            {
                return $"检测到 {_ruleDiagnostics.ConflictingTargetGroupCount} 组冲突规则和 " +
                    $"{_ruleDiagnostics.RedundantTargetGroupCount} 组重复规则；相同匹配条件会按优先级决定结果。";
            }

            if (_ruleDiagnostics.ConflictingTargetGroupCount > 0)
            {
                return $"检测到 {_ruleDiagnostics.ConflictingTargetGroupCount} 组冲突规则；" +
                    "相同匹配条件指向不同目标，会按优先级决定结果。";
            }

            return $"检测到 {_ruleDiagnostics.RedundantTargetGroupCount} 组重复规则；" +
                "建议删除冗余项以保持配置清晰。";
        }
    }

    public string GlobalDefaultSummary
    {
        get
        {
            if (_globalDefault is null)
            {
                return "未设置 · 未匹配应用保持当前输入状态";
            }

            var providerId = InputMethodProviderIds.Normalize(_globalDefault.ProviderId);
            var mode = _globalDefault.Action == InputAction.Chinese ? "中文" : "英文";
            return $"{GetProviderDisplayName(providerId)} · {mode}";
        }
    }

    public async ValueTask LoadAsync(CancellationToken cancellationToken = default)
    {
        var configuration = await _repository.GetConfigurationAsync(cancellationToken);
        _sourceRules = configuration.Rules;
        _globalDefault = configuration.GlobalDefault;
        ReplaceProjection(_sourceRules);
        RaiseGlobalDefaultProperties();
    }

    public ApplicationRule? FindByExecutablePath(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return null;
        }

        return _sourceRules.FirstOrDefault(rule =>
            !string.IsNullOrWhiteSpace(rule.Match.ProcessPath) &&
            string.Equals(
                rule.Match.ProcessPath,
                executablePath,
                StringComparison.OrdinalIgnoreCase));
    }

    public ApplicationRule? FindByApplication(RunningApplication application)
    {
        ArgumentNullException.ThrowIfNull(application);
        return _sourceRules.FirstOrDefault(rule => MatchesApplication(rule, application));
    }

    public ApplicationRule? FindEquivalentRule(ApplicationRule incoming)
    {
        ArgumentNullException.ThrowIfNull(incoming);
        return _sourceRules.FirstOrDefault(rule =>
            MatchesEquivalentRule(rule, incoming));
    }

    public async ValueTask AddRuleAsync(
        ApplicationRule rule,
        CancellationToken cancellationToken = default)
    {
        _ = await UpsertRuleAsync(rule, cancellationToken);
    }

    public async ValueTask<RuleUpsertResult> UpsertRuleAsync(
        ApplicationRule rule,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rule);
        rule = rule with
        {
            ProviderId = InputMethodProviderIds.Normalize(rule.ProviderId)
        };

        var current = await _repository.GetRulesAsync(cancellationToken);
        var existing = current.FirstOrDefault(candidate =>
            MatchesEquivalentRule(candidate, rule));
        ApplicationRule savedRule;
        IReadOnlyList<ApplicationRule> next;

        if (existing is null)
        {
            savedRule = rule;
            next = current.Append(savedRule).ToArray();
        }
        else
        {
            savedRule = rule with
            {
                Id = existing.Id,
                Priority = existing.Priority
            };
            next = current
                .Select(candidate => candidate.Id == existing.Id ? savedRule : candidate)
                .ToArray();
        }

        await PersistAndRefreshAsync(next, cancellationToken);
        return new RuleUpsertResult(savedRule, existing is not null);
    }

    public async ValueTask UpdateRuleAsync(
        Guid id,
        InputAction action,
        bool enabled,
        string? providerId = null,
        CancellationToken cancellationToken = default)
    {
        var current = await _repository.GetRulesAsync(cancellationToken);
        var found = false;
        var next = current.Select(rule =>
        {
            if (rule.Id != id)
            {
                return rule;
            }

            found = true;
            return rule with
            {
                Action = action,
                Enabled = enabled,
                ProviderId = providerId is null
                    ? InputMethodProviderIds.Normalize(rule.ProviderId)
                    : InputMethodProviderIds.Normalize(providerId)
            };
        }).ToArray();

        if (!found)
        {
            throw new InvalidOperationException($"Rule {id} no longer exists.");
        }

        await PersistAndRefreshAsync(next, cancellationToken);
    }

    public async ValueTask UpdateRuleAsync(
        ApplicationRule updatedRule,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(updatedRule);
        var current = await _repository.GetRulesAsync(cancellationToken);
        var found = false;
        var next = current.Select(rule =>
        {
            if (rule.Id != updatedRule.Id)
            {
                return rule;
            }

            found = true;
            return updatedRule with
            {
                ProviderId = InputMethodProviderIds.Normalize(updatedRule.ProviderId)
            };
        }).ToArray();

        if (!found)
        {
            throw new InvalidOperationException($"Rule {updatedRule.Id} no longer exists.");
        }

        await PersistAndRefreshAsync(next, cancellationToken);
    }

    public async ValueTask DeleteRuleAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var current = await _repository.GetRulesAsync(cancellationToken);
        var next = current.Where(rule => rule.Id != id).ToArray();
        if (next.Length == current.Count)
        {
            return;
        }

        await PersistAndRefreshAsync(next, cancellationToken);
    }

    public async ValueTask UpdateGlobalDefaultAsync(
        GlobalDefaultTarget? target,
        CancellationToken cancellationToken = default)
    {
        await _repository.ReplaceGlobalDefaultAsync(target, cancellationToken);
        _globalDefault = target?.Normalize();
        RaiseGlobalDefaultProperties();
    }

    public int GetNextPriority() =>
        _sourceRules.Count == 0 ? 100 : _sourceRules.Max(rule => rule.Priority) + 100;

    private async ValueTask PersistAndRefreshAsync(
        IReadOnlyList<ApplicationRule> rules,
        CancellationToken cancellationToken)
    {
        await _repository.ReplaceRulesAsync(rules, cancellationToken);
        _sourceRules = rules.ToArray();
        ReplaceProjection(_sourceRules);
    }

    private void ReplaceProjection(IReadOnlyList<ApplicationRule> rules)
    {
        _ruleDiagnostics = RuleSetAnalyzer.Analyze(rules);
        Rules.Clear();
        foreach (var rule in rules
                     .OrderByDescending(rule => rule.Priority)
                     .ThenBy(rule => rule.Match.ProcessPath, StringComparer.OrdinalIgnoreCase))
        {
            var path = rule.Match.ProcessPath ?? string.Empty;
            var fallbackName = GetRuleFallbackName(rule.Match);

            var providerId = InputMethodProviderIds.Normalize(rule.ProviderId);
            Rules.Add(new RuleListItemViewModel(
                rule.Id,
                string.IsNullOrWhiteSpace(rule.DisplayName)
                    ? fallbackName
                    : rule.DisplayName,
                path,
                providerId,
                GetProviderDisplayName(providerId),
                rule.Action,
                rule.Enabled,
                rule.Priority,
                rule.Match));
        }

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(RuleCount));
        OnPropertyChanged(nameof(EnabledRuleCount));
        OnPropertyChanged(nameof(RuleCountSummary));
        OnPropertyChanged(nameof(HasRuleDiagnostics));
        OnPropertyChanged(nameof(RuleConflictGroupCount));
        OnPropertyChanged(nameof(RuleRedundantGroupCount));
        OnPropertyChanged(nameof(RuleShadowedPairCount));
        OnPropertyChanged(nameof(RuleCompetitionPairCount));
        OnPropertyChanged(nameof(RuleDiagnosticsSummary));
    }

    private void RaiseGlobalDefaultProperties()
    {
        OnPropertyChanged(nameof(GlobalDefault));
        OnPropertyChanged(nameof(HasGlobalDefault));
        OnPropertyChanged(nameof(GlobalDefaultStatusLabel));
        OnPropertyChanged(nameof(GlobalDefaultSummary));
    }

    private string GetProviderDisplayName(string providerId) =>
        _providerNames.TryGetValue(providerId, out var displayName)
            ? displayName
            : providerId;

    private static string GetRuleFallbackName(ApplicationMatch match)
    {
        if (!string.IsNullOrWhiteSpace(match.ProcessPath))
        {
            return Path.GetFileNameWithoutExtension(match.ProcessPath);
        }

        return match.ProcessName ??
               match.ApplicationUserModelId ??
               match.PackageFamilyName ??
               match.WindowTitleContains ??
               match.WindowClass ??
               "高级匹配规则";
    }

    private static bool MatchesEquivalentRule(
        ApplicationRule existing,
        ApplicationRule incoming) =>
        MatchesSameExecutable(existing, incoming) &&
        OptionalEquals(existing.Match.ProcessName, incoming.Match.ProcessName) &&
        OptionalEquals(existing.Match.WindowClass, incoming.Match.WindowClass) &&
        OptionalEquals(existing.Match.WindowTitleContains, incoming.Match.WindowTitleContains);

    private static bool OptionalEquals(string? left, string? right) =>
        string.Equals(
            string.IsNullOrWhiteSpace(left) ? null : left.Trim(),
            string.IsNullOrWhiteSpace(right) ? null : right.Trim(),
            StringComparison.OrdinalIgnoreCase);

    private static bool MatchesSameExecutable(
        ApplicationRule existing,
        ApplicationRule incoming)
    {
        if (PathEquals(existing.Match.ProcessPath, incoming.Match.ProcessPath))
        {
            return true;
        }

        if (StableIdentityEquals(
                existing.Match.ApplicationUserModelId,
                incoming.Match.ApplicationUserModelId))
        {
            return true;
        }

        return SamePackagedExecutable(
            existing.Match.PackageFamilyName,
            existing.Match.ProcessPath,
            incoming.Match.PackageFamilyName,
            incoming.Match.ProcessPath);
    }

    private static bool MatchesApplication(
        ApplicationRule rule,
        RunningApplication application)
    {
        if (PathEquals(rule.Match.ProcessPath, application.ExecutablePath))
        {
            return true;
        }

        if (StableIdentityEquals(
                rule.Match.ApplicationUserModelId,
                application.ApplicationUserModelId))
        {
            return true;
        }

        return SamePackagedExecutable(
            rule.Match.PackageFamilyName,
            rule.Match.ProcessPath,
            application.PackageFamilyName,
            application.ExecutablePath);
    }

    private static bool SamePackagedExecutable(
        string? leftPackageFamilyName,
        string? leftPath,
        string? rightPackageFamilyName,
        string? rightPath)
    {
        if (string.IsNullOrWhiteSpace(leftPackageFamilyName) ||
            string.IsNullOrWhiteSpace(rightPackageFamilyName) ||
            !leftPackageFamilyName.Equals(
                rightPackageFamilyName,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var leftName = string.IsNullOrWhiteSpace(leftPath)
            ? null
            : Path.GetFileName(leftPath);
        var rightName = string.IsNullOrWhiteSpace(rightPath)
            ? null
            : Path.GetFileName(rightPath);

        return !string.IsNullOrWhiteSpace(leftName) &&
               !string.IsNullOrWhiteSpace(rightName) &&
               leftName.Equals(rightName, StringComparison.OrdinalIgnoreCase);
    }


    private static bool StableIdentityEquals(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left) &&
        !string.IsNullOrWhiteSpace(right) &&
        left.Equals(right, StringComparison.OrdinalIgnoreCase);

    private static bool PathEquals(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left) &&
        !string.IsNullOrWhiteSpace(right) &&
        left.Equals(right, StringComparison.OrdinalIgnoreCase);
}

public sealed record RuleUpsertResult(
    ApplicationRule Rule,
    bool ReplacedExisting);
