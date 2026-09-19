using FlowIME.Core.Settings;
using FlowIME.Core.Context;
using FlowIME.Core.Models;
using FlowIME.App.Services;
using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Navigation;

namespace FlowIME.App.Views;

public sealed partial class SettingsPage : Page
{
    private readonly DiagnosticsClipboardWriter _diagnosticsClipboard = new();
    private bool _synchronizingStartupToggle;
    // XAML raises control change events during InitializeComponent. Keep the guard
    // active until persisted overlay preferences have been loaded, otherwise the
    // default control values overwrite the user's saved settings on first visit.
    private bool _synchronizingInputStatusOverlayToggle = true;
    private bool _synchronizingGameplayKeyboardBaselineToggle;
    private bool _synchronizingGameplayHotkeyToggles;
    private bool _savingHotkeys;
    private bool _savingInputStatusOverlay;
    private CancellationTokenSource? _overlayPreferenceSaveCancellation;
    private string _viewMode = "settings";

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
            await RefreshGameplayKeyboardBaselineStateAsync();
            await RefreshGameplayHotkeyGuardStateAsync();
            await RefreshGameTextEntryProfileStateAsync();
            return;
        }

        RefreshStartupState();
        await RefreshInputStatusOverlayStateAsync();
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
        if (_synchronizingInputStatusOverlayToggle || _savingInputStatusOverlay)
        {
            return;
        }

        _overlayPreferenceSaveCancellation?.Cancel();
        UpdateInputStatusOverlayPersonalizationState();
        await SaveInputStatusOverlaySettingsAsync();
    }

    private async void InputStatusOverlayPreference_Changed(object sender, RoutedEventArgs e)
    {
        if (_synchronizingInputStatusOverlayToggle || _savingInputStatusOverlay)
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

        if (_synchronizingInputStatusOverlayToggle || _savingInputStatusOverlay)
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
        var settings = ReadInputStatusOverlaySettingsFromControls();
        _savingInputStatusOverlay = true;
        InputStatusOverlayToggle.IsEnabled = false;
        SetInputStatusOverlayPersonalizationControlsEnabled(false);
        ShowSettingsFeedback("正在保存…", InfoBarSeverity.Informational);
        try
        {
            var services = ((App)Application.Current).Services;
            await services.SetInputStatusOverlaySettingsAsync(settings);
            var snapshot = services.GetInputStatusOverlaySnapshot();
            InputStatusOverlayStatusText.Text = !settings.Enabled
                ? "已关闭：不显示输入状态浮层。"
                : snapshot.Started
                    ? $"已启用：{DescribeOverlayPosition(settings.Position)}显示中 / EN / US。"
                    : "设置已启用，但状态浮层暂不可用。请重启 FlowIME 后重试。";
            ShowSettingsFeedback("浮层设置已保存", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
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
            InputStatusOverlayToggle.IsEnabled = true;
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

    private async void ConfigureGameTextEntryButton_Click(object sender, RoutedEventArgs e)
    {
        var services = ((App)Application.Current).Services;
        var recent = services.GetRecentGameplayTarget();
        if (recent is null)
        {
            GameTextEntryProfileStatusText.Text =
                "还没有检测到游戏。请先进入一次能够被 FlowIME 识别的游戏，再回来配置。";
            return;
        }

        var existingProfiles = await services.GetGameTextEntryProfilesAsync();
        var existing = existingProfiles
            .FirstOrDefault(profile => StringComparer.Ordinal.Equals(
                profile.ApplicationIdentityKey,
                recent.ApplicationIdentityKey));

        var dialog = new GameTextEntryDialog(services, recent, existing)
        {
            XamlRoot = XamlRoot
        };

        ConfigureGameTextEntryButton.IsEnabled = false;
        try
        {
            var result = await dialog.ShowAsync();
            if (dialog.DeleteRequested)
            {
                await services.DeleteGameTextEntryProfileAsync(recent.ApplicationIdentityKey);
                GameTextEntryProfileStatusText.Text = $"已删除 {recent.ProcessName} 的文字输入配置。";
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
        finally
        {
            ConfigureGameTextEntryButton.IsEnabled = true;
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

        var enabled = InputStatusOverlayToggle.IsOn && !_savingInputStatusOverlay;
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

    private async Task RefreshGameTextEntryProfileStateAsync()
    {
        var services = ((App)Application.Current).Services;
        var recent = services.GetRecentGameplayTarget();
        var profiles = await services.GetGameTextEntryProfilesAsync();
        ConfigureGameTextEntryButton.IsEnabled = recent is not null;

        if (recent is null)
        {
            GameTextEntryProfileStatusText.Text = profiles.Count == 0
                ? "先运行一次游戏，FlowIME 会记住最近识别到的游戏，再为它配置文字输入方式。"
                : $"已有 {profiles.Count} 个游戏文字输入配置。运行一次游戏后可编辑最近游戏。";
            return;
        }

        var profile = profiles.FirstOrDefault(item => StringComparer.Ordinal.Equals(
            item.ApplicationIdentityKey,
            recent.ApplicationIdentityKey));
        GameTextEntryProfileStatusText.Text = profile is null
            ? $"最近检测到 {recent.ProcessName}，尚未配置文字输入。"
            : $"{recent.ProcessName} {(profile.Enabled ? "已配置" : "配置已停用")}：{DescribeDetectionMode(profile.DetectionMode)}；输入目标 {DescribeTarget(profile, services)}。";
    }

    private static string DescribeDetectionMode(GameTextEntryDetectionMode mode)
    {
        var items = new List<string>();
        if (mode.HasFlag(GameTextEntryDetectionMode.StandardTextControl)) items.Add("标准文字控件");
        if (mode.HasFlag(GameTextEntryDetectionMode.HotkeyProfile)) items.Add("快捷键");
        return items.Count == 0 ? "未启用" : string.Join(" + ", items);
    }

    private static string DescribeTarget(
        GameTextEntryProfile profile,
        AppServices services) =>
        profile.Action == InputAction.Keep
            ? "保持当前"
            : $"{services.GetInputMethodProviderDisplayName(profile.ProviderId)} · {(profile.Action == InputAction.Chinese ? "中文" : "英文")}";

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
