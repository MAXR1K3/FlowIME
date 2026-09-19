using System.Diagnostics;
using FlowIME.Core.Abstractions;
using FlowIME.Core.Context;
using FlowIME.Core.Decisions;
using FlowIME.Core.Models;
using FlowIME.Core.Rules;

namespace FlowIME.Core.Automation;

/// <summary>
/// Coordinates foreground/focus events with application rules using a
/// last-event-wins generation model.
///
/// Every valid foreground or input-focus notification creates a new generation.
/// The previous generation is cancelled immediately. The operation never trusts
/// the HWND captured by a stale WinEvent callback: after debounce and before each
/// bounded retry it re-reads the actual foreground HWND, resolves a fresh window
/// context, then delegates to the input backend. This prevents a delayed A-window
/// task from mutating B after a rapid Alt+Tab/focus transition.
/// </summary>
public sealed class AutomationCoordinator : IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly IForegroundWindowSource _foregroundSource;
    private readonly IInputFocusSource? _focusSource;
    private readonly IWindowResolver _windowResolver;
    private readonly IRuleEngine _ruleEngine;
    private readonly IRuleRepository _ruleRepository;
    private readonly IInputMethodBackend _inputBackend;
    private readonly IInputContextEngine _contextEngine;
    private readonly IInputDecisionEngine _decisionEngine;
    private readonly ManualOverrideGuard _manualOverrideGuard;
    private readonly AutomationDecisionJournal _decisionJournal;
    private readonly AutomationOptions _options;
    private readonly IAutomationDelay _delay;
    private readonly uint _ignoredProcessId;

    private Operation? _currentOperation;
    private long _generation;
    private bool _executionEnabled = true;
    private bool _started;
    private bool _disposed;

    public AutomationCoordinator(
        IForegroundWindowSource foregroundSource,
        IWindowResolver windowResolver,
        IRuleEngine ruleEngine,
        IRuleRepository ruleRepository,
        IInputMethodBackend inputBackend,
        AutomationOptions? options = null,
        IAutomationDelay? delay = null,
        IInputFocusSource? inputFocusSource = null,
        uint ignoredProcessId = 0,
        IInputContextEngine? contextEngine = null,
        IInputDecisionEngine? decisionEngine = null,
        ManualOverrideGuard? manualOverrideGuard = null,
        AutomationDecisionJournal? decisionJournal = null)
    {
        _foregroundSource = foregroundSource ??
            throw new ArgumentNullException(nameof(foregroundSource));
        _focusSource = inputFocusSource ?? foregroundSource as IInputFocusSource;
        _windowResolver = windowResolver ??
            throw new ArgumentNullException(nameof(windowResolver));
        _ruleEngine = ruleEngine ??
            throw new ArgumentNullException(nameof(ruleEngine));
        _ruleRepository = ruleRepository ??
            throw new ArgumentNullException(nameof(ruleRepository));
        _inputBackend = inputBackend ??
            throw new ArgumentNullException(nameof(inputBackend));
        _contextEngine = contextEngine ?? new InputContextEngine();
        _decisionEngine = decisionEngine ?? new InputDecisionEngine(_ruleEngine);
        _manualOverrideGuard = manualOverrideGuard ?? new ManualOverrideGuard();
        _decisionJournal = decisionJournal ?? new AutomationDecisionJournal();
        _options = options ?? new AutomationOptions();
        _options.Validate();
        _delay = delay ?? SystemAutomationDelay.Instance;
        _ignoredProcessId = ignoredProcessId;
    }

    private Exception? _lastError;

    /// <summary>
    /// Last unexpected coordinator exception. Expected cancellation caused by a
    /// newer foreground/focus event is not reported here.
    /// </summary>
    public Exception? LastError
    {
        get
        {
            lock (_sync)
            {
                return _lastError;
            }
        }
    }

    public AutomationRuntimeSnapshot GetRuntimeSnapshot()
    {
        var lastDecision = _decisionJournal.GetRecent(1).FirstOrDefault();
        lock (_sync)
        {
            return new AutomationRuntimeSnapshot(
                Started: _started,
                ExecutionEnabled: _executionEnabled,
                Generation: _generation,
                OperationInFlight: _currentOperation is not null && !_currentOperation.Task.IsCompleted,
                LastErrorType: _lastError?.GetType().Name,
                LastErrorMessage: _lastError?.Message,
                DecisionJournalCount: _decisionJournal.Count,
                LastDecisionOutcome: lastDecision?.Outcome.ToString(),
                LastDecisionReason: lastDecision?.Reason);
        }
    }

    public IReadOnlyList<AutomationDecisionRecord> GetRecentDecisions(int maxCount = 20) =>
        _decisionJournal.GetRecent(maxCount);

    public event Action<AutomationDecisionRecord>? DecisionRecorded;

    public ManualOverrideSnapshot RegisterManualOverride(
        InputContextSnapshot context,
        InputMode mode,
        string source,
        TimeSpan? duration = null) =>
        _manualOverrideGuard.Register(context, mode, source, duration);

    public void ClearManualOverride() => _manualOverrideGuard.Clear();

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
    }

    public async ValueTask StopAsync()
    {
        Operation? operation;

        lock (_sync)
        {
            if (!_started)
            {
                return;
            }

            _foregroundSource.ForegroundWindowChanged -= OnForegroundWindowChanged;
            if (_focusSource is not null)
            {
                _focusSource.InputFocusChanged -= OnInputFocusChanged;
            }

            _started = false;
            _generation++;
            operation = _currentOperation;
            operation?.Cancellation.Cancel();
        }

        if (operation is not null)
        {
            await operation.Task.ConfigureAwait(false);

            lock (_sync)
            {
                if (ReferenceEquals(_currentOperation, operation))
                {
                    _currentOperation = null;
                }
            }

            operation.Cancellation.Dispose();
        }
    }


    /// <summary>
    /// Enables or pauses rule execution while keeping the foreground/focus WinEvent
    /// subscriptions alive. Pausing cancels the in-flight generation. Resuming
    /// immediately schedules the current foreground application so the user does
    /// not need to Alt+Tab before rules become active again.
    /// </summary>
    public async ValueTask SetExecutionEnabledAsync(bool enabled)
    {
        Operation? cancelledOperation = null;
        var shouldReapply = false;

        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_executionEnabled == enabled)
            {
                return;
            }

            _executionEnabled = enabled;
            _generation++;

            if (!enabled)
            {
                cancelledOperation = _currentOperation;
                _currentOperation = null;
                cancelledOperation?.Cancellation.Cancel();
            }
            else
            {
                shouldReapply = _started;
            }
        }

        if (cancelledOperation is not null)
        {
            try
            {
                await cancelledOperation.Task.ConfigureAwait(false);
            }
            finally
            {
                cancelledOperation.Cancellation.Dispose();
            }
        }

        Trace.WriteLine(
            $"[FlowIME.Automation] utc={DateTimeOffset.UtcNow:O} " +
            $"stage=execution-gate enabled={enabled}");

        if (shouldReapply)
        {
            RequestCurrentReapply();
        }
    }

    /// <summary>
    /// Re-evaluates the currently foreground application without changing hook
    /// registration. No-op while execution is paused or the coordinator is stopped.
    /// </summary>
    public void RequestCurrentReapply(
        InputContextTrigger trigger = InputContextTrigger.ManualRefresh)
    {
        lock (_sync)
        {
            if (!_started || _disposed || !_executionEnabled)
            {
                return;
            }
        }

        nint foreground;
        try
        {
            foreground = _foregroundSource.GetCurrentForegroundWindow();
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        if (foreground != 0)
        {
            RequestReapply(
                trigger,
                foreground,
                focusHwnd: 0,
                timestamp: DateTimeOffset.UtcNow,
                focusObjectId: 0,
                focusChildId: 0);
        }
    }

    /// <summary>
    /// Waits until the most recently scheduled operation completes. If a newer
    /// foreground/focus event replaces it while waiting, follows the newer one.
    /// </summary>
    public async ValueTask WaitForIdleAsync(
        CancellationToken cancellationToken = default)
    {
        while (true)
        {
            Operation? operation;
            lock (_sync)
            {
                operation = _currentOperation;
            }

            if (operation is null)
            {
                return;
            }

            await operation.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

            lock (_sync)
            {
                if (ReferenceEquals(operation, _currentOperation))
                {
                    return;
                }
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }
        }

        await StopAsync().ConfigureAwait(false);

        lock (_sync)
        {
            _disposed = true;
        }
    }

    private void OnForegroundWindowChanged(
        object? sender,
        ForegroundWindowChangedEventArgs args)
    {
        if (args.Hwnd == 0)
        {
            return;
        }

        _manualOverrideGuard.NotifyForegroundChanged(args.Hwnd);
        RequestReapply(
            InputContextTrigger.ForegroundChanged,
            args.Hwnd,
            args.Hwnd,
            args.Timestamp,
            focusObjectId: 0,
            focusChildId: 0);
    }

    private void OnInputFocusChanged(
        object? sender,
        InputFocusChangedEventArgs args)
    {
        if (args.Hwnd == 0)
        {
            return;
        }

        nint foreground;
        try
        {
            // EVENT_OBJECT_FOCUS often identifies a child/accessibility HWND. Rules
            // match the top-level application, so use the current foreground HWND
            // only as the expected application identity and resolve it again later.
            foreground = _foregroundSource.GetCurrentForegroundWindow();
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        if (foreground == 0)
        {
            return;
        }

        RequestReapply(
            InputContextTrigger.FocusChanged,
            foreground,
            args.Hwnd,
            args.Timestamp,
            args.ObjectId,
            args.ChildId);
    }

    private void RequestReapply(
        InputContextTrigger trigger,
        nint expectedForegroundHwnd,
        nint focusHwnd,
        DateTimeOffset timestamp,
        int focusObjectId = 0,
        int focusChildId = 0)
    {
        Operation? previous;
        var cancellation = new CancellationTokenSource();
        long generation;
        Task task;

        lock (_sync)
        {
            if (!_started || _disposed || !_executionEnabled)
            {
                cancellation.Dispose();
                return;
            }

            generation = ++_generation;
            _lastError = null;
            previous = _currentOperation;
            previous?.Cancellation.Cancel();

            task = RunSafelyAsync(
                generation,
                trigger,
                expectedForegroundHwnd,
                focusHwnd,
                timestamp,
                focusObjectId,
                focusChildId,
                cancellation.Token);
            _currentOperation = new Operation(cancellation, task);
        }

        TraceLine(
            generation,
            trigger,
            "scheduled",
            expectedForegroundHwnd,
            attempt: 0,
            details: previous is null ? "replaced=false" : "replaced=true");

        if (previous is not null)
        {
            _ = DisposeAfterCompletionAsync(previous);
        }
    }

    private async Task RunSafelyAsync(
        long generation,
        InputContextTrigger trigger,
        nint expectedForegroundHwnd,
        nint focusHwnd,
        DateTimeOffset timestamp,
        int focusObjectId,
        int focusChildId,
        CancellationToken cancellationToken)
    {
        try
        {
            await ProcessRequestAsync(
                    generation,
                    trigger,
                    expectedForegroundHwnd,
                    focusHwnd,
                    timestamp,
                    focusObjectId,
                    focusChildId,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TraceLine(
                generation,
                trigger,
                "cancelled",
                expectedForegroundHwnd,
                attempt: 0,
                details: "reason=superseded-or-stopped");
        }
        catch (Exception ex)
        {
            lock (_sync)
            {
                if (_started && !_disposed && generation == _generation)
                {
                    _lastError = ex;
                }
            }

            TraceLine(
                generation,
                trigger,
                "exception",
                expectedForegroundHwnd,
                attempt: 0,
                details: $"type={ex.GetType().Name} " +
                         $"hresult=0x{unchecked((uint)ex.HResult):X8} " +
                         $"message={SanitizeTraceValue(ex.Message)}");
        }
    }

    private async Task ProcessRequestAsync(
        long generation,
        InputContextTrigger trigger,
        nint expectedForegroundHwnd,
        nint focusHwnd,
        DateTimeOffset timestamp,
        int focusObjectId,
        int focusChildId,
        CancellationToken cancellationToken)
    {
        await DelayIfNeededAsync(
                _options.ForegroundDebounce,
                cancellationToken)
            .ConfigureAwait(false);

        for (var attempt = 1; attempt <= _options.MaxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsCurrentGeneration(generation))
            {
                return;
            }

            var foregroundHwnd = _foregroundSource.GetCurrentForegroundWindow();
            if (foregroundHwnd == 0 || foregroundHwnd != expectedForegroundHwnd)
            {
                TraceLine(
                    generation,
                    trigger,
                    "stale",
                    foregroundHwnd,
                    attempt,
                    $"expected=0x{expectedForegroundHwnd:X}");
                return;
            }

            // Resolve on every attempt. Window title/class/process metadata can
            // change while a UI is settling, and a retry must never reuse stale
            // application context from the preceding native event.
            var window = await _windowResolver
                .ResolveAsync(foregroundHwnd, cancellationToken)
                .ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            if (!IsCurrentGeneration(generation) ||
                _foregroundSource.GetCurrentForegroundWindow() != foregroundHwnd)
            {
                TraceLine(
                    generation,
                    trigger,
                    "stale-after-resolve",
                    foregroundHwnd,
                    attempt,
                    "reason=foreground-or-generation-changed");
                return;
            }

            if (window is null)
            {
                TraceLine(
                    generation,
                    trigger,
                    "resolve-failed",
                    foregroundHwnd,
                    attempt,
                    "window=null");

                if (!await DelayBeforeRetryAsync(attempt, cancellationToken)
                        .ConfigureAwait(false))
                {
                    return;
                }

                continue;
            }

            if (_ignoredProcessId != 0 && window.ProcessId == _ignoredProcessId)
            {
                TraceLine(
                    generation,
                    trigger,
                    "ignored-process",
                    foregroundHwnd,
                    attempt,
                    $"process={window.ProcessName} pid={window.ProcessId}");
                return;
            }

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
            if (!decision.HasTarget || decision.Action == InputAction.Keep)
            {
                RecordDecision(
                    generation,
                    context,
                    decision,
                    AutomationDecisionOutcome.NoAction);
                TraceLine(
                    generation,
                    trigger,
                    "no-action",
                    foregroundHwnd,
                    attempt,
                    $"process={window.ProcessName} source={decision.Source} " +
                    $"reason={decision.Reason} action={decision.Action}");
                return;
            }

            var overrideEvaluation = _manualOverrideGuard.Evaluate(context, decision);
            if (overrideEvaluation.Suppress)
            {
                RecordDecision(
                    generation,
                    context,
                    decision,
                    AutomationDecisionOutcome.SuppressedByManualOverride);
                TraceLine(
                    generation,
                    trigger,
                    "manual-override",
                    foregroundHwnd,
                    attempt,
                    $"process={window.ProcessName} source={decision.Source} " +
                    $"reason={overrideEvaluation.Reason} action={decision.Action}");
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (!IsCurrentGeneration(generation) ||
                _foregroundSource.GetCurrentForegroundWindow() != foregroundHwnd)
            {
                return;
            }

            var providerId = InputMethodProviderIds.Normalize(decision.ProviderId);
            var result = await _inputBackend
                .ApplyAsync(window, providerId, decision.Action, cancellationToken)
                .ConfigureAwait(false);

            RecordDecision(
                generation,
                context,
                decision,
                result.Success
                    ? AutomationDecisionOutcome.Applied
                    : AutomationDecisionOutcome.ApplyFailed,
                result.ErrorCode,
                result);

            TraceLine(
                generation,
                trigger,
                result.Success ? "applied" : "apply-failed",
                foregroundHwnd,
                attempt,
                $"process={window.ProcessName} pid={window.ProcessId} " +
                $"app={context.Application.Key} source={decision.Source} " +
                $"reason={decision.Reason} rule={decision.ApplicationRule?.Id} " +
                $"policy={decision.ContextPolicyId ?? "none"} " +
                $"provider={providerId} action={decision.Action} " +
                $"thread={window.ThreadId} backend={SanitizeTraceValue(result.Backend)} " +
                $"beforeProfile={SanitizeTraceValue(result.Before.ProfileName)} " +
                $"beforeMode={result.Before.Mode} beforeHkl={FormatKeyboardLayout(result.Before.KeyboardLayout)} " +
                $"afterProfile={SanitizeTraceValue(result.After.ProfileName)} " +
                $"afterMode={result.After.Mode} afterHkl={FormatKeyboardLayout(result.After.KeyboardLayout)} " +
                $"durationMs={result.Duration.TotalMilliseconds:F1} " +
                $"error={SanitizeTraceValue(result.ErrorCode)}");

            if (result.Success)
            {
                return;
            }

            if (!IsRetryableApplyFailure(result.ErrorCode))
            {
                return;
            }

            if (!await DelayBeforeRetryAsync(attempt, cancellationToken)
                    .ConfigureAwait(false))
            {
                return;
            }
        }
    }

    private async ValueTask<bool> DelayBeforeRetryAsync(
        int completedAttempt,
        CancellationToken cancellationToken)
    {
        if (completedAttempt >= _options.MaxAttempts)
        {
            return false;
        }

        await DelayIfNeededAsync(
                _options.GetRetryDelay(completedAttempt),
                cancellationToken)
            .ConfigureAwait(false);
        return true;
    }

    private bool IsCurrentGeneration(long generation)
    {
        lock (_sync)
        {
            return _started && !_disposed && _executionEnabled && generation == _generation;
        }
    }

    private ValueTask DelayIfNeededAsync(
        TimeSpan delay,
        CancellationToken cancellationToken) =>
        delay == TimeSpan.Zero
            ? ValueTask.CompletedTask
            : _delay.DelayAsync(delay, cancellationToken);

    private static async Task DisposeAfterCompletionAsync(Operation operation)
    {
        try
        {
            await operation.Task.ConfigureAwait(false);
        }
        finally
        {
            operation.Cancellation.Dispose();
        }
    }

    private void RecordDecision(
        long generation,
        InputContextSnapshot context,
        InputDecision decision,
        AutomationDecisionOutcome outcome,
        string? errorCode = null,
        InputOperationResult? operationResult = null)
    {
        var record = new AutomationDecisionRecord(
            Timestamp: DateTimeOffset.UtcNow,
            Generation: generation,
            Trigger: context.Trigger,
            Hwnd: context.Window.Hwnd,
            ApplicationKey: context.Application.Key,
            ProcessName: context.Window.ProcessName,
            Signals: context.Signals
                .Select(signal => signal.Kind)
                .Distinct()
                .ToArray(),
            DecisionSource: decision.Source,
            ReasonCode: decision.ReasonCode,
            Reason: decision.Reason,
            ProviderId: decision.ProviderId,
            Action: decision.Action,
            Outcome: outcome,
            RuleId: decision.ApplicationRule?.Id,
            ContextPolicyId: decision.ContextPolicyId,
            ErrorCode: errorCode,
            TargetThreadId: context.Window.ThreadId,
            Backend: operationResult?.Backend,
            BeforeState: operationResult?.Before,
            AfterState: operationResult?.After,
            Duration: operationResult?.Duration);

        _decisionJournal.Add(record);
        try
        {
            DecisionRecorded?.Invoke(record);
        }
        catch (Exception ex)
        {
            Trace.WriteLine(
                $"[FlowIME.Automation] utc={DateTimeOffset.UtcNow:O} " +
                $"stage=decision-observer-failed type={ex.GetType().Name}");
        }
    }

    private static void TraceLine(
        long generation,
        InputContextTrigger trigger,
        string stage,
        nint foregroundHwnd,
        int attempt,
        string details)
    {
        Trace.WriteLine(
            $"[FlowIME.Automation] utc={DateTimeOffset.UtcNow:O} " +
            $"generation={generation} trigger={trigger} stage={stage} " +
            $"foreground=0x{foregroundHwnd:X} attempt={attempt} {details}");
    }

    private static string FormatKeyboardLayout(nint keyboardLayout) =>
        keyboardLayout == 0
            ? "none"
            : $"0x{unchecked((uint)(nuint)keyboardLayout):X8}";

    private static string SanitizeTraceValue(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? "none"
            : value.Replace('\r', ' ').Replace('\n', ' ').Replace(' ', '_');

    private static bool IsRetryableApplyFailure(string? errorCode) =>
        errorCode is not (
            "input-context-unavailable" or
            "input-context-inaccessible" or
            "provider-not-found");


    private sealed record Operation(
        CancellationTokenSource Cancellation,
        Task Task);
}
