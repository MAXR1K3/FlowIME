using CommunityToolkit.Mvvm.ComponentModel;
using FlowIME.Core.Models;
using FlowIME.Core.Rules;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace FlowIME.App.ViewModels;

public sealed class RuleListItemViewModel : ObservableObject
{
    private ImageSource? _icon;

    public RuleListItemViewModel(
        Guid id,
        string displayName,
        string executablePath,
        string providerId,
        string providerLabel,
        InputAction action,
        bool enabled,
        int priority = 100,
        ApplicationMatch? match = null)
    {
        Id = id;
        DisplayName = displayName;
        ExecutablePath = executablePath;
        ProviderId = providerId;
        ProviderLabel = providerLabel;
        Action = action;
        Enabled = enabled;
        Priority = priority;
        Match = match ?? new ApplicationMatch(ProcessPath: executablePath);
    }

    public Guid Id { get; }

    public string DisplayName { get; }

    public string ExecutablePath { get; }

    public string ExecutableName => !string.IsNullOrWhiteSpace(ExecutablePath)
        ? Path.GetFileName(ExecutablePath)
        : Match.ProcessName ??
          Match.ApplicationUserModelId ??
          Match.PackageFamilyName ??
          Match.WindowTitleContains ??
          Match.WindowClass ??
          "高级匹配规则";

    public string ProviderId { get; }

    public string ProviderLabel { get; }

    public InputAction Action { get; }

    public bool Enabled { get; }

    public int Priority { get; }

    public ApplicationMatch Match { get; }

    public string MatchSummary => RuleSetAnalyzer.DescribeMatch(Match);

    public ImageSource? Icon
    {
        get => _icon;
        set
        {
            if (!SetProperty(ref _icon, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IconVisibility));
            OnPropertyChanged(nameof(FallbackIconVisibility));
        }
    }

    public Visibility IconVisibility => Icon is null
        ? Visibility.Collapsed
        : Visibility.Visible;

    public Visibility FallbackIconVisibility => Icon is null
        ? Visibility.Visible
        : Visibility.Collapsed;

    public string ActionLabel => Action switch
    {
        InputAction.Chinese => "中文",
        InputAction.English => "英文",
        InputAction.Keep => "保持",
        _ => Action.ToString()
    };

    public string TargetLabel => Action == InputAction.Keep
        ? "保持当前输入状态"
        : $"{ProviderLabel} · {ActionLabel}";
}
