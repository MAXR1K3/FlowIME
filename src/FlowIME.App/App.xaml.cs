using System.Diagnostics;
using FlowIME.App.Services;
using FlowIME.App.Windowing;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace FlowIME.App;

public partial class App : Application
{
    private readonly SingleInstanceCoordinator _singleInstance;
    private AppServices? _services;
    private MainWindow? _window;
    private TrayIconService? _trayIcon;
    private TrayFlyoutWindow? _trayFlyout;
    private DispatcherQueue? _dispatcherQueue;
    private DispatcherQueueTimer? _resumeTimer;
    private long _pausedUntilUtcTicks;
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

    // Read by the tray thread when it builds the menu, written on the UI thread.
    private DateTimeOffset? PausedUntil
    {
        get
        {
            var ticks = Volatile.Read(ref _pausedUntilUtcTicks);
            return ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
        }
        set => Volatile.Write(ref _pausedUntilUtcTicks, value?.UtcTicks ?? 0);
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var commandLineArguments = Environment.GetCommandLineArgs().Skip(1).ToArray();
        var startHidden = LaunchOptions.ShouldStartHidden(commandLineArguments);
        var requestExit = LaunchOptions.ShouldRequestExit(commandLineArguments);

        if (!_singleInstance.IsPrimary)
        {
            if (requestExit)
            {
                _singleInstance.SignalPrimaryExit();
            }
            else if (!startHidden)
            {
                _singleInstance.SignalPrimary();
            }

            _singleInstance.Dispose();
            Exit();
            return;
        }

        if (requestExit)
        {
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

        _singleInstance.StartListening(
            () => _dispatcherQueue?.TryEnqueue(ShowMainWindow),
            () => _dispatcherQueue?.TryEnqueue(RequestExit));

        if (!startHidden || _trayIcon is null)
        {
            _window.Activate();
        }
    }

    private TrayIconService? TryCreateTrayIcon()
    {
        try
        {
            var trayIcon = new TrayIconService(
                openRequested: () => _dispatcherQueue?.TryEnqueue(ShowMainWindow),
                systemRecoveryRequested: reason => _dispatcherQueue?.TryEnqueue(() =>
                {
                    _services?.RequestSystemRecovery(reason);
                }),
                menuItems: () => TrayMenu.Build(CurrentTrayState()),
                commandRequested: command => _dispatcherQueue?.TryEnqueue(() => OnTrayCommand(command)),
                flyoutRequested: (x, y) => _dispatcherQueue?.TryEnqueue(
                    () => _ = ShowTrayFlyoutAsync(new PointInt32(x, y))) ?? false);

            if (_services is not null)
            {
                _services.AutomationEnabledChanged += enabled =>
                    _dispatcherQueue?.TryEnqueue(() => OnAutomationEnabledChanged(enabled));
            }

            trayIcon.SetTooltip(TrayMenu.DescribeStatus(_services?.IsAutomationEnabled ?? false, null));
            return trayIcon;
        }
        catch (Exception ex)
        {
            Trace.WriteLine(
                $"[FlowIME.Lifecycle] stage=tray-init result=failed " +
                $"error={ex.GetType().Name}:{ex.Message}");
            return null;
        }
    }

    private TrayMenuState CurrentTrayState() =>
        new(_services?.IsAutomationEnabled ?? false, PausedUntil, _services?.QuickRuleTargets.Current);

    private async Task ShowTrayFlyoutAsync(PointInt32 pointer)
    {
        if (Volatile.Read(ref _shutdownStarted) != 0)
        {
            return;
        }

        try
        {
            if (_trayFlyout is null)
            {
                _trayFlyout = new TrayFlyoutWindow();
                _trayFlyout.CommandInvoked += command => _ = RunFlyoutCommandAsync(command);
            }

            await _trayFlyout.ShowAtAsync(CurrentTrayState(), pointer);
        }
        catch (Exception ex)
        {
            Trace.WriteLine(
                $"[FlowIME.Lifecycle] stage=tray-flyout result=failed " +
                $"error={ex.GetType().Name}:{ex.Message}");
            _trayIcon?.ShowMenu();
        }
    }

    private async Task RunFlyoutCommandAsync(TrayCommand command)
    {
        await RunTrayCommandAsync(command, notifyQuickRule: false);
        _trayFlyout?.Refresh(CurrentTrayState());
    }

    private async Task ApplyQuickRuleAsync(QuickRuleChoice choice, bool notifySuccess)
    {
        if (_services is null || Volatile.Read(ref _shutdownStarted) != 0)
        {
            return;
        }

        var result = await _services.ApplyQuickRuleAsync(choice);
        // The flyout shows a saved rule itself; failures are always announced.
        if (result is not null && (notifySuccess || result.Failed))
        {
            _trayIcon?.ShowNotification(result.Title, result.Message);
        }
    }

    private void OnTrayCommand(TrayCommand command) =>
        _ = RunTrayCommandAsync(command, notifyQuickRule: true);

    private async Task RunTrayCommandAsync(TrayCommand command, bool notifyQuickRule)
    {
        switch (command)
        {
            case TrayCommand.Open:
                ShowMainWindow();
                break;
            case TrayCommand.OpenRules:
                ShowMainWindow();
                _window?.NavigateToSection("rules");
                break;
            case TrayCommand.PauseFifteenMinutes:
            case TrayCommand.PauseOneHour:
            case TrayCommand.PauseUntilResumed:
                await PauseAutomationAsync(TrayMenu.PauseDurationFor(command));
                break;
            case TrayCommand.Resume:
                await SetAutomationEnabledAsync(true);
                break;
            case TrayCommand.Exit:
                RequestExit();
                break;
            default:
                if (TrayMenu.ChoiceFor(command) is { } choice)
                {
                    await ApplyQuickRuleAsync(choice, notifyQuickRule);
                }

                break;
        }
    }

    private async Task PauseAutomationAsync(TimeSpan? duration)
    {
        if (!await SetAutomationEnabledAsync(false))
        {
            return;
        }

        PausedUntil = duration is { } value ? DateTimeOffset.UtcNow + value : null;
        if (PausedUntil is not null && _dispatcherQueue is not null)
        {
            // A short repeating check instead of one long timer, so a pause that
            // spans sleep or hibernation still ends on the wall-clock deadline.
            _resumeTimer ??= CreateResumeTimer(_dispatcherQueue);
            _resumeTimer.Start();
        }

        UpdateTrayStatus();
    }

    private DispatcherQueueTimer CreateResumeTimer(DispatcherQueue dispatcherQueue)
    {
        var timer = dispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(15);
        timer.IsRepeating = true;
        timer.Tick += (_, _) =>
        {
            if (PausedUntil is { } until && DateTimeOffset.UtcNow >= until)
            {
                _ = SetAutomationEnabledAsync(true);
            }
        };
        return timer;
    }

    private async Task<bool> SetAutomationEnabledAsync(bool enabled)
    {
        if (_services is null || Volatile.Read(ref _shutdownStarted) != 0)
        {
            return false;
        }

        try
        {
            await _services.SetAutomationEnabledAsync(enabled);
            return true;
        }
        catch (Exception ex)
        {
            Trace.WriteLine(
                $"[FlowIME.Lifecycle] stage=tray-automation enabled={enabled} result=failed " +
                $"error={ex.GetType().Name}:{ex.Message}");
            return false;
        }
    }

    private void OnAutomationEnabledChanged(bool enabled)
    {
        // Resuming from anywhere (tray, home page, timer) ends a timed pause.
        if (enabled)
        {
            PausedUntil = null;
            _resumeTimer?.Stop();
        }

        UpdateTrayStatus();
    }

    private void UpdateTrayStatus()
    {
        _trayIcon?.SetTooltip(TrayMenu.DescribeStatus(_services?.IsAutomationEnabled ?? false, PausedUntil));
        _trayFlyout?.Refresh(CurrentTrayState());
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
        _trayFlyout?.CloseForExit();
        _trayFlyout = null;

        if (_services is not null)
        {
            await _services.DisposeAsync();
            _services = null;
        }

        // Keep ownership until all application services have released files,
        // hooks and user-data writers. A replacement instance cannot become
        // primary while teardown is still in progress.
        _singleInstance.Dispose();

        Exit();
    }
}
