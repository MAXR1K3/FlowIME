using System.Runtime.InteropServices;
using FlowIME.App.Services;
using FlowIME.Core.Models;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;
using Windows.Graphics;
using Windows.System;
using Windows.UI.ViewManagement;
using WinRT.Interop;

namespace FlowIME.App.Windowing;

/// <summary>
/// Windows 11 style tray flyout: an acrylic panel that opens next to the tray
/// icon, offers the same actions as the tray menu and hides when it loses focus.
/// </summary>
public sealed partial class TrayFlyoutWindow : Window
{
    private const double FlyoutWidth = 328;
    private const uint DwmWindowCornerPreference = 33;
    private const int DwmCornerRound = 2;
    private const uint DwmBorderColor = 34;
    private const uint DwmTransitionsForceDisabled = 3;
    private const int DwmColorNone = unchecked((int)0xFFFFFFFE);
    private const uint MonitorDefaultToNearest = 2;
    private const int MdtEffectiveDpi = 0;
    private const int VkLeftButton = 0x01;
    private const int VkRightButton = 0x02;

    // A click on the tray icon first deactivates (hides) an open flyout; the
    // button-up that follows must not open it again.
    private static readonly TimeSpan ReopenGuard = TimeSpan.FromMilliseconds(300);

    private readonly nint _hwnd;
    private readonly AppWindow _appWindow;
    private readonly ApplicationIconLoader _iconLoader = new();
    private readonly UISettings _uiSettings = new();
    private readonly DispatcherTimer _foregroundWatch = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private nint _foregroundAtOpen;
    private TaskCompletionSource? _loaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private string? _iconPath;
    private long _hiddenAt;
    private PointInt32 _pointer;
    private bool _allowClose;

