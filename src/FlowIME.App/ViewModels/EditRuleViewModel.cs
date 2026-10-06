using FlowIME.Core.Models;
using FlowIME.Core.Rules;

namespace FlowIME.App.ViewModels;

public sealed class EditRuleViewModel : InputActionSelectionViewModel
{
    private readonly IReadOnlyList<ApplicationRule> _existingRules;
    private string _selectedProviderId;
    private bool _enabled;
    private string _processName;
    private string _windowClass;
    private string _windowTitleContains;
    private int _priority;

    public EditRuleViewModel(
        RuleListItemViewModel rule,
        IReadOnlyList<InputMethodProviderDescriptor>? providers = null,
        IReadOnlyList<ApplicationRule>? existingRules = null)
        : base(
            ApplicationRuleActionOptions,
            rule is null ? throw new ArgumentNullException(nameof(rule)) : rule.Action)
    {
        Rule = rule;
        _existingRules = existingRules ?? Array.Empty<ApplicationRule>();
        _selectedProviderId = rule.Action == InputAction.StandardUsKeyboard
            ? InputMethodProviderOption.StandardUsKeyboardTargetId
            : InputMethodProviderIds.Normalize(rule.ProviderId);
        _enabled = rule.Enabled;
        _processName = rule.Match.ProcessName ?? string.Empty;
        _windowClass = rule.Match.WindowClass ?? string.Empty;
        _windowTitleContains = rule.Match.WindowTitleContains ?? string.Empty;
        _priority = rule.Priority;

        var inputMethodOptions = providers is { Count: > 0 }
            ? providers.Select(provider =>
                    new InputMethodProviderOption(provider.DisplayName, provider.Id))
                .ToArray()
            : [new InputMethodProviderOption("Microsoft Pinyin", InputMethodProviderIds.MicrosoftPinyin)];
        ProviderOptions =
        [
            .. inputMethodOptions,
            new InputMethodProviderOption(
                "标准美式键盘（US）",
                InputMethodProviderOption.StandardUsKeyboardTargetId)
        ];
    }

    public RuleListItemViewModel Rule { get; }

    public string DisplayName => Rule.DisplayName;

    public string ExecutablePath => Rule.ExecutablePath;

    public IReadOnlyList<InputMethodProviderOption> ProviderOptions { get; }

    public string SelectedProviderId
    {
        get => _selectedProviderId;
        set
        {
            var normalized = InputMethodProviderOption.IsStandardUsKeyboard(value)
                ? InputMethodProviderOption.StandardUsKeyboardTargetId
                : InputMethodProviderIds.Normalize(value);
            if (!SetProperty(ref _selectedProviderId, normalized))
            {
                return;
            }

            if (InputMethodProviderOption.IsStandardUsKeyboard(normalized))
            {
                SelectedAction = InputAction.StandardUsKeyboard;
            }
            else if (SelectedAction == InputAction.StandardUsKeyboard)
            {
                SelectedAction = InputAction.English;
            }
        }
    }

    protected override void OnSelectedActionChanged()
    {
        if (SelectedAction == InputAction.StandardUsKeyboard)
        {
            SetProperty(
                ref _selectedProviderId,
                InputMethodProviderOption.StandardUsKeyboardTargetId,
                nameof(SelectedProviderId));
        }
        else if (InputMethodProviderOption.IsStandardUsKeyboard(_selectedProviderId))
        {
            SetProperty(
                ref _selectedProviderId,
                ProviderOptions.First(option => !InputMethodProviderOption.IsStandardUsKeyboard(option.ProviderId)).ProviderId,
                nameof(SelectedProviderId));
        }
    }

    public bool Enabled
    {
        get => _enabled;
        set => SetProperty(ref _enabled, value);
    }

    public string ProcessName
    {
        get => _processName;
        set => SetMatchField(ref _processName, value);
    }

    public string WindowClass
    {
        get => _windowClass;
        set => SetMatchField(ref _windowClass, value);
    }

    public string WindowTitleContains
    {
        get => _windowTitleContains;
        set => SetMatchField(ref _windowTitleContains, value);
    }

    public int Priority
    {
        get => _priority;
        set
        {
            if (SetProperty(ref _priority, Math.Clamp(value, 0, 10000)))
            {
                RaisePreviewProperties();
            }
        }
    }

    public string MatchPreview => RuleSetAnalyzer.DescribeMatch(CreateMatch());

    public bool HasConflictPreview => GetRelationships().Count > 0;

    public string ConflictPreview
    {
        get
        {
            var relationships = GetRelationships();
            if (relationships.Any(item => item.Kind == RuleRelationshipKind.ExistingShadowsCandidate))
            {
                return "更高优先级的宽规则会覆盖此规则；重叠窗口中本规则不会生效。";
            }

            if (relationships.Any(item => item.Kind == RuleRelationshipKind.ExactMatch))
            {
                return "另一条规则使用完全相同的匹配条件。";
            }

            if (relationships.Any(item => item.Kind == RuleRelationshipKind.EqualPriorityCompetition))
            {
                return "存在同优先级且可同时匹配的规则；将按条件具体程度和规则 ID 决定。";
            }

            if (relationships.Any(item => item.Kind == RuleRelationshipKind.CandidateShadowsExisting))
            {
                return "此规则会覆盖优先级更低的现有规则。";
            }

            return relationships.Count > 0
                ? "存在可同时匹配的规则，请检查条件和优先级。"
                : "未发现与其他规则的明显冲突";
        }
    }

    public ApplicationRule CreateUpdatedRule() => new(
        Rule.Id,
        Enabled,
        Priority,
        CreateMatch(),
        SelectedAction,
        Rule.DisplayName,
        SelectedAction == InputAction.StandardUsKeyboard
            ? null
            : InputMethodProviderIds.Normalize(SelectedProviderId));

    private ApplicationMatch CreateMatch() => Rule.Match with
    {
        ProcessName = NormalizeOptional(ProcessName),
        WindowClass = NormalizeOptional(WindowClass),
        WindowTitleContains = NormalizeOptional(WindowTitleContains)
    };

    private IReadOnlyList<RuleRelationship> GetRelationships() =>
        RuleSetAnalyzer.AnalyzeCandidate(CreateUpdatedRule(), _existingRules);

    private void SetMatchField(ref string field, string? value)
    {
        if (SetProperty(ref field, value ?? string.Empty))
        {
            RaisePreviewProperties();
        }
    }

    private void RaisePreviewProperties()
    {
        OnPropertyChanged(nameof(MatchPreview));
        OnPropertyChanged(nameof(HasConflictPreview));
        OnPropertyChanged(nameof(ConflictPreview));
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
