using FlowIME.App.Services;
using FlowIME.Core.Context;
using FlowIME.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Input;
using Windows.System;
using Windows.UI.Core;

namespace FlowIME.App.Views;

public sealed partial class GameTextEntryDialog : ContentDialog
{
    private readonly RecentGameplayTarget _recent;
    private readonly GameTextEntryProfile? _existing;
    private readonly List<GameTextEntryKeyGesture> _enterGestures = [];
    private readonly List<GameTextEntryKeyGesture> _exitGestures = [];

    internal GameTextEntryDialog(
        AppServices services,
        RecentGameplayTarget recent,
        GameTextEntryProfile? existing)
    {
        InitializeComponent();
        _recent = recent;
        _existing = existing;

        Title = $"{recent.ProcessName} · 游戏聊天";
        var seed = existing ?? GameTextEntryProfile.CreateDefault(
            recent.ApplicationIdentityKey,
            recent.ProcessName);
        _enterGestures.AddRange(seed.EnterGestures);
        _exitGestures.AddRange(seed.ExitGestures);
        EnabledToggle.IsOn = seed.Enabled;
        StandardControlToggle.IsOn =
            seed.DetectionMode.HasFlag(GameTextEntryDetectionMode.StandardTextControl);
        HotkeyProfileToggle.IsOn =
            seed.DetectionMode.HasFlag(GameTextEntryDetectionMode.HotkeyProfile);
        RefreshGestureBoxes();

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
        ClearEnterGesturesButton.IsEnabled = enabled && _enterGestures.Count > 0;
        ClearExitGesturesButton.IsEnabled = enabled && _exitGestures.Count > 0;
        SetPresetButtonsEnabled(enabled);
    }

    private void PresetGestureButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton { Tag: string tag } button)
        {
            return;
        }

        var parts = tag.Split(':', 2);
        if (parts.Length != 2 || !uint.TryParse(parts[1], out var virtualKey))
        {
            return;
        }

        var target = StringComparer.Ordinal.Equals(parts[0], "enter")
            ? _enterGestures
            : _exitGestures;
        var gesture = new GameTextEntryKeyGesture(virtualKey);
        if (button.IsChecked == true)
        {
            if (!target.Contains(gesture))
            {
                target.Add(gesture);
            }
        }
        else
        {
            target.Remove(gesture);
        }

        RefreshGestureBoxes();
        ValidationBar.IsOpen = false;
    }

    private void GestureBox_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!HotkeyProfileToggle.IsOn || sender is not TextBox textBox)
        {
            return;
        }

        if (IsModifierKey(e.Key))
        {
            e.Handled = true;
            return;
        }

        var gesture = new GameTextEntryKeyGesture(
            checked((uint)e.Key),
            ReadModifiers());
        var target = ReferenceEquals(textBox, EnterGesturesBox)
            ? _enterGestures
            : _exitGestures;
        if (!target.Contains(gesture))
        {
            target.Add(gesture);
        }

        RefreshGestureBoxes();
        ValidationBar.IsOpen = false;
        e.Handled = true;
    }

    private void ClearEnterGesturesButton_Click(object sender, RoutedEventArgs e)
    {
        _enterGestures.Clear();
        RefreshGestureBoxes();
    }

    private void ClearExitGesturesButton_Click(object sender, RoutedEventArgs e)
    {
        _exitGestures.Clear();
        RefreshGestureBoxes();
    }

    private void RefreshGestureBoxes()
    {
        EnterGesturesBox.Text = GameTextEntryKeyGestureParser.FormatList(_enterGestures);
        ExitGesturesBox.Text = GameTextEntryKeyGestureParser.FormatList(_exitGestures);
        if (ClearEnterGesturesButton is not null)
        {
            ClearEnterGesturesButton.IsEnabled = HotkeyProfileToggle.IsOn && _enterGestures.Count > 0;
            ClearExitGesturesButton.IsEnabled = HotkeyProfileToggle.IsOn && _exitGestures.Count > 0;
            RefreshPresetButtons();
        }
    }

    private void RefreshPresetButtons()
    {
        SetPresetState(OpenEnterPreset, _enterGestures, 0x0D);
        SetPresetState(OpenTPreset, _enterGestures, 0x54);
        SetPresetState(OpenYPreset, _enterGestures, 0x59);
        SetPresetState(OpenUPreset, _enterGestures, 0x55);
        SetPresetState(OpenSlashPreset, _enterGestures, 0xBF);
        SetPresetState(OpenCommaPreset, _enterGestures, 0xBC);
        SetPresetState(OpenPeriodPreset, _enterGestures, 0xBE);
        SetPresetState(ExitEnterPreset, _exitGestures, 0x0D);
        SetPresetState(ExitEscapePreset, _exitGestures, 0x1B);
    }

    private static void SetPresetState(
        ToggleButton button,
        IReadOnlyCollection<GameTextEntryKeyGesture> gestures,
        uint virtualKey) =>
        button.IsChecked = gestures.Contains(new GameTextEntryKeyGesture(virtualKey));

    private void SetPresetButtonsEnabled(bool enabled)
    {
        foreach (var button in new[]
        {
            OpenEnterPreset,
            OpenTPreset,
            OpenYPreset,
            OpenUPreset,
            OpenSlashPreset,
            OpenCommaPreset,
            OpenPeriodPreset,
            ExitEnterPreset,
            ExitEscapePreset
        })
        {
            button.IsEnabled = enabled;
        }
    }

    private static GameTextEntryModifierKeys ReadModifiers()
    {
        var modifiers = GameTextEntryModifierKeys.None;
        if (IsDown(VirtualKey.Control)) modifiers |= GameTextEntryModifierKeys.Control;
        if (IsDown(VirtualKey.Menu)) modifiers |= GameTextEntryModifierKeys.Alt;
        if (IsDown(VirtualKey.Shift)) modifiers |= GameTextEntryModifierKeys.Shift;
        if (IsDown(VirtualKey.LeftWindows) || IsDown(VirtualKey.RightWindows))
        {
            modifiers |= GameTextEntryModifierKeys.Windows;
        }
        return modifiers;
    }

    private static bool IsDown(VirtualKey key) =>
        (InputKeyboardSource.GetKeyStateForCurrentThread(key) & CoreVirtualKeyStates.Down) != 0;

    private static bool IsModifierKey(VirtualKey key) =>
        key is VirtualKey.Control or VirtualKey.Menu or VirtualKey.Shift or
            VirtualKey.LeftWindows or VirtualKey.RightWindows;

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

        if (HotkeyProfileToggle.IsOn &&
            (_enterGestures.Count == 0 || _exitGestures.Count == 0))
        {
            args.Cancel = true;
            ValidationBar.Message = _enterGestures.Count == 0
                ? "请点击“打开聊天”输入框，然后按下游戏中的聊天键。"
                : "请点击“发送 / 关闭”输入框，然后按下发送或取消键。";
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
            ExecutablePath = _recent.ExecutablePath ?? _existing?.ExecutablePath,
            Enabled = EnabledToggle.IsOn,
            DetectionMode = mode,
            ProviderId = target.ProviderId,
            Action = target.Action,
            EnterGestures = HotkeyProfileToggle.IsOn ? _enterGestures.ToArray() : [],
            ExitGestures = HotkeyProfileToggle.IsOn ? _exitGestures.ToArray() : []
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