    public TrayFlyoutWindow()
    {
        InitializeComponent();

        _hwnd = WindowNative.GetWindowHandle(this);
        _appWindow = AppWindow.GetFromWindowId(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_hwnd))
            ?? throw new InvalidOperationException("The tray flyout could not be resolved as an AppWindow.");
        _appWindow.IsShownInSwitchers = false;
        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsResizable = false;
            presenter.IsAlwaysOnTop = true;
            presenter.SetBorderAndTitleBar(true, false);
        }

        var corner = DwmCornerRound;
        _ = DwmSetWindowAttribute(_hwnd, DwmWindowCornerPreference, ref corner, sizeof(int));

        // Flyouts keep a neutral edge; the system border would follow the
        // "accent colour on title bars" setting while the flyout is active.
        var border = DwmColorNone;
        _ = DwmSetWindowAttribute(_hwnd, DwmBorderColor, ref border, sizeof(int));

        // The flyout plays its own slide-in; the system window animation on
        // top of it would fade and scale the whole panel a second time.
        var disabled = 1;
        _ = DwmSetWindowAttribute(_hwnd, DwmTransitionsForceDisabled, ref disabled, sizeof(int));

        _appWindow.Closing += (_, args) =>
        {
            if (!_allowClose)
            {
                args.Cancel = true;
                Hide();
            }
        };
        Activated += OnActivated;
        _foregroundWatch.Tick += (_, _) => HideIfAnotherWindowTookOver();
        FlyoutRoot.Loaded += (_, _) => _loaded?.TrySetResult();
    }

    internal event Action<TrayCommand>? CommandInvoked;

    internal async Task ShowAtAsync(TrayMenuState state, PointInt32 pointer)
    {
        if (Environment.TickCount64 - _hiddenAt < ReopenGuard.TotalMilliseconds)
        {
            return;
        }

        if (_loaded is { } loaded)
        {
            // Lay the content out once off-screen so its height can be measured.
            _appWindow.MoveAndResize(new RectInt32(-32000, -32000, 1, 1));
            _appWindow.Show(false);
            await loaded.Task;
            _loaded = null;
        }

        Update(state);
        _pointer = pointer;
        var bounds = Layout();
        _appWindow.Show(true);
        Activate();
        _ = SetForegroundWindow(_hwnd);
        _foregroundAtOpen = GetForegroundWindow();

        // Clear "pressed since last call" so an old click cannot close the flyout.
        _ = IsPressed(VkLeftButton);
        _ = IsPressed(VkRightButton);
        _foregroundWatch.Start();
        PlayEntrance(opensUp: bounds.Y < pointer.Y);
        OpenButton.Focus(FocusState.Programmatic);

        await LoadTargetIconAsync(state.QuickRuleTarget?.Window.ExecutablePath);
    }

    /// <summary>
    /// Shows the new state in an open flyout, so an action taken from it is
    /// visible in place instead of the flyout just disappearing.
    /// </summary>
    internal void Refresh(TrayMenuState state)
    {
        if (!_appWindow.IsVisible)
        {
            return;
        }

        Update(state);
        _ = Layout();
    }

    // Sizes the window to the content and places it next to the tray pointer.
    private RectInt32 Layout()
    {
        var scale = GetScaleAt(_pointer);
        FlyoutRoot.Measure(new Size(FlyoutWidth, double.PositiveInfinity));
        var size = new SizeInt32(
            (int)Math.Ceiling(FlyoutWidth * scale),
            (int)Math.Ceiling(FlyoutRoot.DesiredSize.Height * scale));
        var workArea = DisplayArea.GetFromPoint(_pointer, DisplayAreaFallback.Nearest).WorkArea;
        var bounds = TrayFlyoutPlacement.Calculate(_pointer, size, workArea, scale);
        PlaceClientArea(bounds);
        return bounds;
    }

    // The window keeps a thin system frame (for the shadow and rounded
    // corners), so size and place the client area, not the outer rectangle;
    // otherwise the frame eats into the content on the right and bottom.
    // AppWindow.ResizeClient is not used: without a title bar it still adds
    // a caption's height.
    private void PlaceClientArea(RectInt32 bounds)
    {
        _appWindow.MoveAndResize(bounds);
        if (!GetWindowRect(_hwnd, out var outer) || !GetClientRect(_hwnd, out var client))
        {
            return;
        }

        var origin = new NativePoint();
        if (!ClientToScreen(_hwnd, ref origin))
        {
            return;
        }

        var frameWidth = (outer.Right - outer.Left) - (client.Right - client.Left);
        var frameHeight = (outer.Bottom - outer.Top) - (client.Bottom - client.Top);
        _appWindow.MoveAndResize(new RectInt32(
            bounds.X - (origin.X - outer.Left),
            bounds.Y - (origin.Y - outer.Top),
            bounds.Width + frameWidth,
            bounds.Height + frameHeight));
    }

    internal void CloseForExit()
    {
        _allowClose = true;
        Close();
    }

    internal void Hide()
    {
        if (!_appWindow.IsVisible)
        {
            return;
        }

        _foregroundWatch.Stop();
        _appWindow.Hide();
        _hiddenAt = Environment.TickCount64;
    }

    // Deactivation only fires if Windows let the flyout take the foreground.
    // When it did not, close as soon as the user switches window or clicks
    // anywhere outside the flyout.
    private void HideIfAnotherWindowTookOver()
    {
        var foreground = GetForegroundWindow();
        if (foreground != _hwnd && foreground != _foregroundAtOpen)
        {
            Hide();
            return;
        }

        if (foreground != _hwnd && (IsPressed(VkLeftButton) || IsPressed(VkRightButton)) &&
            GetCursorPos(out var cursor) && GetWindowRect(_hwnd, out var rect) &&
            (cursor.X < rect.Left || cursor.X >= rect.Right || cursor.Y < rect.Top || cursor.Y >= rect.Bottom))
        {
            Hide();
        }
    }

    // The low bit catches a click that started and ended between two ticks.
    private static bool IsPressed(int key) => (GetAsyncKeyState(key) & 0x8001) != 0;

    private void Update(TrayMenuState state)
    {
        StatusText.Text = TrayMenu.DescribeAutomation(state.AutomationEnabled, null);
        RunningDot.Visibility = state.AutomationEnabled ? Visibility.Visible : Visibility.Collapsed;
        PausedDot.Visibility = state.AutomationEnabled ? Visibility.Collapsed : Visibility.Visible;
        PausePanel.Visibility = state.AutomationEnabled ? Visibility.Visible : Visibility.Collapsed;
        ResumePanel.Visibility = state.AutomationEnabled ? Visibility.Collapsed : Visibility.Visible;
        ResumeHintText.Text = TrayMenu.DescribeResume(state.PausedUntil);

        var target = state.QuickRuleTarget;
        NoTargetText.Visibility = target is null ? Visibility.Visible : Visibility.Collapsed;
        TargetPanel.Visibility = target is null ? Visibility.Collapsed : Visibility.Visible;
        TargetLinks.Visibility = TargetPanel.Visibility;
        if (target is null)
        {
            return;
        }

        var rule = target.ExistingRule;
        var current = rule is { Enabled: true } ? rule.Action : (InputAction?)null;
        TargetNameText.Text = target.DisplayName;
        TargetRuleText.Text = TrayMenu.DescribeRule(rule);
        ChineseChoice.IsChecked = current == InputAction.Chinese;
        EnglishChoice.IsChecked = current == InputAction.English;
        KeepChoice.IsChecked = current == InputAction.Keep;
        RemoveRuleButton.Visibility = rule is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private async Task LoadTargetIconAsync(string? path)
    {
        if (string.Equals(path, _iconPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _iconPath = path;
        TargetIcon.Source = null;
        TargetFallbackIcon.Visibility = Visibility.Visible;
        if (path is null)
        {
            return;
        }

        var icon = await _iconLoader.LoadAsync(path);
        if (icon is not null && string.Equals(path, _iconPath, StringComparison.OrdinalIgnoreCase))
        {
            TargetIcon.Source = icon;
            TargetFallbackIcon.Visibility = Visibility.Collapsed;
        }
    }

    private void PlayEntrance(bool opensUp)
    {
        if (!_uiSettings.AnimationsEnabled)
        {
            FlyoutRoot.Opacity = 1;
            FlyoutOffset.Y = 0;
            return;
        }

        var easing = new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 6 };
        var slide = new DoubleAnimation
        {
            From = opensUp ? 24 : -24,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(267),
            EasingFunction = easing
        };
        var fade = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(167)
        };
        Storyboard.SetTarget(slide, FlyoutOffset);
        Storyboard.SetTargetProperty(slide, "Y");
        Storyboard.SetTarget(fade, FlyoutRoot);
        Storyboard.SetTargetProperty(fade, "Opacity");

        var storyboard = new Storyboard();
        storyboard.Children.Add(slide);
        storyboard.Children.Add(fade);
        storyboard.Begin();
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated)
        {
            Hide();
        }
    }

    private void FlyoutRoot_KeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key == VirtualKey.Escape)
        {
            args.Handled = true;
            Hide();
        }
    }

    private void QuickRuleChoice_Click(object sender, RoutedEventArgs args)
    {
        // Show the choice at once; Refresh then reports what was saved.
        foreach (var choice in new[] { ChineseChoice, EnglishChoice, KeepChoice })
        {
            choice.IsChecked = ReferenceEquals(choice, sender);
        }

        Command_Click(sender, args);
    }

    private void Command_Click(object sender, RoutedEventArgs args)
    {
        if (sender is FrameworkElement { Tag: string tag } &&
            Enum.TryParse<TrayCommand>(tag, out var command))
        {
            // Rule and pause changes keep the flyout open to show the result.
            if (command is TrayCommand.Open or TrayCommand.OpenRules or TrayCommand.Exit)
            {
                Hide();
            }

            CommandInvoked?.Invoke(command);
        }
    }

    private static double GetScaleAt(PointInt32 pointer)
    {
        var monitor = MonitorFromPoint(new NativePoint { X = pointer.X, Y = pointer.Y }, MonitorDefaultToNearest);
        return monitor != 0 && GetDpiForMonitor(monitor, MdtEffectiveDpi, out var dpiX, out _) == 0
            ? dpiX / 96d
            : 1d;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        internal int X;
        internal int Y;
    }

    [DllImport("user32.dll")]
    private static extern nint MonitorFromPoint(NativePoint point, uint flags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(nint monitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint hwnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(nint hwnd, out NativeRect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(nint hwnd, ref NativePoint point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint hwnd, out NativeRect rect);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, uint attribute, ref int value, int size);
}
