using System.Collections.ObjectModel;
using FlowIME.Core.Models;
using FlowIME.Core.Rules;

namespace FlowIME.App.ViewModels;

public sealed class AddApplicationViewModel : InputActionSelectionViewModel
{
    private readonly List<RunningApplicationItemViewModel> _allItems;
    private readonly IReadOnlyList<ApplicationRule> _existingRules;
    private RunningApplication? _selectedApplication;
    private RunningApplicationItemViewModel? _selectedItem;
    private string _selectedProviderId;
    private string _searchQuery = string.Empty;
    private string _customDisplayName = string.Empty;
    private string _processName = string.Empty;
    private string _windowClass = string.Empty;
    private string _windowTitleContains = string.Empty;
    private int _previewPriority = 100;

    public AddApplicationViewModel(
        IReadOnlyList<RunningApplication> applications,
        IReadOnlyList<InputMethodProviderDescriptor>? providers = null,
        string? defaultProviderId = null,
        IReadOnlyList<ApplicationRule>? existingRules = null)
        : base(ApplicationRuleActionOptions, InputAction.Keep)
    {
        Applications = applications ?? throw new ArgumentNullException(nameof(applications));
        _existingRules = existingRules ?? Array.Empty<ApplicationRule>();
        _allItems = Applications
            .Select(application => new RunningApplicationItemViewModel(application))
            .ToList();
        ApplicationItems = new ObservableCollection<RunningApplicationItemViewModel>(_allItems);

        var providerOptions = providers is { Count: > 0 }
            ? providers.Select(provider =>
                    new InputMethodProviderOption(provider.DisplayName, provider.Id))
                .ToArray()
            : [new InputMethodProviderOption("Microsoft Pinyin", InputMethodProviderIds.MicrosoftPinyin)];

        ProviderOptions = providerOptions;
        var requestedDefault = InputMethodProviderIds.Normalize(defaultProviderId);
        _selectedProviderId = providerOptions.Any(option =>
                option.ProviderId.Equals(requestedDefault, StringComparison.OrdinalIgnoreCase))
            ? requestedDefault
            : providerOptions[0].ProviderId;
    }

    public IReadOnlyList<RunningApplication> Applications { get; }

    public ObservableCollection<RunningApplicationItemViewModel> ApplicationItems { get; }

