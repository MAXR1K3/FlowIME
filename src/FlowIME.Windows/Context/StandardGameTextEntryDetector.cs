using FlowIME.Core.Context;

namespace FlowIME.Windows.Context;

/// <summary>
/// Converts high-confidence standard editable-control focus into the shared
/// GameTextEntry runtime latch for explicitly configured games. The later
/// GameTextEntryRuntimeDetector (order 300) projects that latch into the context
/// signal set, which keeps the state stable across post-apply/manual refreshes.
/// </summary>
public sealed class StandardGameTextEntryDetector : IInputContextDetector
{
    public const string DetectorId = "250.game-text-entry.standard-control";

    private readonly GameTextEntryProfileRegistry _profiles;
    private readonly GameTextEntryRuntimeState _runtimeState;
    private readonly IStandardTextControlProbe _probe;
    private readonly IWindowPresentationProbe _presentationProbe;
    private readonly object _focusSequenceSync = new();
    private DateTimeOffset _latestFocusTimestamp = DateTimeOffset.MinValue;
    private DateTimeOffset _committedFocusTimestamp = DateTimeOffset.MinValue;

    public StandardGameTextEntryDetector(
        GameTextEntryProfileRegistry profiles,
        GameTextEntryRuntimeState runtimeState)
        : this(
            profiles,
            runtimeState,
            new StandardTextControlProbe(),
            new Win32WindowPresentationProbe())
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Standard text-control detection requires Windows.");
        }
    }

    internal StandardGameTextEntryDetector(
        GameTextEntryProfileRegistry profiles,
        GameTextEntryRuntimeState runtimeState,
        IStandardTextControlProbe probe,
        IWindowPresentationProbe presentationProbe)
    {
        _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
        _runtimeState = runtimeState ?? throw new ArgumentNullException(nameof(runtimeState));
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _presentationProbe = presentationProbe ?? throw new ArgumentNullException(nameof(presentationProbe));
    }

    public string Id => DetectorId;

    public int Order => 250;

    public ValueTask<IReadOnlyList<InputContextSignal>> DetectAsync(
        ContextDetectionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        // Standard controls are a focus-level fact. Do not probe an external game
        // merely because it became foreground or a manual refresh ran. If a previous
        // focus event established a session, GameTextEntryRuntimeDetector preserves it.
        if (request.Trigger != InputContextTrigger.FocusChanged ||
            !RegisterFocusCandidate(request.Timestamp))
        {
            return ValueTask.FromResult<IReadOnlyList<InputContextSignal>>([]);
        }

        var application = FlowIME.Core.Context.ApplicationIdentity.FromWindow(request.Window);
        var profile = _profiles.Resolve(application);
        if (profile is null ||
            !profile.DetectionMode.HasFlag(GameTextEntryDetectionMode.StandardTextControl))
        {
            CommitIfLatest(
                request,
                () => DeactivateOwnedSession(application, request, "standard-profile-unavailable"));
            return ValueTask.FromResult<IReadOnlyList<InputContextSignal>>([]);
        }

        // The profile was created from a confirmed Gameplay identity, but the same
        // executable can also expose launcher/windowed UI. Only latch standard text
        // focus while the current top-level window is geometrically fullscreen; the
        // context engine will additionally require the independent Game signal.
        var presentation = _presentationProbe.Capture(request.Window.Hwnd);
        if (!presentation.IsFullscreen(FullscreenWindowDetector.DefaultEdgeTolerancePixels))
        {
            CommitIfLatest(
                request,
                () => DeactivateOwnedSession(application, request, "standard-not-fullscreen"));
            return ValueTask.FromResult<IReadOnlyList<InputContextSignal>>([]);
        }

        var observation = _probe.Capture(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (!observation.IsTextEntry ||
            observation.Confidence < ContextSignalConfidence.High)
        {
            // Generic document containers are intentionally treated as ambiguous.
            // Only high/certain evidence may relax the Gameplay US baseline.
            CommitIfLatest(
                request,
                () => DeactivateOwnedSession(application, request, "standard-focus-left"));
            return ValueTask.FromResult<IReadOnlyList<InputContextSignal>>([]);
        }

        CommitIfLatest(
            request,
            () => _runtimeState.Activate(
                application,
                request.Window.ProcessId,
                request.Window.Hwnd,
                request.Window.ProcessName,
                GameTextEntryActivationSource.StandardTextControl,
                profile.Id,
                reason: $"standard:{observation.Reason}"));

        // Runtime detector order 300 emits the actual GameTextEntry signal in this
        // same context resolution. Returning no duplicate signal keeps diagnostics
        // and downstream evidence deterministic.
        return ValueTask.FromResult<IReadOnlyList<InputContextSignal>>([]);
    }

    private bool RegisterFocusCandidate(DateTimeOffset timestamp)
    {
        lock (_focusSequenceSync)
        {
            if (timestamp < _latestFocusTimestamp)
            {
                return false;
            }

            if (timestamp > _latestFocusTimestamp)
            {
                _latestFocusTimestamp = timestamp;
            }

            return true;
        }
    }

    private void CommitIfLatest(ContextDetectionRequest request, Action mutation)
    {
        lock (_focusSequenceSync)
        {
            if (request.Timestamp != _latestFocusTimestamp ||
                request.Timestamp <= _committedFocusTimestamp)
            {
                return;
            }

            _committedFocusTimestamp = request.Timestamp;
            mutation();
        }
    }

    private void DeactivateOwnedSession(
        FlowIME.Core.Context.ApplicationIdentity application,
        ContextDetectionRequest request,
        string reason)
    {
        var runtime = _runtimeState.GetSnapshot();
        if (runtime.Active &&
            runtime.Source == GameTextEntryActivationSource.StandardTextControl &&
            StringComparer.Ordinal.Equals(runtime.ApplicationKey, application.Key) &&
            (runtime.ProcessId == 0 || runtime.ProcessId == request.Window.ProcessId) &&
            (runtime.TopLevelHwnd == 0 || runtime.TopLevelHwnd == request.Window.Hwnd))
        {
            _runtimeState.Deactivate(reason);
        }
    }
}
