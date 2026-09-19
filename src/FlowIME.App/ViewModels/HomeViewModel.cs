using CommunityToolkit.Mvvm.ComponentModel;
using FlowIME.Core.Context;
using FlowIME.Core.Models;
using FlowIME.Core.Rules;

namespace FlowIME.App.ViewModels;

public sealed class HomeViewModel : ObservableObject
{
    private bool _isAutomationEnabled = true;
    private string _currentApplicationName = "等待前台应用";
    private string _currentProcessName = "—";
    private string _currentInputProfile = "等待检测";
    private string _currentInputModeLabel = "未知";
    private string _currentInputHeadline = "等待检测";
    private string _currentInputDetail = "正在读取输入状态";
    private string _ruleSourceLabel = "等待解析";
    private string _ruleTargetLabel = "—";
    private string _lastRuleSummary = "尚未检测到前台规则";
    private string _ruleMatchReason = "等待前台窗口信息";
    private string _currentContextLabel = "普通应用";
    private string _currentContextDescription = "等待场景识别";
    private string _currentRuleActionLabel = "为此应用设置";
    private bool _canCreateRuleForCurrentApplication;

    public bool IsAutomationEnabled
    {
        get => _isAutomationEnabled;
        set
        {
            if (SetProperty(ref _isAutomationEnabled, value))
            {
                OnPropertyChanged(nameof(AutomationStateLabel));
            }
        }
    }

    public string AutomationStateLabel =>
        IsAutomationEnabled ? "正在运行" : "已暂停";

    public string CurrentApplicationName
    {
        get => _currentApplicationName;
        private set => SetProperty(ref _currentApplicationName, value);
    }

    public string CurrentProcessName
    {
        get => _currentProcessName;
        private set => SetProperty(ref _currentProcessName, value);
    }

    public string CurrentInputProfile
    {
        get => _currentInputProfile;
        private set => SetProperty(ref _currentInputProfile, value);
    }

    public string CurrentInputModeLabel
    {
        get => _currentInputModeLabel;
        private set => SetProperty(ref _currentInputModeLabel, value);
    }

    public string CurrentInputHeadline
    {
        get => _currentInputHeadline;
        private set => SetProperty(ref _currentInputHeadline, value);
    }

    public string CurrentInputDetail
    {
        get => _currentInputDetail;
        private set => SetProperty(ref _currentInputDetail, value);
    }

    public string RuleSourceLabel
    {
        get => _ruleSourceLabel;
        private set => SetProperty(ref _ruleSourceLabel, value);
    }

    public string RuleTargetLabel
    {
        get => _ruleTargetLabel;
        private set => SetProperty(ref _ruleTargetLabel, value);
    }


    public string CurrentContextLabel
    {
        get => _currentContextLabel;
        private set => SetProperty(ref _currentContextLabel, value);
    }

    public string CurrentContextDescription
    {
        get => _currentContextDescription;
        private set => SetProperty(ref _currentContextDescription, value);
    }

    public string CurrentRuleActionLabel
    {
        get => _currentRuleActionLabel;
        private set => SetProperty(ref _currentRuleActionLabel, value);
    }

    public bool CanCreateRuleForCurrentApplication
    {
        get => _canCreateRuleForCurrentApplication;
        private set => SetProperty(ref _canCreateRuleForCurrentApplication, value);
    }

    public string LastRuleSummary
    {
        get => _lastRuleSummary;
        private set => SetProperty(ref _lastRuleSummary, value);
    }

    public string RuleMatchReason
    {
        get => _ruleMatchReason;
        private set => SetProperty(ref _ruleMatchReason, value);
    }

    public void UpdateCurrentState(
        WindowContext window,
        InputState input,
        InputAction? matchedAction,
        ApplicationRule? matchedRule = null,
        string? matchedProviderDisplayName = null,
        RuleResolutionSource resolutionSource = RuleResolutionSource.None,
        InputContextSnapshot? context = null)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(input);

        CurrentApplicationName = !string.IsNullOrWhiteSpace(matchedRule?.DisplayName)
            ? matchedRule.DisplayName!
            : window.ProcessName;
        CanCreateRuleForCurrentApplication = !string.IsNullOrWhiteSpace(window.ExecutablePath);
        CurrentRuleActionLabel = matchedRule is null ? "为此应用设置" : "编辑此应用规则";
        CurrentProcessName = GetExecutableName(window);
        CurrentInputProfile = string.IsNullOrWhiteSpace(input.ProfileName)
            ? "未知输入法"
            : input.ProfileName;
        CurrentInputModeLabel = input.Mode switch
        {
            InputMode.Chinese => "中文",
            InputMode.English => "英文",
            _ => "未知"
        };
        if (input.Mode == InputMode.Unknown)
        {
            CurrentInputHeadline = CurrentInputProfile == "未知输入法"
                ? "输入状态暂不可读"
                : CurrentInputProfile;
            CurrentInputDetail = CurrentInputProfile == "未知输入法"
                ? "等待检测"
                : "模式暂不可读";
        }
        else
        {
            CurrentInputHeadline = CurrentInputModeLabel;
            CurrentInputDetail = CurrentInputProfile;
        }

        UpdateContext(context);

        var targetProvider = string.IsNullOrWhiteSpace(matchedProviderDisplayName)
            ? CurrentInputProfile
            : matchedProviderDisplayName;

        var effectiveResolutionSource = resolutionSource != RuleResolutionSource.None
            ? resolutionSource
            : matchedRule is not null
                ? RuleResolutionSource.ApplicationRule
                : RuleResolutionSource.None;

