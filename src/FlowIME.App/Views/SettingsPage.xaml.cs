using FlowIME.Core.Settings;
using FlowIME.Core.Context;
using FlowIME.Core.Models;
using FlowIME.App.Services;
using FlowIME.App.ViewModels;
using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace FlowIME.App.Views;

public sealed partial class SettingsPage : Page
{
    private readonly DiagnosticsClipboardWriter _diagnosticsClipboard = new();
    private readonly LocalGameLibraryScanner _gameLibraryScanner = LocalGameLibraryScanner.CreateDefault();
    private readonly ApplicationIconLoader _applicationIconLoader = new();
    private IReadOnlyList<GameLibraryScanEntry> _discoveredGames = [];
    private IReadOnlyList<GameTextEntryTargetItemViewModel> _gameLibraryItems = [];
    private bool _gameLibraryScanned;
    // XAML raises control change events during InitializeComponent. Every guard
    // must start active, including controls hidden by the current page mode, or
    // their XAML defaults can overwrite persisted preferences before Loaded.
    private bool _synchronizingStartupToggle = true;
    // XAML raises control change events during InitializeComponent. Keep the guard
    // active until persisted overlay preferences have been loaded, otherwise the
    // default control values overwrite the user's saved settings on first visit.
    private bool _synchronizingInputStatusOverlayToggle = true;
    private bool _synchronizingGameplayKeyboardBaselineToggle = true;
    private bool _synchronizingGameplayHotkeyToggles = true;
    private bool _savingHotkeys;
    private bool _savingInputStatusOverlay;
    private InputStatusOverlaySettings? _pendingInputStatusOverlaySettings;
    private CancellationTokenSource? _overlayPreferenceSaveCancellation;
    private string _viewMode = "settings";
    private AppServices? _subscribedGameplayServices;

