using FlowIME.App.Services;
using FlowIME.Core.Context;
using FlowIME.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FlowIME.App.Views;

public sealed partial class GameTextEntryDialog : ContentDialog
{
    private readonly RecentGameplayTarget _recent;
    private readonly GameTextEntryProfile? _existing;

    internal GameTextEntryDialog(
        AppServices services,
        RecentGameplayTarget recent,
        GameTextEntryProfile? existing)
    {
        InitializeComponent();
        _recent = recent;
        _existing = existing;

        GameNameText.Text = recent.ProcessName;
        EnabledToggle.IsOn = existing?.Enabled ?? true;
        StandardControlToggle.IsOn =
            existing?.DetectionMode.HasFlag(GameTextEntryDetectionMode.StandardTextControl) ?? true;
        HotkeyProfileToggle.IsOn =
            existing?.DetectionMode.HasFlag(GameTextEntryDetectionMode.HotkeyProfile) ?? false;
        EnterGesturesBox.Text = existing is null
            ? string.Empty
            : GameTextEntryKeyGestureParser.FormatList(existing.EnterGestures);
        ExitGesturesBox.Text = existing is null
            ? string.Empty
            : GameTextEntryKeyGestureParser.FormatList(existing.ExitGestures);

        var targets = BuildTargetOptions(services);
        TargetCombo.ItemsSource = targets;
        TargetCombo.SelectedItem = targets.FirstOrDefault(option =>
            option.Action == (existing?.Action ?? InputAction.Keep) &&
            StringComparer.Ordinal.Equals(option.ProviderId, existing?.ProviderId)) ?? targets[0];

        DeleteProfileButton.Visibility = existing is null ? Visibility.Collapsed : Visibility.Visible;
        UpdateHotkeyFields();
    }

    internal bool DeleteRequested { get; private set; }

    internal GameTextEntryProfile? ResultProfile { get; private set; }

    private void HotkeyProfileToggle_Toggled(object sender, RoutedEventArgs e) =>
        UpdateHotkeyFields();

    private void UpdateHotkeyFields()
    {
        if (HotkeyFields is null)
        {
            return;
        }

        var enabled = HotkeyProfileToggle.IsOn;
        HotkeyFields.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        EnterGesturesBox.IsEnabled = enabled;
        ExitGesturesBox.IsEnabled = enabled;
    }

    private void DeleteProfileButton_Click(object sender, RoutedEventArgs e)
    {
        DeleteRequested = true;
        Hide();
    }

    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        ValidationBar.IsOpen = false;

        var mode = GameTextEntryDetectionMode.None;
        if (StandardControlToggle.IsOn)
        {
            mode |= GameTextEntryDetectionMode.StandardTextControl;
        }
        if (HotkeyProfileToggle.IsOn)
        {
            mode |= GameTextEntryDetectionMode.HotkeyProfile;
        }

        if (mode == GameTextEntryDetectionMode.None)
        {
            args.Cancel = true;
            ValidationBar.Message = "至少启用一种文字输入检测方式。";
            ValidationBar.IsOpen = true;
            return;
        }

        IReadOnlyList<GameTextEntryKeyGesture> enterGestures;
        IReadOnlyList<GameTextEntryKeyGesture> exitGestures;
        try
        {
            enterGestures = HotkeyProfileToggle.IsOn
                ? GameTextEntryKeyGestureParser.ParseList(EnterGesturesBox.Text)
                : [];
            exitGestures = HotkeyProfileToggle.IsOn
                ? GameTextEntryKeyGestureParser.ParseList(ExitGesturesBox.Text)
                : [];

            if (HotkeyProfileToggle.IsOn &&
                (enterGestures.Count == 0 || exitGestures.Count == 0))
            {
                throw new FormatException("快捷键检测需要至少一个打开键和一个关闭键。");
            }
        }
        catch (FormatException ex)
        {
            args.Cancel = true;
            ValidationBar.Message = $"快捷键格式不正确：{ex.Message}";
            ValidationBar.IsOpen = true;
            return;
        }

        if (TargetCombo.SelectedItem is not TargetOption target)
        {
            args.Cancel = true;
            ValidationBar.Message = "无法读取文字输入目标。";
            ValidationBar.IsOpen = true;
            return;
        }

        ResultProfile = new GameTextEntryProfile
        {
            Id = _existing?.Id ?? $"game-chat:{_recent.ApplicationIdentityKey}",
            ApplicationIdentityKey = _recent.ApplicationIdentityKey,
            ApplicationDisplayName = _recent.ProcessName,
            Enabled = EnabledToggle.IsOn,
            DetectionMode = mode,
            ProviderId = target.ProviderId,
            Action = target.Action,
            EnterGestures = enterGestures,
            ExitGestures = exitGestures
        };
    }

    private static List<TargetOption> BuildTargetOptions(AppServices services)
    {
        var options = new List<TargetOption>
        {
            new("保持当前输入状态", null, InputAction.Keep)
        };

        foreach (var provider in services.InputMethodProviders)
        {
            options.Add(new($"{provider.DisplayName} · 中文", provider.Id, InputAction.Chinese));
            options.Add(new($"{provider.DisplayName} · 英文", provider.Id, InputAction.English));
        }

        return options;
    }

    private sealed record TargetOption(string DisplayName, string? ProviderId, InputAction Action)
    {
        public override string ToString() => DisplayName;
    }
}
