using FlowIME.Core.Abstractions;
using FlowIME.Core.Context;
using FlowIME.Core.Decisions;
using FlowIME.Core.Models;
using FlowIME.Core.Rules;

namespace FlowIME.App.Services;

public sealed class ForegroundContextService : IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly object _notificationSync = new();
    private readonly IForegroundWindowSource _foregroundSource;
    private readonly IInputFocusSource? _focusSource;
    private readonly IWindowResolver _windowResolver;
    private readonly IInputContextEngine _contextEngine;
    private readonly IInputDecisionEngine _decisionEngine;
    private readonly IRuleRepository _ruleRepository;
    private readonly IInputMethodBackend _inputBackend;
    private readonly uint _ignoredProcessId;
    private readonly TimeSpan _sampleDelay;

    private CancellationTokenSource? _currentCancellation;
    private Task _currentTask = Task.CompletedTask;
    private long _generation;
    private long _publicationSequence;
    private long _lastNotifiedSequence;
    private bool _started;
    private bool _disposed;

    public ForegroundContextService(
        IForegroundWindowSource foregroundSource,
        IWindowResolver windowResolver,
        IRuleEngine ruleEngine,
        IRuleRepository ruleRepository,
        IInputMethodBackend inputBackend,
        uint? ignoredProcessId = null,
        TimeSpan? sampleDelay = null)
        : this(
            foregroundSource,
            windowResolver,
            new InputContextEngine(),
            new InputDecisionEngine(ruleEngine ?? throw new ArgumentNullException(nameof(ruleEngine))),
            ruleRepository,
            inputBackend,
            ignoredProcessId,
            sampleDelay)
    {
    }

    public ForegroundContextService(
        IForegroundWindowSource foregroundSource,
        IWindowResolver windowResolver,
        IInputContextEngine contextEngine,
        IInputDecisionEngine decisionEngine,
        IRuleRepository ruleRepository,
        IInputMethodBackend inputBackend,
        uint? ignoredProcessId = null,
        TimeSpan? sampleDelay = null)
    {
        _foregroundSource = foregroundSource ?? throw new ArgumentNullException(nameof(foregroundSource));
        _focusSource = foregroundSource as IInputFocusSource;
        _windowResolver = windowResolver ?? throw new ArgumentNullException(nameof(windowResolver));
        _contextEngine = contextEngine ?? throw new ArgumentNullException(nameof(contextEngine));
        _decisionEngine = decisionEngine ?? throw new ArgumentNullException(nameof(decisionEngine));
        _ruleRepository = ruleRepository ?? throw new ArgumentNullException(nameof(ruleRepository));
        _inputBackend = inputBackend ?? throw new ArgumentNullException(nameof(inputBackend));
        _ignoredProcessId = ignoredProcessId ?? checked((uint)Environment.ProcessId);
        _sampleDelay = sampleDelay ?? TimeSpan.FromMilliseconds(125);
        if (_sampleDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleDelay));
        }
    }

    public event Action<CurrentStateSnapshot>? StateChanged;

    public CurrentStateSnapshot? Current { get; private set; }

    public void Start()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_started)
            {
                return;
            }

            _foregroundSource.ForegroundWindowChanged += OnForegroundWindowChanged;
            if (_focusSource is not null)
            {
                _focusSource.InputFocusChanged += OnInputFocusChanged;
            }
            _started = true;
        }

        RequestCurrentRefresh();
    }

    /// <summary>
    /// Re-samples the actual current foreground window. This is used after unlock
    /// or resume, where Windows may not emit a fresh foreground event for the app
    /// that was already active before the system transition.
    /// </summary>
    public void RequestCurrentRefresh(
        InputContextTrigger trigger = InputContextTrigger.ManualRefresh)
    {
        lock (_sync)
        {
            if (!_started || _disposed)
            {
                return;
            }
        }

        nint current;
        try
        {
            current = _foregroundSource.GetCurrentForegroundWindow();
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        if (current != 0)
        {
            Schedule(
                current,
                trigger,
                focusHwnd: 0,
                timestamp: DateTimeOffset.UtcNow);
        }
    }

    public async ValueTask RefreshRuleMatchAsync(
        CancellationToken cancellationToken = default)
    {
        CurrentStateSnapshot? current;
        long generation;
        lock (_sync)
        {
            current = Current;
            generation = _generation;
        }
        if (current is null)
        {
            return;
        }

        var configuration = await _ruleRepository
            .GetConfigurationAsync(cancellationToken)
            .ConfigureAwait(false);
        var context = current.Context ?? await _contextEngine
            .ResolveAsync(
                new ContextDetectionRequest(
                    current.Window,
                    InputContextTrigger.RuleChanged,
                    FocusHwnd: 0,
                    Timestamp: DateTimeOffset.UtcNow),
                cancellationToken)
            .ConfigureAwait(false);
        var decision = _decisionEngine.Resolve(context, configuration);
        var refreshed = current with
        {
            MatchedAction = decision.HasTarget ? decision.Action : null,
            MatchedRule = decision.ApplicationRule,
            MatchedProviderId = decision.ProviderId,
            ResolutionSource = MapResolutionSource(decision.Source),
            Context = context,
            Decision = decision
        };

        TryPublish(refreshed, generation, cancellationToken);
    }

    public async ValueTask WaitForIdleAsync(CancellationToken cancellationToken = default)
    {
        Task task;
        lock (_sync)
        {
            task = _currentTask;
        }

        await task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        CancellationTokenSource? cancellation;
        Task task;

        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _generation++;
            if (_started)
            {
                _foregroundSource.ForegroundWindowChanged -= OnForegroundWindowChanged;
                if (_focusSource is not null)
                {
                    _focusSource.InputFocusChanged -= OnInputFocusChanged;
                }
                _started = false;
            }

            cancellation = _currentCancellation;
            cancellation?.Cancel();
            task = _currentTask;
        }

        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            cancellation?.Dispose();
        }
    }

    private void OnForegroundWindowChanged(
        object? sender,
        ForegroundWindowChangedEventArgs args)
    {
        if (args.Hwnd != 0)
        {
            Schedule(
                args.Hwnd,
                InputContextTrigger.ForegroundChanged,
                args.Hwnd,
                args.Timestamp);
        }
    }

    private void OnInputFocusChanged(
        object? sender,
        InputFocusChangedEventArgs args)
    {
        if (args.Hwnd == 0)
        {
            return;
        }

        var foreground = _foregroundSource.GetCurrentForegroundWindow();
        if (foreground != 0)
        {
            Schedule(
                foreground,
                InputContextTrigger.FocusChanged,
                args.Hwnd,
                args.Timestamp,
                args.ObjectId,
                args.ChildId);
        }
    }

    private void Schedule(
        nint hwnd,
        InputContextTrigger trigger,
        nint focusHwnd,
        DateTimeOffset timestamp,
        int focusObjectId = 0,
        int focusChildId = 0)
    {
        CancellationTokenSource? previous;
        Task previousTask;
        var cancellation = new CancellationTokenSource();

        lock (_sync)
        {
            if (!_started || _disposed)
            {
                cancellation.Dispose();
                return;
            }

            previous = _currentCancellation;
            previousTask = _currentTask;
            previous?.Cancel();
            var generation = ++_generation;
            _currentCancellation = cancellation;
            _currentTask = ProcessAsync(
                hwnd,
                trigger,
                focusHwnd,
                timestamp,
                focusObjectId,
                focusChildId,
                generation,
                cancellation.Token);
        }

        if (previous is not null)
        {
            _ = DisposeWhenSafeAsync(previous, previousTask);
        }
    }

    private async Task ProcessAsync(
        nint hwnd,
        InputContextTrigger trigger,
        nint focusHwnd,
        DateTimeOffset timestamp,
        int focusObjectId,
        int focusChildId,
        long generation,
        CancellationToken cancellationToken)
    {
        try
        {
            if (_sampleDelay > TimeSpan.Zero)
            {
                await Task.Delay(_sampleDelay, cancellationToken).ConfigureAwait(false);
            }

            var window = await _windowResolver
                .ResolveAsync(hwnd, cancellationToken)
                .ConfigureAwait(false);
            if (window is null || window.ProcessId == _ignoredProcessId)
            {
                return;
            }

            var input = await _inputBackend
                .GetStateAsync(window, cancellationToken)
                .ConfigureAwait(false);
            var context = await _contextEngine
                .ResolveAsync(
                    new ContextDetectionRequest(
                        window,
                        trigger,
                        focusHwnd,
                        timestamp,
                        focusObjectId,
                        focusChildId),
                    cancellationToken)
                .ConfigureAwait(false);
            var configuration = await _ruleRepository
                .GetConfigurationAsync(cancellationToken)
                .ConfigureAwait(false);
            var decision = _decisionEngine.Resolve(context, configuration);
            var snapshot = new CurrentStateSnapshot(
                window,
                input,
                decision.HasTarget ? decision.Action : null,
                decision.ApplicationRule,
                decision.ProviderId,
                MapResolutionSource(decision.Source),
                context,
                decision);

            TryPublish(snapshot, generation, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void TryPublish(
        CurrentStateSnapshot snapshot,
        long generation,
        CancellationToken cancellationToken)
    {
        Action<CurrentStateSnapshot>? handler;
        long sequence;
        lock (_sync)
        {
            if (_disposed || generation != _generation || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            Current = snapshot;
            sequence = ++_publicationSequence;
            handler = StateChanged;
        }

        if (handler is null)
        {
            return;
        }

        lock (_notificationSync)
        {
            lock (_sync)
            {
                if (sequence <= _lastNotifiedSequence)
                {
                    return;
                }

                _lastNotifiedSequence = sequence;
            }

            handler(snapshot);
        }
    }

    private static RuleResolutionSource MapResolutionSource(InputDecisionSource source) =>
        source switch
        {
            InputDecisionSource.ContextPolicy => RuleResolutionSource.ContextPolicy,
            InputDecisionSource.ApplicationRule => RuleResolutionSource.ApplicationRule,
            InputDecisionSource.GlobalDefault => RuleResolutionSource.GlobalDefault,
            _ => RuleResolutionSource.None
        };

    private static async Task DisposeWhenSafeAsync(
        CancellationTokenSource cancellation,
        Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            cancellation.Dispose();
        }
    }
}