    public SettingsPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _viewMode = e.Parameter as string == "game" ? "game" : "settings";
        ApplyViewMode();
    }

    private async void SettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyViewMode();

        if (_viewMode == "game")
        {
            SubscribeToGameplayDetection();
            UpdateCurrentGameStatus();
            await RefreshGameplayKeyboardBaselineStateAsync();
            await RefreshGameplayHotkeyGuardStateAsync();
            await RefreshGameTextEntryProfileStateAsync();
            return;
        }

        RefreshStartupState();
        await RefreshInputStatusOverlayStateAsync();
    }

    private void SettingsPage_Unloaded(object sender, RoutedEventArgs e) =>
        UnsubscribeFromGameplayDetection();

    private void SubscribeToGameplayDetection()
    {
        var services = ((App)Application.Current).Services;
        if (ReferenceEquals(_subscribedGameplayServices, services))
        {
            return;
        }

        UnsubscribeFromGameplayDetection();
        services.ActiveGameplayTargetChanged += OnActiveGameplayTargetChanged;
        _subscribedGameplayServices = services;
    }

    private void UnsubscribeFromGameplayDetection()
    {
        if (_subscribedGameplayServices is null)
        {
            return;
        }

        _subscribedGameplayServices.ActiveGameplayTargetChanged -= OnActiveGameplayTargetChanged;
        _subscribedGameplayServices = null;
    }

    private void OnActiveGameplayTargetChanged(RecentGameplayTarget? target)
    {
        _ = target;
        DispatcherQueue.TryEnqueue(async () =>
        {
            if (_viewMode != "game" || _subscribedGameplayServices is null)
            {
                return;
            }

            UpdateCurrentGameStatus();
            try
            {
                await RefreshGameTextEntryProfileStateAsync();
            }
            catch (Exception ex)
            {
                Trace.WriteLine(
                    $"[FlowIME.GameLibrary] stage=live-refresh result=failed " +
                    $"error={ex.GetType().Name}:{ex.Message}");
            }
        });
    }

    private void UpdateCurrentGameStatus()
    {
        var services = ((App)Application.Current).Services;
        var status = GameplayDetectionStatusViewModel.Create(
            services.GetActiveGameplayTarget(),
            services.GetRecentGameplayTarget(),
            DateTimeOffset.Now);
        CurrentGameStatusTitle.Text = status.Title;
        CurrentGameStatusDescription.Text = status.Description;
        CurrentGameActiveIndicator.Visibility = status.IsActive
            ? Visibility.Visible
            : Visibility.Collapsed;
        CurrentGameInactiveIndicator.Visibility = status.IsActive
            ? Visibility.Collapsed
            : Visibility.Visible;
        AutomationProperties.SetName(
            CurrentGameStatusCard,
            $"{status.Title}。{status.Description}");
    }

    private void ApplyViewMode()
    {
        if (PageTitle is null)
        {
            return;
        }

        var gameMode = _viewMode == "game";
        PageTitle.Text = gameMode ? "游戏" : "设置";
        PageSubtitle.Text = gameMode
            ? "减少游戏中的输入法误触，并为聊天保留合适的输入状态。"
            : "管理启动方式与输入状态浮层。更改后自动保存。";

        GeneralSection.Visibility = gameMode ? Visibility.Collapsed : Visibility.Visible;
        OverlaySection.Visibility = gameMode ? Visibility.Collapsed : Visibility.Visible;
        DiagnosticsSection.Visibility = gameMode ? Visibility.Collapsed : Visibility.Visible;
        GameProtectionSection.Visibility = gameMode ? Visibility.Visible : Visibility.Collapsed;
        GameTextEntrySection.Visibility = gameMode ? Visibility.Visible : Visibility.Collapsed;
    }

    private void StartupToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_synchronizingStartupToggle)
        {
            return;
        }

        try
        {
            var startup = ((App)Application.Current).Services.StartupRegistration;
            startup.SetEnabled(StartupToggle.IsOn);
            ShowSettingsFeedback("启动设置已保存", InfoBarSeverity.Success);
            StartupStatusText.Text = StartupToggle.IsOn
                ? "已启用：下次登录 Windows 后 FlowIME 会直接在后台运行。"
                : "登录后在后台启动，不弹出主窗口。";
        }
        catch (Exception ex)
        {
            Trace.WriteLine(
                $"[FlowIME.Lifecycle] stage=startup-registration result=failed " +
                $"error={ex.GetType().Name}:{ex.Message}");

            RefreshStartupState();
            ShowSettingsFeedback("无法更新开机启动设置，请重试。", InfoBarSeverity.Error);
        }
    }

    private async void InputStatusOverlayToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_synchronizingInputStatusOverlayToggle)
        {
            return;
        }

        _overlayPreferenceSaveCancellation?.Cancel();
        UpdateInputStatusOverlayPersonalizationState();
        await SaveInputStatusOverlaySettingsAsync();
    }

    private async void InputStatusOverlayPreference_Changed(object sender, RoutedEventArgs e)
    {
        if (_synchronizingInputStatusOverlayToggle)
        {
            return;
        }

        _overlayPreferenceSaveCancellation?.Cancel();
        await SaveInputStatusOverlaySettingsAsync();
    }

    private async void InputStatusOverlayOpacitySlider_ValueChanged(
        object sender,
        RangeBaseValueChangedEventArgs e)
    {
        if (InputStatusOverlayOpacityText is not null)
        {
            InputStatusOverlayOpacityText.Text = $"{Math.Round(e.NewValue):0}%";
        }

        if (_synchronizingInputStatusOverlayToggle)
        {
            return;
        }

        _overlayPreferenceSaveCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _overlayPreferenceSaveCancellation = cancellation;
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(240), cancellation.Token);
            await SaveInputStatusOverlaySettingsAsync();
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task SaveInputStatusOverlaySettingsAsync()
    {
        // Never discard a control event just because an earlier disk write is still
        // running. Keep replacing the pending snapshot and drain it after the active
        // write; the final persisted value is therefore always the latest UI state.
        _pendingInputStatusOverlaySettings = ReadInputStatusOverlaySettingsFromControls();
        if (_savingInputStatusOverlay)
        {
            return;
        }

        _savingInputStatusOverlay = true;
        ShowSettingsFeedback("正在保存…", InfoBarSeverity.Informational);
        try
        {
            var services = ((App)Application.Current).Services;
            InputStatusOverlaySettings? savedSettings = null;
            while (_pendingInputStatusOverlaySettings is { } settings)
            {
                _pendingInputStatusOverlaySettings = null;
                await services.SetInputStatusOverlaySettingsAsync(settings);
                savedSettings = settings;
            }

            if (savedSettings is null)
            {
                return;
            }

            var snapshot = services.GetInputStatusOverlaySnapshot();
            InputStatusOverlayStatusText.Text = !savedSettings.Enabled
                ? "已关闭：不显示输入状态浮层。"
                : snapshot.Started
                    ? $"已启用：{DescribeOverlayPosition(savedSettings.Position)}显示中 / EN / US。"
                    : "设置已启用，但状态浮层暂不可用。请重启 FlowIME 后重试。";
            ShowSettingsFeedback("浮层设置已保存", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            _pendingInputStatusOverlaySettings = null;
            Trace.WriteLine(
                $"[FlowIME.Overlay] stage=settings-save result=failed " +
                $"error={ex.GetType().Name}:{ex.Message}");
            InputStatusOverlayStatusText.Text = "无法保存输入状态浮层设置。";
            await RefreshInputStatusOverlayStateAsync();
            ShowSettingsFeedback("无法保存浮层设置，请重试。", InfoBarSeverity.Error);
        }
        finally
        {
            _savingInputStatusOverlay = false;
            UpdateInputStatusOverlayPersonalizationState();
        }
    }

    private async void GameplayKeyboardBaselineToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_synchronizingGameplayKeyboardBaselineToggle)
        {
            return;
        }

        var settings = new GameplayKeyboardBaselineSettings
        {
            Enabled = GameplayKeyboardBaselineToggle.IsOn
        };

        GameplayKeyboardBaselineToggle.IsEnabled = false;
        ShowSettingsFeedback("正在保存…", InfoBarSeverity.Informational);
        try
        {
            var services = ((App)Application.Current).Services;
            await services.SetGameplayKeyboardBaselineSettingsAsync(settings);
            UpdateGameplayKeyboardBaselineStatus(
                services.GetGameplayKeyboardBaselineSnapshot());
            ShowSettingsFeedback("游戏键盘设置已保存", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            Trace.WriteLine(
                $"[FlowIME.GameplayKeyboard] stage=settings-save result=failed " +
                $"error={ex.GetType().Name}:{ex.Message}");
            GameplayKeyboardBaselineStatusText.Text = "无法保存游戏美式键盘设置。";
            await RefreshGameplayKeyboardBaselineStateAsync();
            ShowSettingsFeedback("无法保存游戏键盘设置，请重试。", InfoBarSeverity.Error);
        }
        finally
        {
            GameplayKeyboardBaselineToggle.IsEnabled = true;
        }
    }

    private async void GameplayHotkeyToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_synchronizingGameplayHotkeyToggles || _savingHotkeys)
        {
            return;
        }

        UpdateGameplayHotkeySubControls();
        var settings = new GameplayHotkeyGuardSettings
        {
            Enabled = GameplayHotkeyGuardToggle.IsOn,
            BlockWinSpace = WinSpaceToggle.IsOn,
            BlockCtrlSpace = CtrlSpaceToggle.IsOn,
            BlockLegacyLanguageHotkeys = LegacyHotkeyToggle.IsOn
        };

        _savingHotkeys = true;
        GameplayHotkeyGuardToggle.IsEnabled = false;
        UpdateGameplayHotkeySubControls();
        ShowSettingsFeedback("正在保存…", InfoBarSeverity.Informational);
        try
        {
            await ((App)Application.Current)
                .Services
                .SetGameplayHotkeyGuardSettingsAsync(settings);
            GameplayHotkeyStatusText.Text = settings.Enabled
                ? "已启用：仅在检测到游戏场景时生效；离开游戏会立即恢复快捷键。"
                : "已关闭：FlowIME 不会拦截游戏中的输入法快捷键。";
            ShowSettingsFeedback("快捷键保护设置已保存", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            Trace.WriteLine(
                $"[FlowIME.GameplayHotkey] stage=settings-save result=failed " +
                $"error={ex.GetType().Name}:{ex.Message}");
            GameplayHotkeyStatusText.Text = "无法保存游戏快捷键保护设置。";
            await RefreshGameplayHotkeyGuardStateAsync();
            ShowSettingsFeedback("无法保存快捷键设置，请重试。", InfoBarSeverity.Error);
        }
        finally
        {
            _savingHotkeys = false;
            GameplayHotkeyGuardToggle.IsEnabled = true;
            UpdateGameplayHotkeySubControls();
        }
    }

    private async void ConfigureGameLibraryItemButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button ||
            button.DataContext is not GameTextEntryTargetItemViewModel selected)
        {
            return;
        }

        button.IsEnabled = false;
        try
        {
            if (!selected.CanConfigureDirectly)
            {
                var resolved = await ResolveExecutableForTargetAsync(selected);
                if (resolved is null)
                {
                    GameTextEntryProfileStatusText.Text =
                        $"{selected.DisplayName} 需要关联实际运行的游戏 EXE 后才能配置。";
                    return;
                }
                selected = resolved;
            }

            await ConfigureGameTextEntryTargetAsync(selected);
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    private async void AddGameTextEntryButton_Click(object sender, RoutedEventArgs e)
    {
        AddGameTextEntryButton.IsEnabled = false;
        try
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".exe");

            var app = (App)Application.Current;
            if (app.MainWindow is not null)
            {
                var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(app.MainWindow);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, windowHandle);
            }

            var file = await picker.PickSingleFileAsync();
            if (file is null)
            {
                return;
            }

            var application = ExecutableCandidateFactory.Create(file.Path);
            var identity = FlowIME.Core.Context.ApplicationIdentity.FromRunningApplication(application);
            var profiles = await ((App)Application.Current).Services.GetGameTextEntryProfilesAsync();
            var existing = profiles.FirstOrDefault(profile => StringComparer.Ordinal.Equals(
                profile.ApplicationIdentityKey,
                identity.Key));
            var selected = new GameTextEntryTargetItemViewModel(
                identity.Key,
                application.DisplayName,
                IsRecent: false,
                existing,
                SourceName: "手动",
                ExecutablePath: file.Path);

            await ConfigureGameTextEntryTargetAsync(selected);
        }
        catch (Exception ex)
        {
            Trace.WriteLine(
                $"[FlowIME.GameTextEntry] stage=game-picker result=failed " +
                $"error={ex.GetType().Name}:{ex.Message}");
            GameTextEntryProfileStatusText.Text = "无法添加游戏，请确认选择的是可访问的 EXE 文件。";
        }
        finally
        {
            AddGameTextEntryButton.IsEnabled = true;
        }
    }

    private async void ScanGameLibraryButton_Click(object sender, RoutedEventArgs e)
    {
        ScanGameLibraryButton.IsEnabled = false;
        GameTextEntryProfileStatusText.Text = "正在扫描 Steam 和 Epic 本地游戏库…";
        try
        {
            _discoveredGames = await _gameLibraryScanner.ScanAsync();
            _gameLibraryScanned = true;
            await RefreshGameTextEntryProfileStateAsync();
            GameTextEntryProfileStatusText.Text = _discoveredGames.Count == 0
                ? "未从 Steam 或 Epic 本地清单发现游戏；仍可使用“添加游戏…”。"
                : $"已发现 {_discoveredGames.Count} 款游戏。未明确主程序的项目需要手动关联 EXE。";
        }
        catch (Exception ex)
        {
            Trace.WriteLine(
                $"[FlowIME.GameLibrary] stage=scan result=failed error={ex.GetType().Name}:{ex.Message}");
            GameTextEntryProfileStatusText.Text = "游戏库扫描失败；已有配置未受影响。";
        }
        finally
        {
            ScanGameLibraryButton.IsEnabled = true;
        }
    }

    private void GameLibrarySearchBox_TextChanged(
        AutoSuggestBox sender,
        AutoSuggestBoxTextChangedEventArgs args) =>
        ApplyGameLibraryFilter();

    private void ApplyGameLibraryFilter()
    {
        var query = GameLibrarySearchBox.Text?.Trim();
        var filtered = string.IsNullOrWhiteSpace(query)
            ? _gameLibraryItems
            : _gameLibraryItems.Where(item =>
                    item.DisplayName.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                    item.SourceName.Contains(query, StringComparison.CurrentCultureIgnoreCase))
                .ToArray();
        GameLibraryList.ItemsSource = filtered;
    }

    private async Task<GameTextEntryTargetItemViewModel?> ResolveExecutableForTargetAsync(
        GameTextEntryTargetItemViewModel target)
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".exe");
        var app = (App)Application.Current;
        if (app.MainWindow is not null)
        {
            var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(app.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, windowHandle);
        }

        var file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return null;
        }

        var application = ExecutableCandidateFactory.Create(file.Path);
        var identity = FlowIME.Core.Context.ApplicationIdentity.FromRunningApplication(application);
        var profiles = await app.Services.GetGameTextEntryProfilesAsync();
        var existing = profiles.FirstOrDefault(profile => StringComparer.Ordinal.Equals(
            profile.ApplicationIdentityKey,
            identity.Key));
        return new GameTextEntryTargetItemViewModel(
            identity.Key,
            target.DisplayName,
            target.IsRecent,
            existing,
            target.SourceName,
            file.Path,
            target.ArtworkPath,
            target.ExecutableCandidates);
    }

    private async Task ConfigureGameTextEntryTargetAsync(GameTextEntryTargetItemViewModel selected)
    {
        var services = ((App)Application.Current).Services;
        var existingProfiles = await services.GetGameTextEntryProfilesAsync();
        var existing = existingProfiles
            .FirstOrDefault(profile => StringComparer.Ordinal.Equals(
                profile.ApplicationIdentityKey,
                selected.ApplicationIdentityKey));

        var dialog = new GameTextEntryDialog(services, selected.ToGameplayTarget(), existing)
        {
            XamlRoot = XamlRoot
        };

        try
        {
            var result = await dialog.ShowAsync();
            if (dialog.DeleteRequested)
            {
                await services.DeleteGameTextEntryProfileAsync(selected.ApplicationIdentityKey);
                await RefreshGameTextEntryProfileStateAsync();
                GameTextEntryProfileStatusText.Text = $"已删除 {selected.DisplayName} 的文字输入配置。";
                return;
            }

            if (result != ContentDialogResult.Primary || dialog.ResultProfile is null)
            {
                return;
            }

            await services.SaveGameTextEntryProfileAsync(dialog.ResultProfile);
            await RefreshGameTextEntryProfileStateAsync();
        }
        catch (Exception ex)
        {
            Trace.WriteLine(
                $"[FlowIME.GameTextEntry] stage=profile-save result=failed " +
                $"error={ex.GetType().Name}:{ex.Message}");
            GameTextEntryProfileStatusText.Text = "无法保存游戏文字输入配置。";
        }
    }

    private async void CopyDiagnosticsButton_Click(object sender, RoutedEventArgs e)
    {
        CopyDiagnosticsButton.IsEnabled = false;
        try
        {
            var report = await ((App)Application.Current)
                .Services
                .CreateDiagnosticsReportAsync();
            var result = await _diagnosticsClipboard.WriteTextAsync(report);
            if (result.Success)
            {
                DiagnosticsStatusText.Text = result.Attempts == 1
                    ? "已复制诊断信息。"
                    : $"已复制诊断信息（重试 {result.Attempts - 1} 次）。";
                return;
            }

            Trace.WriteLine(
                $"[FlowIME.Diagnostics] stage=set-content result=failed " +
                $"attempts={result.Attempts} type={result.ErrorType ?? "unknown"} " +
                $"hresult={(result.HResult is int hresult ? $"0x{unchecked((uint)hresult):X8}" : "none")} " +
                $"message={SanitizeTraceValue(result.Message)}");
            DiagnosticsStatusText.Text = "复制失败，剪贴板可能正被其他程序占用，请重试。";
        }
        catch (Exception ex)
        {
            Trace.WriteLine(
                $"[FlowIME.Diagnostics] stage=copy result=failed " +
                $"error={ex.GetType().Name}:{ex.Message}");
            DiagnosticsStatusText.Text = "复制失败，请稍后重试。";
        }
        finally
        {
            CopyDiagnosticsButton.IsEnabled = true;
        }
    }

    private void SettingsFeedbackBar_Closed(InfoBar sender, InfoBarClosedEventArgs args) =>
        SettingsFeedbackBar.Visibility = Visibility.Collapsed;

    private void ShowSettingsFeedback(string message, InfoBarSeverity severity)
    {
        SettingsFeedbackBar.Visibility = Visibility.Visible;
        SettingsFeedbackBar.Severity = severity;
        SettingsFeedbackBar.Message = message;
        SettingsFeedbackBar.IsOpen = true;
    }

    private static string SanitizeTraceValue(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? "none"
            : value.Replace('\r', ' ').Replace('\n', ' ').Trim();

    private InputStatusOverlaySettings ReadInputStatusOverlaySettingsFromControls() =>
        new InputStatusOverlaySettings
        {
            Enabled = InputStatusOverlayToggle.IsOn,
            Position = InputStatusOverlayPositionComboBox.SelectedIndex is >= 0 and <= 7
                ? (InputStatusOverlayPosition)InputStatusOverlayPositionComboBox.SelectedIndex
                : InputStatusOverlayPosition.BottomCenter,
            Size = InputStatusOverlaySizeComboBox.SelectedIndex is >= 0 and <= 2
                ? (InputStatusOverlaySize)InputStatusOverlaySizeComboBox.SelectedIndex
                : InputStatusOverlaySize.Medium,
            OpacityPercent = (int)Math.Round(InputStatusOverlayOpacitySlider.Value),
            AnimationsEnabled = InputStatusOverlayAnimationsToggle.IsOn
        }.Normalize();

    private void UpdateInputStatusOverlayPersonalizationState()
    {
        if (InputStatusOverlayPersonalizationPanel is null)
        {
            return;
        }

        var enabled = InputStatusOverlayToggle.IsOn;
        SetInputStatusOverlayPersonalizationControlsEnabled(enabled);
        InputStatusOverlayPersonalizationPanel.Opacity = enabled ? 1d : 0.55d;
    }

    private void SetInputStatusOverlayPersonalizationControlsEnabled(bool enabled)
    {
        InputStatusOverlayPositionComboBox.IsEnabled = enabled;
        InputStatusOverlaySizeComboBox.IsEnabled = enabled;
        InputStatusOverlayOpacitySlider.IsEnabled = enabled;
        InputStatusOverlayAnimationsToggle.IsEnabled = enabled;
    }

    private static string DescribeOverlayPosition(InputStatusOverlayPosition position) =>
        position switch
        {
            InputStatusOverlayPosition.Caret => "跟随输入光标",
            InputStatusOverlayPosition.TopLeft => "在屏幕左上角",
            InputStatusOverlayPosition.TopCenter => "在屏幕顶部",
            InputStatusOverlayPosition.TopRight => "在屏幕右上角",
            InputStatusOverlayPosition.Center => "在屏幕中央",
            InputStatusOverlayPosition.BottomLeft => "在屏幕左下角",
            InputStatusOverlayPosition.BottomRight => "在屏幕右下角",
            _ => "在屏幕底部"
        };

    private async Task RefreshInputStatusOverlayStateAsync()
    {
        _synchronizingInputStatusOverlayToggle = true;
        try
        {
            var services = ((App)Application.Current).Services;
            var settings = (await services.GetInputStatusOverlaySettingsAsync()).Normalize();
            InputStatusOverlayToggle.IsOn = settings.Enabled;
            InputStatusOverlayPositionComboBox.SelectedIndex = (int)settings.Position;
            InputStatusOverlaySizeComboBox.SelectedIndex = (int)settings.Size;
            InputStatusOverlayOpacitySlider.Value = settings.OpacityPercent;
            InputStatusOverlayOpacityText.Text = $"{settings.OpacityPercent}%";
            InputStatusOverlayAnimationsToggle.IsOn = settings.AnimationsEnabled;
            await Task.Yield();
            var snapshot = services.GetInputStatusOverlaySnapshot();
            InputStatusOverlayStatusText.Text = !settings.Enabled
                ? "已关闭：不显示输入状态浮层。"
                : !snapshot.Started
                    ? "设置已启用，但状态浮层暂不可用。请重启 FlowIME 后重试。"
                    : $"已启用：{DescribeOverlayPosition(settings.Position)}显示中 / EN / US。";
        }
        catch (Exception ex)
        {
            Trace.WriteLine(
                $"[FlowIME.Overlay] stage=settings-read result=failed " +
                $"error={ex.GetType().Name}:{ex.Message}");
            InputStatusOverlayToggle.IsOn = true;
            InputStatusOverlayStatusText.Text = "无法读取输入状态浮层设置。";
        }
        finally
        {
            _synchronizingInputStatusOverlayToggle = false;
            UpdateInputStatusOverlayPersonalizationState();
        }
    }

    private async Task RefreshGameplayKeyboardBaselineStateAsync()
    {
        _synchronizingGameplayKeyboardBaselineToggle = true;
        try
        {
            var services = ((App)Application.Current).Services;
            var settings = await services.GetGameplayKeyboardBaselineSettingsAsync();
            GameplayKeyboardBaselineToggle.IsOn = settings.Enabled;
            UpdateGameplayKeyboardBaselineStatus(
                services.GetGameplayKeyboardBaselineSnapshot());
        }
        catch (Exception ex)
        {
            Trace.WriteLine(
                $"[FlowIME.GameplayKeyboard] stage=settings-read result=failed " +
                $"error={ex.GetType().Name}:{ex.Message}");
            GameplayKeyboardBaselineToggle.IsOn = true;
            GameplayKeyboardBaselineStatusText.Text = "无法读取游戏美式键盘设置。";
        }
        finally
        {
            _synchronizingGameplayKeyboardBaselineToggle = false;
        }
    }

    private void UpdateGameplayKeyboardBaselineStatus(
        FlowIME.Windows.Input.GameplayKeyboardBaselineSnapshot snapshot)
    {
        if (!snapshot.Settings.Enabled)
        {
            GameplayKeyboardBaselineStatusText.Text =
                "已关闭：FlowIME 不会在游戏场景中主动切换键盘布局。";
            return;
        }

        if (!snapshot.UsKeyboardAvailable)
        {
            GameplayKeyboardBaselineStatusText.Text =
                "未检测到标准 US 键盘。请先在 Windows 中启用 English (United States) / US keyboard；无需安装完整英文显示语言。";
            return;
        }

        GameplayKeyboardBaselineStatusText.Text = snapshot.Active
            ? snapshot.LastOutcome switch
            {
                FlowIME.Windows.Input.GameplayKeyboardBaselineOutcome.Applied =>
                    "游戏保护已生效：当前游戏已切换到标准 US 键盘。",
                FlowIME.Windows.Input.GameplayKeyboardBaselineOutcome.AlreadyUsKeyboard =>
                    "游戏保护已生效：当前游戏已经处于标准 US 键盘。",
                FlowIME.Windows.Input.GameplayKeyboardBaselineOutcome.Requested =>
                    "游戏保护已触发：已向当前游戏发送标准 US 键盘切换请求。",
                FlowIME.Windows.Input.GameplayKeyboardBaselineOutcome.ApplyFailed =>
                    "检测到游戏，但 US 键盘切换请求失败；可复制诊断信息查看原因。",
                _ => "检测到游戏场景时会切换到标准 US 键盘。"
            }
            : "已就绪：检测到游戏场景时切换到标准 US 键盘；离开游戏后继续由应用规则接管。";
    }

    private async Task RefreshGameplayHotkeyGuardStateAsync()
    {
        _synchronizingGameplayHotkeyToggles = true;
        try
        {
            var services = ((App)Application.Current).Services;
            var settings = await services.GetGameplayHotkeyGuardSettingsAsync();
            GameplayHotkeyGuardToggle.IsOn = settings.Enabled;
            WinSpaceToggle.IsOn = settings.BlockWinSpace;
            CtrlSpaceToggle.IsOn = settings.BlockCtrlSpace;
            LegacyHotkeyToggle.IsOn = settings.BlockLegacyLanguageHotkeys;

            var snapshot = services.GetGameplayHotkeyGuardSnapshot();
            GameplayHotkeyStatusText.Text = !snapshot.Installed
                ? "快捷键保护暂不可用。请重启 FlowIME；若仍无效，可在设置中复制诊断信息。"
                : settings.Enabled
                    ? "已启用：仅在检测到游戏场景时生效；离开游戏会立即恢复快捷键。"
                    : "已关闭：FlowIME 不会拦截游戏中的输入法快捷键。";
        }
        catch (Exception ex)
        {
            Trace.WriteLine(
                $"[FlowIME.GameplayHotkey] stage=settings-read result=failed " +
                $"error={ex.GetType().Name}:{ex.Message}");
            GameplayHotkeyGuardToggle.IsOn = false;
            WinSpaceToggle.IsOn = true;
            CtrlSpaceToggle.IsOn = true;
            LegacyHotkeyToggle.IsOn = false;
            GameplayHotkeyStatusText.Text = "无法读取游戏快捷键保护设置。";
        }
        finally
        {
            _synchronizingGameplayHotkeyToggles = false;
            UpdateGameplayHotkeySubControls();
        }
    }

    private void UpdateGameplayHotkeySubControls()
    {
        var enabled = GameplayHotkeyGuardToggle.IsOn && !_savingHotkeys;
        WinSpaceToggle.IsEnabled = enabled;
        CtrlSpaceToggle.IsEnabled = enabled;
        LegacyHotkeyToggle.IsEnabled = enabled;
    }

    private async ValueTask<ImageSource?> LoadGameArtworkAsync(
        GameTextEntryTargetItemViewModel item)
    {
        if (!string.IsNullOrWhiteSpace(item.ArtworkPath))
        {
            try
            {
                var file = await StorageFile.GetFileFromPathAsync(item.ArtworkPath);
                await using var stream = await file.OpenStreamForReadAsync();
                var image = new BitmapImage();
                await image.SetSourceAsync(stream.AsRandomAccessStream());
                return image;
            }
            catch (Exception ex) when (
                ex is IOException or UnauthorizedAccessException or ArgumentException or
                    System.Runtime.InteropServices.COMException)
            {
            }
        }

        return string.IsNullOrWhiteSpace(item.ExecutablePath)
            ? null
            : await _applicationIconLoader.LoadAsync(item.ExecutablePath);
    }

    private async Task RefreshGameTextEntryProfileStateAsync()
    {
        var services = ((App)Application.Current).Services;
        if (!_gameLibraryScanned)
        {
            try
            {
                _discoveredGames = await _gameLibraryScanner.ScanAsync();
            }
            catch (Exception ex)
            {
                Trace.WriteLine(
                    $"[FlowIME.GameLibrary] stage=auto-scan result=failed error={ex.GetType().Name}:{ex.Message}");
            }
            _gameLibraryScanned = true;
        }

        var recent = services.GetRecentGameplayTarget();
        var profiles = await services.GetGameTextEntryProfilesAsync();
        _gameLibraryItems = GameTextEntryTargetItemViewModel.Build(
            profiles,
            recent,
            _discoveredGames);

        foreach (var item in _gameLibraryItems)
        {
            item.Icon = await LoadGameArtworkAsync(item);
        }

        ApplyGameLibraryFilter();
        var configuredCount = _gameLibraryItems.Count(item => item.HasProfile);
        GameTextEntryProfileStatusText.Text = _gameLibraryItems.Count == 0
            ? "尚未发现游戏。可扫描 Steam/Epic，或手动添加游戏程序。"
            : $"{_gameLibraryItems.Count} 款游戏 · {configuredCount} 款已配置；扫描不会覆盖已有配置。";
    }

    private void RefreshStartupState()
    {
        _synchronizingStartupToggle = true;
        try
        {
            var enabled = ((App)Application.Current).Services.StartupRegistration.IsEnabled;
            StartupToggle.IsOn = enabled;
            StartupStatusText.Text = enabled
                ? "已启用：下次登录 Windows 后 FlowIME 会直接在后台运行。"
                : "登录后在后台启动，不弹出主窗口。";
        }
        catch (Exception ex)
        {
            Trace.WriteLine(
                $"[FlowIME.Lifecycle] stage=startup-state result=failed " +
                $"error={ex.GetType().Name}:{ex.Message}");
            StartupToggle.IsOn = false;
            StartupStatusText.Text = "无法读取开机启动状态。";
        }
        finally
        {
            _synchronizingStartupToggle = false;
        }
    }
}
