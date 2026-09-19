using System.Diagnostics;
using FlowIME.App.Services;
using FlowIME.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FlowIME.App.Views;

public sealed partial class RulesPage : Page
{
    private readonly AppServices _services;
    private readonly RulesViewModel _viewModel;
    private readonly DispatcherTimer _feedbackTimer;
    private readonly ApplicationIconLoader _iconLoader = new();
    private bool _loaded;

    public RulesPage()
    {
        InitializeComponent();
        _services = ((App)Application.Current).Services;
        _viewModel = new RulesViewModel(_services.RuleRepository, _services.InputMethodProviders);
        DataContext = _viewModel;
        _viewModel.Rules.CollectionChanged += (_, _) =>
        {
            ApplySearchFilter();
            _ = LoadRuleIconsAsync();
        };

        _feedbackTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(4)
        };
        _feedbackTimer.Tick += (_, _) =>
        {
            _feedbackTimer.Stop();
            RuleFeedbackBar.IsOpen = false;
        };
    }

    private void RuleCard_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement card) return;
        foreach (var group in VisualStateManager.GetVisualStateGroups(card))
        foreach (var state in group.States)
        foreach (var trigger in state.StateTriggers.OfType<FlowIME.App.Layout.ContentWidthTrigger>())
            trigger.TargetElement = card;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        try
        {
            await _viewModel.LoadAsync();
            ApplySearchFilter();
            await LoadRuleIconsAsync();
        }
        catch (Exception ex)
        {
            ShowFailure("无法读取规则", ex);
        }
    }

    private async void EditGlobalDefault_Click(object sender, RoutedEventArgs e)
    {
        var editor = new GlobalDefaultViewModel(
            _viewModel.GlobalDefault,
            _services.InputMethodProviders,
            _services.DefaultInputMethodProviderId);
        var dialog = new GlobalDefaultDialog(editor)
        {
            XamlRoot = XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        try
        {
            await _viewModel.UpdateGlobalDefaultAsync(editor.CreateTarget());
            await _services.RefreshAfterRuleChangeAsync();
            ShowFeedback(
                InfoBarSeverity.Success,
                "全局默认已更新",
                _viewModel.GlobalDefaultSummary);
        }
        catch (Exception ex)
        {
            ShowFailure("无法更新全局默认", ex);
        }
    }

    private async void AddApplication_Click(object sender, RoutedEventArgs e)
    {
        IReadOnlyList<FlowIME.Core.Models.RunningApplication> applications;
        try
        {
            applications = await _services.RunningApplications.GetRunningApplicationsAsync();
        }
        catch (Exception ex)
        {
            ShowFailure("无法读取应用或进程", ex);
            return;
        }

        var picker = new AddApplicationViewModel(
            applications,
            _services.InputMethodProviders,
            _services.DefaultInputMethodProviderId,
            _viewModel.SourceRules)
        {
            PreviewPriority = _viewModel.GetNextPriority()
        };
        picker.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != nameof(AddApplicationViewModel.SelectedApplication))
            {
                return;
            }

            var existingRule = picker.SelectedApplication is { } selected
                ? _viewModel.FindByApplication(selected)
                : null;
            if (existingRule is not null)
            {
                picker.LoadExistingRule(existingRule);
                return;
            }

            picker.SelectedAction = FlowIME.Core.Models.InputAction.Keep;
            picker.SelectedProviderId = _services.DefaultInputMethodProviderId;
            picker.ProcessName = string.Empty;
            picker.WindowClass = string.Empty;
            picker.WindowTitleContains = string.Empty;
            picker.PreviewPriority = _viewModel.GetNextPriority();
        };
        var dialog = new AddApplicationDialog(picker)
        {
            XamlRoot = XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary || !picker.CanCreateRule)
        {
            return;
        }

        var rule = picker.CreateRule(picker.PreviewPriority);
        if (rule.Match.ProcessPath is { Length: > 0 } path &&
            _viewModel.FindEquivalentRule(rule) is { } existing)
        {
            var confirm = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "更新现有规则？",
                Content = $"{existing.DisplayName ?? Path.GetFileNameWithoutExtension(path)} 已有规则。继续会更新原规则，而不会创建重复项。",
                PrimaryButtonText = "更新",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary
            };

            if (await confirm.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }
        }

        try
        {
            var saved = await _viewModel.UpsertRuleAsync(rule);
            await _services.RefreshAfterRuleChangeAsync();
            SearchBox.Text = string.Empty;
            ApplySearchFilter();
            ShowFeedback(
                InfoBarSeverity.Success,
                saved.ReplacedExisting ? "规则已更新" : "规则已添加",
                $"{saved.Rule.DisplayName ?? Path.GetFileNameWithoutExtension(saved.Rule.Match.ProcessPath)} 的自动切换设置已保存。");
        }
        catch (Exception ex)
        {
            ShowFailure("无法保存应用规则", ex);
        }
    }

    private async void RuleEnabled_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleSwitch toggle ||
            toggle.DataContext is not RuleListItemViewModel item ||
            toggle.IsOn == item.Enabled)
        {
            return;
        }

        try
        {
            await _viewModel.UpdateRuleAsync(
                item.Id,
                item.Action,
                toggle.IsOn,
                item.ProviderId);
            await _services.RefreshAfterRuleChangeAsync();
            ApplySearchFilter();
            ShowFeedback(
                InfoBarSeverity.Success,
                toggle.IsOn ? "规则已启用" : "规则已停用",
                item.DisplayName);
        }
        catch (Exception ex)
        {
            toggle.IsOn = item.Enabled;
            ShowFailure("无法更新规则状态", ex);
        }
    }

    private async void EditRule_Click(object sender, RoutedEventArgs e)
    {
        if (!TryResolveRuleItem(sender, out var item))
        {
            return;
        }

        await EditRuleAsync(item);
    }

    private async void RulesList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is RuleListItemViewModel item)
        {
            await EditRuleAsync(item);
        }
    }

    private async Task EditRuleAsync(RuleListItemViewModel item)
    {
        var editor = new EditRuleViewModel(
            item,
            _services.InputMethodProviders,
            _viewModel.SourceRules);
        var dialog = new EditRuleDialog(editor)
        {
            XamlRoot = XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        try
        {
            await _viewModel.UpdateRuleAsync(editor.CreateUpdatedRule());
            await _services.RefreshAfterRuleChangeAsync();
            ApplySearchFilter();
            ShowFeedback(
                InfoBarSeverity.Success,
                "规则已保存",
                $"{item.DisplayName} 的输入法与输入状态已更新。");
        }
        catch (Exception ex)
        {
            ShowFailure("无法保存规则", ex);
        }
    }

    private async void DeleteRule_Click(object sender, RoutedEventArgs e)
    {
        if (!TryResolveRuleItem(sender, out var item))
        {
            return;
        }

        var confirm = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "删除规则？",
            Content = $"删除 {item.DisplayName} 的自动切换规则。",
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close
        };

        if (await confirm.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        try
        {
            await _viewModel.DeleteRuleAsync(item.Id);
            await _services.RefreshAfterRuleChangeAsync();
            ApplySearchFilter();
            ShowFeedback(
                InfoBarSeverity.Success,
                "规则已删除",
                item.DisplayName);
        }
        catch (Exception ex)
        {
            ShowFailure("无法删除规则", ex);
        }
    }

    private void SearchBox_TextChanged(
        AutoSuggestBox sender,
        AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            ApplySearchFilter();
        }
    }

    private void ApplySearchFilter()
    {
        var query = SearchBox.Text.Trim();
        IReadOnlyList<RuleListItemViewModel> visibleRules = string.IsNullOrWhiteSpace(query)
            ? _viewModel.Rules.ToArray()
            : _viewModel.Rules
                .Where(item =>
                    item.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    item.ExecutablePath.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    item.TargetLabel.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToArray();

        RulesList.ItemsSource = visibleRules;
        UpdateListStates(visibleRules.Count, !string.IsNullOrWhiteSpace(query));
    }

    private void UpdateListStates(int visibleRuleCount, bool hasSearchQuery)
    {
        var hasAnyRules = _viewModel.Rules.Count > 0;
        var hasVisibleRules = visibleRuleCount > 0;

        RulesList.Visibility = hasVisibleRules ? Visibility.Visible : Visibility.Collapsed;
        RulesListSurface.Visibility = hasVisibleRules ? Visibility.Visible : Visibility.Collapsed;
        EmptyState.Visibility = hasAnyRules ? Visibility.Collapsed : Visibility.Visible;
        NoSearchResultsState.Visibility = hasAnyRules && hasSearchQuery && !hasVisibleRules
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private async Task LoadRuleIconsAsync()
    {
        foreach (var item in _viewModel.Rules)
        {
            if (item.Icon is not null || string.IsNullOrWhiteSpace(item.ExecutablePath))
            {
                continue;
            }

            item.Icon = await _iconLoader.LoadAsync(item.ExecutablePath);
        }
    }

    private bool TryResolveRuleItem(object sender, out RuleListItemViewModel item)
    {
        if (sender is FrameworkElement { DataContext: RuleListItemViewModel dataContextItem })
        {
            item = dataContextItem;
            return true;
        }

        if (sender is FrameworkElement { Tag: Guid ruleId })
        {
            var match = _viewModel.Rules.FirstOrDefault(candidate => candidate.Id == ruleId);
            if (match is not null)
            {
                item = match;
                return true;
            }
        }

        item = null!;
        return false;
    }

    private void ShowFeedback(InfoBarSeverity severity, string title, string message)
    {
        RuleFeedbackBar.Severity = severity;
        RuleFeedbackBar.Title = title;
        RuleFeedbackBar.Message = message;
        RuleFeedbackBar.IsOpen = true;

        _feedbackTimer.Stop();
        if (severity == InfoBarSeverity.Success) _feedbackTimer.Start();
    }

    private void ShowFailure(string title, Exception ex)
    {
        Trace.WriteLine(
            $"[FlowIME.UI] operation={title} result=failed error={ex.GetType().Name}:{ex.Message}");
        ShowFeedback(
            InfoBarSeverity.Error,
            title,
            "操作没有完成。可在“设置 → 诊断信息”复制运行状态用于排查。");
    }
}