    public IReadOnlyList<InputMethodProviderOption> ProviderOptions { get; }

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (SetProperty(ref _searchQuery, value ?? string.Empty))
            {
                ApplySearchFilter();
            }
        }
    }

    public string CustomDisplayName
    {
        get => _customDisplayName;
        set => SetProperty(ref _customDisplayName, value ?? string.Empty);
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

    public int PreviewPriority
    {
        get => _previewPriority;
        set
        {
            if (SetProperty(ref _previewPriority, Math.Clamp(value, 0, 10000)))
            {
                RaisePreviewProperties();
            }
        }
    }

    public string MatchPreview => SelectedApplication is null
        ? "选择应用后显示匹配条件"
        : RuleSetAnalyzer.DescribeMatch(CreateMatch(SelectedApplication));

    public bool HasConflictPreview => GetRelationships().Count > 0;

    public string ConflictPreview
    {
        get
        {
            var relationships = GetRelationships();
            if (relationships.Count == 0)
            {
                return "未发现与现有规则的明显冲突";
            }

            if (relationships.Any(item => item.Kind == RuleRelationshipKind.ExistingShadowsCandidate))
            {
                return "已有更高优先级的宽规则覆盖这些条件；当前规则在重叠窗口中不会生效。";
            }

            if (relationships.Any(item => item.Kind == RuleRelationshipKind.ExactMatch))
            {
                return "已有完全相同的匹配条件；保存时将更新现有规则。";
            }

            if (relationships.Any(item => item.Kind == RuleRelationshipKind.EqualPriorityCompetition))
            {
                return "存在同优先级且可同时匹配的规则；FlowIME 将按条件具体程度选择。";
            }

            if (relationships.Any(item => item.Kind == RuleRelationshipKind.CandidateShadowsExisting))
            {
                return "当前规则会覆盖优先级较低的现有规则。";
            }

            return "存在可同时匹配的规则，请检查优先级和条件范围。";
        }
    }

    public RunningApplicationItemViewModel? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (SetProperty(ref _selectedItem, value))
            {
                SelectedApplication = value?.Application;
            }
        }
    }

    public RunningApplication? SelectedApplication
    {
        get => _selectedApplication;
        set
        {
            if (SetProperty(ref _selectedApplication, value))
            {
                CustomDisplayName = value?.DisplayName ?? string.Empty;
                OnPropertyChanged(nameof(CanCreateRule));
                RaisePreviewProperties();
            }
        }
    }

    public string SelectedProviderId
    {
        get => _selectedProviderId;
        set => SetProperty(ref _selectedProviderId, InputMethodProviderIds.Normalize(value));
    }

    public bool CanCreateRule => SelectedApplication is not null;

    public RunningApplicationItemViewModel AddOrSelectApplication(RunningApplication application)
    {
        ArgumentNullException.ThrowIfNull(application);
        var normalizedPath = NormalizePath(application.ExecutablePath);
        var item = _allItems.FirstOrDefault(candidate =>
            string.Equals(
                NormalizePath(candidate.ExecutablePath),
                normalizedPath,
                StringComparison.OrdinalIgnoreCase));
        if (item is null)
        {
            item = new RunningApplicationItemViewModel(application);
            _allItems.Add(item);
        }

        SearchQuery = string.Empty;
        if (!ApplicationItems.Contains(item))
        {
            ApplicationItems.Add(item);
        }

        SelectedItem = item;
        return item;
    }

    public void LoadExistingRule(ApplicationRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        CustomDisplayName = rule.DisplayName ?? SelectedApplication?.DisplayName ?? string.Empty;
        SelectedAction = rule.Action;
        SelectedProviderId = InputMethodProviderIds.Normalize(rule.ProviderId);
        ProcessName = rule.Match.ProcessName ?? string.Empty;
        WindowClass = rule.Match.WindowClass ?? string.Empty;
        WindowTitleContains = rule.Match.WindowTitleContains ?? string.Empty;
        PreviewPriority = rule.Priority;
    }

    public ApplicationRule CreateRule(int priority)
    {
        var app = SelectedApplication ??
            throw new InvalidOperationException("Select an application before creating a rule.");

        return new ApplicationRule(
            Guid.NewGuid(),
            true,
            priority,
            CreateMatch(app),
            SelectedAction,
            string.IsNullOrWhiteSpace(CustomDisplayName)
                ? app.DisplayName
                : CustomDisplayName.Trim(),
            InputMethodProviderIds.Normalize(SelectedProviderId));
    }

    private ApplicationMatch CreateMatch(RunningApplication app) => new(
        ProcessPath: app.ExecutablePath,
        ProcessName: NormalizeOptional(ProcessName),
        WindowTitleContains: NormalizeOptional(WindowTitleContains),
        WindowClass: NormalizeOptional(WindowClass),
        PackageFamilyName: app.PackageFamilyName,
        ApplicationUserModelId: app.ApplicationUserModelId);

    private IReadOnlyList<RuleRelationship> GetRelationships()
    {
        if (SelectedApplication is null)
        {
            return Array.Empty<RuleRelationship>();
        }

        var candidate = new ApplicationRule(
            Guid.Empty,
            true,
            PreviewPriority,
            CreateMatch(SelectedApplication),
            SelectedAction,
            CustomDisplayName,
            SelectedProviderId);
        return RuleSetAnalyzer.AnalyzeCandidate(candidate, _existingRules);
    }

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

    private void ApplySearchFilter()
    {
        var query = SearchQuery.Trim();
        var matches = string.IsNullOrEmpty(query)
            ? _allItems
            : _allItems.Where(item =>
                    item.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    item.ProcessName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    Path.GetFileName(item.ExecutablePath).Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    item.ExecutablePath.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();

        ApplicationItems.Clear();
        foreach (var item in matches)
        {
            ApplicationItems.Add(item);
        }

        if (SelectedItem is not null && !ApplicationItems.Contains(SelectedItem))
        {
            SelectedItem = null;
        }
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path;
        }
    }
}

public sealed record InputMethodProviderOption(string Label, string ProviderId);
