using System.Diagnostics;
using FlowIME.App.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace FlowIME.App;

public partial class App : Application
{
    private readonly SingleInstanceCoordinator _singleInstance;
    private AppServices? _services;
    private MainWindow? _window;
    private TrayIconService? _trayIcon;
    private DispatcherQueue? _dispatcherQueue;
    private int _shutdownStarted;

    public App()
    {
        InitializeComponent();
        _singleInstance = new SingleInstanceCoordinator();
    }

    public AppServices Services =>
        _services ?? throw new InvalidOperationException(
            "FlowIME services are not available before the primary instance launches.");

    public MainWindow? MainWindow => _window;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var commandLineArguments = Environment.GetCommandLineArgs().Skip(1).ToArray();
        var startHidden = LaunchOptions.ShouldStartHidden(commandLineArguments);

        if (!_singleInstance.IsPrimary)
        {
            if (!startHidden)
            {
                _singleInstance.SignalPrimary();
            }

            _singleInstance.Dispose();
            Exit();
            return;
        }

        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        _services = new AppServices();
        _services.Start();

        _window = new MainWindow();
        _window.Closed += OnMainWindowClosed;

        _trayIcon = TryCreateTrayIcon();
        _window.CloseToTrayEnabled = _trayIcon is not null;

        _singleInstance.StartListening(() =>
        {
            _dispatcherQueue?.TryEnqueue(ShowMainWindow);
        });

        if (!startHidden || _trayIcon is null)
        {
            _window.Activate();
        }
    }

    private TrayIconService? TryCreateTrayIcon()
    {
        try
        {
            return new TrayIconService(
                openRequested: () => _dispatcherQueue?.TryEnqueue(ShowMainWindow),
                isAutomationEnabled: () => _services?.IsAutomationEnabled ?? false,
                toggleAutomationRequested: () => _dispatcherQueue?.TryEnqueue(() =>
                {
                    _ = ToggleAutomationAsync();
                }),
                systemRecoveryRequested: reason => _dispatcherQueue?.TryEnqueue(() =>
                {
                    _services?.RequestSystemRecovery(reason);
                }),
                exitRequested: () => _dispatcherQueue?.TryEnqueue(RequestExit));
        }
        catch (Exception ex)
        {
            Trace.WriteLine(
                $"[FlowIME.Lifecycle] stage=tray-init result=failed " +
                $"error={ex.GetType().Name}:{ex.Message}");
            return null;
        }
    }

    private async Task ToggleAutomationAsync()
    {
        if (_services is null || Volatile.Read(ref _shutdownStarted) != 0)
        {
            return;
        }

        await _services.SetAutomationEnabledAsync(!_services.IsAutomationEnabled);
    }

    private void ShowMainWindow()
    {
        if (_window is null || Volatile.Read(ref _shutdownStarted) != 0)
        {
            return;
        }

        _window.ShowAndActivate();
    }

    private void RequestExit()
    {
        if (_window is null)
        {
            _ = ShutdownAsync();
            return;
        }

        _window.RequestExit();
    }

    private async void OnMainWindowClosed(object sender, WindowEventArgs args)
    {
        if (_window is not null)
        {
            _window.Closed -= OnMainWindowClosed;
        }

        await ShutdownAsync().ConfigureAwait(true);
    }

    private async Task ShutdownAsync()
    {
        if (Interlocked.Exchange(ref _shutdownStarted, 1) != 0)
        {
            return;
        }

        _trayIcon?.Dispose();
        _trayIcon = null;

        _singleInstance.Dispose();

        if (_services is not null)
        {
            await _services.DisposeAsync();
            _services = null;
        }

        Exit();
    }
}
