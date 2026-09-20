using System.Runtime.InteropServices;
using FlowIME.App.Windowing;
using FlowIME.App.Views;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinRT.Interop;
using Windows.Graphics;

namespace FlowIME.App;

public sealed partial class MainWindow : Window
{
    private readonly nint _hwnd;
    private readonly AppWindow _appWindow;
    private bool _allowClose;
    private bool _enforcingWindowSize;
    private string? _currentNavigationTag;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        _hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId)
            ?? throw new InvalidOperationException("The WinUI window could not be resolved as an AppWindow.");
        _appWindow.SetIcon(Path.Combine(
            AppContext.BaseDirectory,
            "Assets",
            "Brand",
            "Generated",
            "FlowIME.ico"));
        _appWindow.Closing += OnAppWindowClosing;
        _appWindow.Changed += OnAppWindowChanged;
        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMaximizable = false;
            presenter.IsResizable = true;
        }
        ApplyInitialWindowBounds(windowId);

        HomeNavItem.IsSelected = true;
        NavigateTo("home");
    }

    internal bool CloseToTrayEnabled { get; set; }

    private void ApplyInitialWindowBounds(Microsoft.UI.WindowId windowId)
    {
        var dpi = GetDpiForWindow(_hwnd);
        var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
        var workArea = displayArea.WorkArea;
        var preferred = MainWindowSizePolicy.Preferred(dpi);
        var clamped = MainWindowSizePolicy.Clamp(
            preferred.Width,
            preferred.Height,
            workArea.Width,
            workArea.Height,
            dpi);
        var width = clamped.Width;
        var height = clamped.Height;
        var x = workArea.X + Math.Max(0, (workArea.Width - width) / 2);
        var y = workArea.Y + Math.Max(0, (workArea.Height - height) / 2);

        _appWindow.MoveAndResize(new RectInt32(x, y, width, height));
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (!args.DidSizeChange || _enforcingWindowSize)
        {
            return;
        }

        var displayArea = DisplayArea.GetFromWindowId(sender.Id, DisplayAreaFallback.Nearest);
        var workArea = displayArea.WorkArea;
        var current = sender.Size;
        var clamped = MainWindowSizePolicy.Clamp(
            current.Width,
            current.Height,
            workArea.Width,
            workArea.Height,
            GetDpiForWindow(_hwnd));
        if (current.Width == clamped.Width && current.Height == clamped.Height)
        {
            return;
        }

        _enforcingWindowSize = true;
        try
        {
            _appWindow.Resize(new SizeInt32(clamped.Width, clamped.Height));
        }
        finally
        {
            _enforcingWindowSize = false;
        }
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hwnd);

    internal void ShowAndActivate() =>
        _appWindow.Show(true);

    internal void HideToTray() =>
        _appWindow.Hide();

    internal void NavigateToSection(string tag)
    {
        var item = tag switch
        {
            "home" => HomeNavItem,
            "rules" => RulesNavItem,
            "game" => GameNavItem,
            _ => null
        };

        if (item is not null && !ReferenceEquals(NavView.SelectedItem, item))
        {
            NavView.SelectedItem = item;
            return;
        }

        NavigateTo(tag);
    }

    internal void RequestExit()
    {
        _allowClose = true;
        _appWindow.Changed -= OnAppWindowChanged;
        _appWindow.Closing -= OnAppWindowClosing;
        Close();
    }

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_allowClose || !CloseToTrayEnabled)
        {
            return;
        }

        args.Cancel = true;
        HideToTray();
    }

    private void ShellRoot_SizeChanged(object sender, SizeChangedEventArgs args)
    {
        AppTitleBar.Subtitle = args.NewSize.Width >= 680 ? "让输入状态跟随你的使用场景" : string.Empty;
        UpdateNavigationInset();
    }

    private void NavView_DisplayModeChanged(NavigationView sender, NavigationViewDisplayModeChangedEventArgs args) =>
        UpdateNavigationInset();

    private void UpdateNavigationInset()
    {
        // Minimal navigation overlays a 48-DIP menu button. Reserve its row outside
        // the page ScrollViewer so scrolling/focus can never run underneath it.
        if (ContentFrame is not null)
            ContentFrame.Margin = NavView.DisplayMode == NavigationViewDisplayMode.Minimal
                ? new Thickness(0, 48, 0, 0) : new Thickness(0);
    }

    private void NavView_SelectionChanged(
        NavigationView sender,
        NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            NavigateTo("settings");
            return;
        }

        if (args.SelectedItemContainer?.Tag is string tag)
        {
            NavigateTo(tag);
        }
    }

    private void NavigateTo(string tag)
    {
        var pageType = tag switch
        {
            "home" => typeof(HomePage),
            "rules" => typeof(RulesPage),
            "game" => typeof(SettingsPage),
            "settings" => typeof(SettingsPage),
            "about" => typeof(AboutPage),
            _ => typeof(HomePage)
        };

        var parameter = tag switch
        {
            "game" => "game",
            "settings" => "settings",
            _ => null
        };

        if (ContentFrame.CurrentSourcePageType == pageType &&
            string.Equals(_currentNavigationTag, tag, StringComparison.Ordinal))
        {
            return;
        }

        ContentFrame.Navigate(pageType, parameter);
        ContentFrame.BackStack.Clear();
        _currentNavigationTag = tag;
    }

}