        RuleSourceLabel = effectiveResolutionSource switch
        {
            RuleResolutionSource.ContextPolicy => "场景规则",
            RuleResolutionSource.ApplicationRule => "应用专属规则",
            RuleResolutionSource.GlobalDefault => "全局默认",
            _ => "未匹配"
        };

        RuleTargetLabel = matchedAction switch
        {
            InputAction.Chinese => $"{targetProvider} · 中文",
            InputAction.English => $"{targetProvider} · 英文",
            InputAction.Keep when effectiveResolutionSource is
                RuleResolutionSource.ContextPolicy or RuleResolutionSource.ApplicationRule =>
                "保持当前输入状态",
            _ => "不干预"
        };

        LastRuleSummary = effectiveResolutionSource switch
        {
            RuleResolutionSource.ContextPolicy when matchedAction == InputAction.Chinese =>
                $"当前场景 → {targetProvider} · 中文",
            RuleResolutionSource.ContextPolicy when matchedAction == InputAction.English =>
                $"当前场景 → {targetProvider} · 英文",
            RuleResolutionSource.ContextPolicy when matchedAction == InputAction.Keep =>
                "当前场景 → 保持当前输入状态",
            RuleResolutionSource.GlobalDefault when matchedAction == InputAction.Chinese =>
                $"全局默认 → {targetProvider} · 中文",
            RuleResolutionSource.GlobalDefault when matchedAction == InputAction.English =>
                $"全局默认 → {targetProvider} · 英文",
            RuleResolutionSource.ApplicationRule when matchedAction == InputAction.Chinese =>
                $"{CurrentApplicationName} → {targetProvider} · 中文",
            RuleResolutionSource.ApplicationRule when matchedAction == InputAction.English =>
                $"{CurrentApplicationName} → {targetProvider} · 英文",
            RuleResolutionSource.ApplicationRule when matchedAction == InputAction.Keep =>
                $"{CurrentApplicationName} → 保持当前输入状态",
            _ when matchedAction == InputAction.Chinese =>
                $"{CurrentApplicationName} → {targetProvider} · 中文",
            _ when matchedAction == InputAction.English =>
                $"{CurrentApplicationName} → {targetProvider} · 英文",
            _ when matchedAction == InputAction.Keep =>
                $"{CurrentApplicationName} → 保持当前输入状态",
            _ => "当前未匹配规则"
        };

        RuleMatchReason = DescribeMatchReason(window, matchedRule, effectiveResolutionSource);
    }

    private static string DescribeMatchReason(
        WindowContext window,
        ApplicationRule? rule,
        RuleResolutionSource source)
    {
        if (rule is null)
        {
            return source switch
            {
                RuleResolutionSource.GlobalDefault => "没有应用专属规则命中，因此使用全局默认。",
                RuleResolutionSource.ContextPolicy => "当前窗口命中了场景策略。",
                _ => "当前窗口没有命中启用的应用规则。"
            };
        }

        var conditions = new List<string>();
        if (!string.IsNullOrWhiteSpace(rule.Match.ProcessPath))
        {
            conditions.Add($"可执行文件 {window.ExecutablePath ?? "未知"}");
        }

        if (!string.IsNullOrWhiteSpace(rule.Match.ProcessName))
        {
            conditions.Add($"进程名 {window.ProcessName}");
        }

        if (!string.IsNullOrWhiteSpace(rule.Match.WindowClass))
        {
            conditions.Add($"窗口类 {window.WindowClass ?? "未知"}");
        }

        if (!string.IsNullOrWhiteSpace(rule.Match.WindowTitleContains))
        {
            conditions.Add($"标题包含 {rule.Match.WindowTitleContains}");
        }

        if (!string.IsNullOrWhiteSpace(rule.Match.PackageFamilyName))
        {
            conditions.Add($"包标识 {rule.Match.PackageFamilyName}");
        }

        if (!string.IsNullOrWhiteSpace(rule.Match.ApplicationUserModelId))
        {
            conditions.Add($"AUMID {rule.Match.ApplicationUserModelId}");
        }

        return conditions.Count == 0
            ? $"规则 {rule.DisplayName ?? rule.Id.ToString()} 没有有效匹配条件。"
            : $"命中条件：{string.Join("；", conditions)}。优先级 {rule.Priority}。";
    }

    private void UpdateContext(InputContextSnapshot? context)
    {
        if (context is null)
        {
            CurrentContextLabel = "普通应用";
            CurrentContextDescription = "FlowIME 正在按应用规则处理输入状态";
            return;
        }

        if (context.HasSignal(InputContextSignalKind.GameTextEntry))
        {
            CurrentContextLabel = "游戏 · 文字输入";
            CurrentContextDescription = "已进入游戏文字输入场景，游戏保护会暂时让路";
            return;
        }

        if (context.HasSignal(InputContextSignalKind.Game))
        {
            CurrentContextLabel = "游戏模式";
            CurrentContextDescription = "已识别游戏场景，正在应用游戏保护";
            return;
        }

        if (context.HasSignal(InputContextSignalKind.Fullscreen))
        {
            CurrentContextLabel = "全屏应用";
            CurrentContextDescription = "检测到全屏，但未将它视为游戏";
            return;
        }

        CurrentContextLabel = "普通应用";
        CurrentContextDescription = "按当前应用规则或全局默认处理";
    }

    private static string GetExecutableName(WindowContext window)
    {
        if (!string.IsNullOrWhiteSpace(window.ExecutablePath))
        {
            return Path.GetFileName(window.ExecutablePath);
        }

        return window.ProcessName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? window.ProcessName
            : window.ProcessName + ".exe";
    }
}
