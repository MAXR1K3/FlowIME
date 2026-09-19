using FlowIME.Core.Context;
using FlowIME.Core.Decisions;
using FlowIME.Core.Models;

namespace FlowIME.Core.Automation;

public sealed record ManualOverrideOptions
{
    public TimeSpan DefaultDuration { get; init; } = TimeSpan.FromSeconds(20);

    internal void Validate()
    {
        if (DefaultDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(DefaultDuration),
                "Manual override duration must be positive.");
        }
    }
}

public sealed record ManualOverrideSnapshot(
    string ApplicationKey,
    uint ProcessId,
    nint Hwnd,
    InputMode Mode,
    string Source,
    DateTimeOffset StartedAt,
    DateTimeOffset ExpiresAt);

public sealed record ManualOverrideEvaluation(
    bool Suppress,
    ManualOverrideSnapshot? Override,
    string Reason)
{
    public static ManualOverrideEvaluation None { get; } =
        new(false, null, "none");
}

/// <summary>
/// Runtime-only veto used when the user explicitly chooses a different input mode.
/// The override is scoped to the current top-level foreground window and expires
/// after a bounded interval. Leaving that foreground window clears it immediately,
/// so re-entering an application restores normal rule behavior.
///
/// P8A deliberately exposes registration as an explicit hook instead of guessing
/// from arbitrary keyboard state. Later input-language observers/tray actions can
/// report a verified user override without teaching the coordinator provider-
/// specific hotkeys.
/// </summary>
public sealed class ManualOverrideGuard
{
    private readonly object _sync = new();
    private readonly TimeProvider _timeProvider;
    private readonly ManualOverrideOptions _options;
    private ManualOverrideSnapshot? _current;

    public ManualOverrideGuard(
        ManualOverrideOptions? options = null,
        TimeProvider? timeProvider = null)
    {
        _options = options ?? new ManualOverrideOptions();
        _options.Validate();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public ManualOverrideSnapshot? Current
    {
        get
        {
            lock (_sync)
            {
                ExpireIfNeededCore();
                return _current;
            }
        }
    }

    public ManualOverrideSnapshot Register(
        InputContextSnapshot context,
        InputMode mode,
        string source,
        TimeSpan? duration = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (mode is not (InputMode.Chinese or InputMode.English))
        {
            throw new ArgumentException(
                "A manual override must explicitly choose Chinese or English.",
                nameof(mode));
        }

        if (string.IsNullOrWhiteSpace(source))
        {
            throw new ArgumentException(
                "Manual override source cannot be empty.",
                nameof(source));
        }

        var effectiveDuration = duration ?? _options.DefaultDuration;
        if (effectiveDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(duration),
                "Manual override duration must be positive.");
        }

        var now = _timeProvider.GetUtcNow();
        var snapshot = new ManualOverrideSnapshot(
            context.Application.Key,
            context.Window.ProcessId,
            context.Window.Hwnd,
            mode,
            source.Trim(),
            now,
            now + effectiveDuration);

        lock (_sync)
        {
            _current = snapshot;
        }

        return snapshot;
    }

    public void NotifyForegroundChanged(nint hwnd)
    {
        lock (_sync)
        {
            ExpireIfNeededCore();
            if (_current is not null && _current.Hwnd != hwnd)
            {
                _current = null;
            }
        }
    }

    public ManualOverrideEvaluation Evaluate(
        InputContextSnapshot context,
        InputDecision decision)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(decision);

        lock (_sync)
        {
            ExpireIfNeededCore();
            var current = _current;
            if (current is null ||
                current.Hwnd != context.Window.Hwnd ||
                current.ProcessId != context.Window.ProcessId)
            {
                if (current is not null && current.Hwnd == context.Window.Hwnd)
                {
                    // HWND values can be reused after a process exits. A different
                    // process owning the same numeric handle must never inherit a
                    // previous application's manual override.
                    _current = null;
                }

                return ManualOverrideEvaluation.None;
            }

            if (!decision.HasTarget || decision.Action == InputAction.Keep)
            {
                return new ManualOverrideEvaluation(false, current, "no-mutating-target");
            }

            var targetMode = decision.Action switch
            {
                InputAction.Chinese => InputMode.Chinese,
                InputAction.English => InputMode.English,
                _ => InputMode.Unknown
            };

            if (targetMode == InputMode.Unknown || targetMode == current.Mode)
            {
                return new ManualOverrideEvaluation(false, current, "target-matches-override");
            }

            return new ManualOverrideEvaluation(true, current, "manual-override-active");
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _current = null;
        }
    }

    private void ExpireIfNeededCore()
    {
        if (_current is not null && _timeProvider.GetUtcNow() >= _current.ExpiresAt)
        {
            _current = null;
        }
    }
}
