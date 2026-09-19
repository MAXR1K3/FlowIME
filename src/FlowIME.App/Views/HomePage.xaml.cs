using FlowIME.App.Services;
using FlowIME.App.ViewModels;
using FlowIME.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FlowIME.App.Views;

public sealed partial class HomePage : Page
{
    private readonly AppServices _services;
    private readonly HomeViewModel _viewModel;
    private readonly ApplicationIconLoader _iconLoader = new();
    private bool _subscribed;
    private bool _automationToggleReady;
    private int _iconRequestVersion;

    public HomePage()
    {
        InitializeComponent();
        _services = ((App)Application.Current).Services;
        _viewModel = new HomeViewModel
        {
            IsAutomationEnabled = _services.IsAutomationEnabled
        };
        DataContext = _viewModel;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_subscribed)
        {
            _services.ForegroundContext.StateChanged += OnStateChanged;
            _services.AutomationEnabledChanged += OnAutomationEnabledChanged;
            _subscribed = true;
        }

        if (_services.ForegroundContext.Current is { } current)
        {
            ApplySnapshot(current);
        }

        _automationToggleReady = true;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_subscribed)
        {
            _services.ForegroundContext.StateChanged -= OnStateChanged;
            _services.AutomationEnabledChanged -= OnAutomationEnabledChanged;
            _subscribed = false;
        }
    }

    private void OnStateChanged(CurrentStateSnapshot snapshot)
    {
        _ = DispatcherQueue.TryEnqueue(() => ApplySnapshot(snapshot));
    }

    private void OnAutomationEnabledChanged(bool enabled)
    {
        _ = DispatcherQueue.TryEnqueue(() => _viewModel.IsAutomationEnabled = enabled);
    }

    private void ApplySnapshot(CurrentStateSnapshot snapshot)
    {
        _viewModel.UpdateCurrentState(
            snapshot.Window,
            snapshot.Input,
            snapshot.MatchedAction,
            snapshot.MatchedRule,
            snapshot.MatchedProviderId is null
                ? null
                : _services.GetInputMethodProviderDisplayName(snapshot.MatchedProviderId),
            snapshot.ResolutionSource,
            snapshot.Context);

        var requestVersion = ++_iconRequestVersion;
        _ = UpdateCurrentApplicationIconAsync(snapshot.Window.ExecutablePath, requestVersion);
    }

    private async Task UpdateCurrentApplicationIconAsync(string? executablePath, int requestVersion)
    {
        var icon = executablePath is { Length: > 0 }
            ? await _iconLoader.LoadAsync(executablePath)
            : null;

        if (requestVersion != _iconRequestVersion)
        {
            return;
        }

        CurrentAppImage.Source = icon;
        CurrentAppImage.Visibility = icon is null ? Visibility.Collapsed : Visibility.Visible;
        CurrentAppFallbackIcon.Visibility = icon is null ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void AutomationToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_automationToggleReady)
        {
            return;
        }

        // Suppress binding-driven re-entry while the service commits or rolls back.
        _automationToggleReady = false;
        AutomationToggle.IsEnabled = false;
        try
        {
            await _services.SetAutomationEnabledAsync(_viewModel.IsAutomationEnabled);
            ShowFeedback("已保存", _services.IsAutomationEnabled ? "自动切换已开启。" : "自动切换已暂停。", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[FlowIME.UI] automation-save failed: {ex}");
            ShowFeedback("无法保存自动切换设置", "已恢复原来的状态，请重试。", InfoBarSeverity.Error);
        }
        finally
        {
            _viewModel.IsAutomationEnabled = _services.IsAutomationEnabled;
            AutomationToggle.IsEnabled = true;
            _automationToggleReady = true;
        }
    }

    private void ManageRules_Click(object sender, RoutedEventArgs e)
    {
        ((App)Application.Current).MainWindow?.NavigateToSection("rules");
    }

    private async void CreateCurrentRule_Click(object sender, RoutedEventArgs e)
    {
        var snapshot = _services.ForegroundContext.Current;
        if (snapshot?.Window.ExecutablePath is not { Length: > 0 } executablePath)
        {
            ShowFeedback("当前应用不可配置", "FlowIME 无法取得这个窗口的可执行文件信息。", InfoBarSeverity.Informational);
            return;
        }

        try
        {
            var running = await _services.RunningApplications.GetRunningApplicationsAsync();
            var application = running.FirstOrDefault(app =>
                string.Equals(
                    app.ExecutablePath,
                    executablePath,
                    StringComparison.OrdinalIgnoreCase)) ??
                new RunningApplication(
                    snapshot.Window.ProcessName,
                    snapshot.Window.ProcessName,
                    executablePath,
                    snapshot.Window.Hwnd,
                    snapshot.Window.ProcessId,
                    snapshot.Window.PackageFamilyName,
                    snapshot.Window.ApplicationUserModelId);

            var rules = new RulesViewModel(_services.RuleRepository, _services.InputMethodProviders);
            await rules.LoadAsync();
            var existing = rules.FindByApplication(application);

            var picker = new AddApplicationViewModel(
                [application],
                _services.InputMethodProviders,
                _services.DefaultInputMethodProviderId,
                existingRules: rules.SourceRules)
            {
                PreviewPriority = rules.GetNextPriority()
            };
            picker.SelectedItem = picker.ApplicationItems[0];
            if (existing is not null)
            {
                picker.LoadExistingRule(existing);
            }

            var dialog = new AddApplicationDialog(picker)
            {
                XamlRoot = XamlRoot
            };
            if (existing is not null)
            {
                dialog.Title = "更新应用规则";
                dialog.PrimaryButtonText = "更新规则";
            }

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary || !picker.CanCreateRule)
            {
                return;
            }

            var rule = picker.CreateRule(picker.PreviewPriority);
            await rules.UpsertRuleAsync(rule);
            await _services.RefreshAfterRuleChangeAsync();

            var action = existing is null ? "已创建" : "已更新";
            ShowFeedback(
                $"{action}{application.DisplayName}的规则",
                "新的输入状态将在下次匹配到这个应用时生效。",
                InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[FlowIME.UI] rule-save failed: {ex}");
            ShowFeedback("无法保存应用规则", "请重试。若仍失败，可在设置的高级选项中复制诊断信息。", InfoBarSeverity.Error);
        }
    }


    private void DecisionWhy_Click(object sender, RoutedEventArgs e)
    {
        DecisionExplanationPanel.Visibility = DecisionExplanationPanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void HomeFeedbackBar_Closed(InfoBar sender, InfoBarClosedEventArgs args) =>
        HomeFeedbackBar.Visibility = Visibility.Collapsed;

    private void ShowFeedback(string title, string message, InfoBarSeverity severity)
    {
        HomeFeedbackBar.Visibility = Visibility.Visible;
        HomeFeedbackBar.Title = title;
        HomeFeedbackBar.Message = message;
        HomeFeedbackBar.Severity = severity;
        HomeFeedbackBar.IsOpen = true;
    }
}
