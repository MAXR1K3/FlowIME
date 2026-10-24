using System.Diagnostics;
using FlowIME.Core.Context;

namespace FlowIME.Windows.Context;

/// <summary>
/// Conservatively promotes a foreground window to the Game context only when at
/// least one strong, independent gameplay fact is present. Windows game metadata
/// also supports windowed gameplay; the exclusive Direct3D fact remains fullscreen-
/// bound. Fullscreen alone is intentionally insufficient: browser F11, video
/// playback, presentations and remote-desktop sessions remain ordinary Fullscreen.
/// </summary>
public sealed record GameplayEligibilityObservation(
    DateTimeOffset Timestamp,
    string ProcessName,
    bool IsFullscreen,
    bool IsEligible,
    ContextSignalConfidence Confidence,
    string Reason,
    IReadOnlyList<GameplayEvidenceKind> Evidence)
{
    public bool ExecutablePathAvailable { get; init; }

    public IReadOnlyList<string> FailedProbeIds { get; init; } = Array.Empty<string>();
}

public sealed class GameplayEligibilityDetector : IInputContextDetector
{
    internal const string DetectorId = "windows.gameplay.eligibility";

    private const int ObservationCapacity = 64;

    private readonly IWindowPresentationProbe _presentationProbe;
    private readonly GameplayEligibilityEvaluator _evaluator;
    private readonly IReadOnlyList<IGameplayEvidenceProbe> _evidenceProbes;
    private readonly GameTextEntryProfileRegistry? _profiles;
    private readonly object _observationSync = new();
    private readonly Queue<GameplayEligibilityObservation> _observations = new();

    public GameplayEligibilityDetector()
        : this(new GameTextEntryProfileRegistry())
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Gameplay eligibility detection requires Windows.");
        }
    }

    public GameplayEligibilityDetector(GameTextEntryProfileRegistry profiles)
        : this(
            profiles,
            new Win32WindowPresentationProbe(),
            new GameplayEligibilityEvaluator(),
            [
                new GameConfigStoreEvidenceProbe(),
                new Direct3DFullscreenEvidenceProbe()
            ])
    {
        ArgumentNullException.ThrowIfNull(profiles);
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Gameplay eligibility detection requires Windows.");
    }

    internal GameplayEligibilityDetector(
        IWindowPresentationProbe presentationProbe,
        GameplayEligibilityEvaluator evaluator,
        IEnumerable<IGameplayEvidenceProbe> evidenceProbes)
        : this(null, presentationProbe, evaluator, evidenceProbes)
    {
    }

    internal GameplayEligibilityDetector(
        GameTextEntryProfileRegistry? profiles,
        IWindowPresentationProbe presentationProbe,
        GameplayEligibilityEvaluator evaluator,
        IEnumerable<IGameplayEvidenceProbe> evidenceProbes)
    {
        _profiles = profiles;
        _presentationProbe = presentationProbe ??
            throw new ArgumentNullException(nameof(presentationProbe));
        _evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));

        var configured = (evidenceProbes ?? throw new ArgumentNullException(nameof(evidenceProbes)))
            .ToArray();
        ValidateProbeIds(configured);
        _evidenceProbes = configured
            .OrderBy(probe => probe.Order)
            .ThenBy(probe => probe.Id, StringComparer.Ordinal)
            .ToArray();
    }

    public string Id => DetectorId;

    public int Order => 200;

    public IReadOnlyList<GameplayEligibilityObservation> GetRecentObservations(
        int maxCount = 8)
    {
        if (maxCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCount));
        }

        lock (_observationSync)
        {
            return _observations
                .Reverse()
                .Take(maxCount)
                .ToArray();
        }
    }

    public ValueTask<IReadOnlyList<InputContextSignal>> DetectAsync(
        ContextDetectionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var evidence = new List<GameplayEvidence>();
        var failedProbeIds = new List<string>();
        var isFullscreen = false;
        try
        {
            isFullscreen = _presentationProbe.Capture(request.Window.Hwnd).IsFullscreen(
                FullscreenWindowDetector.DefaultEdgeTolerancePixels);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            failedProbeIds.Add("window-presentation");
            Trace.WriteLine($"[FlowIME.Gameplay] utc={DateTimeOffset.UtcNow:O} probe=window-presentation result=failed type={ex.GetType().Name}");
        }
        cancellationToken.ThrowIfCancellationRequested();
        var application = FlowIME.Core.Context.ApplicationIdentity.FromWindow(request.Window);
        var profile = _profiles?.ResolveDeclaredProfile(application);
        if (profile?.Classification == GameplayClassification.Game)
        {
            evidence.Add(new GameplayEvidence(GameplayEvidenceKind.UserDeclaredGame, "user-profile", ContextSignalConfidence.Certain));
        }
        else if (profile?.Classification == GameplayClassification.NotGame)
        {
            evidence.Add(new GameplayEvidence(GameplayEvidenceKind.UserDeclaredNotGame, "user-profile", ContextSignalConfidence.Certain));
        }
        foreach (var probe in _evidenceProbes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var item = probe.Capture(request.Window);
                if (item is not null && !evidence.Contains(item))
                {
                    evidence.Add(item);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                failedProbeIds.Add(probe.Id);
                Trace.WriteLine(
                    $"[FlowIME.Gameplay] utc={DateTimeOffset.UtcNow:O} " +
                    $"probe={probe.Id} result=failed type={ex.GetType().Name}");
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        var assessment = _evaluator.Evaluate(isFullscreen, evidence);
        RecordObservation(request, assessment, isFullscreen, failedProbeIds);
        Trace.WriteLine(
            $"[FlowIME.Gameplay] utc={DateTimeOffset.UtcNow:O} " +
            $"process={SanitizeProcessName(request.Window.ProcessName)} " +
            $"eligible={assessment.IsEligible} reason={assessment.Reason} " +
            $"evidence={string.Join(',', assessment.Evidence.Select(item => item.Kind))} " +
            $"pathAvailable={!string.IsNullOrWhiteSpace(request.Window.ExecutablePath)} " +
            $"failedProbes={string.Join(',', failedProbeIds)}");

        if (!assessment.IsEligible)
        {
            return ValueTask.FromResult<IReadOnlyList<InputContextSignal>>(
                Array.Empty<InputContextSignal>());
        }

        return ValueTask.FromResult<IReadOnlyList<InputContextSignal>>(
            [new InputContextSignal(
                InputContextSignalKind.Game,
                $"{DetectorId}:{assessment.Reason}",
                assessment.Confidence)]);
    }

    private void RecordObservation(
        ContextDetectionRequest request,
        GameplayEligibilityAssessment assessment,
        bool isFullscreen,
        IReadOnlyList<string> failedProbeIds)
    {
        var observation = new GameplayEligibilityObservation(
            request.Timestamp,
            SanitizeProcessName(request.Window.ProcessName),
            isFullscreen,
            assessment.IsEligible,
            assessment.Confidence,
            assessment.Reason,
            assessment.Evidence
                .Select(item => item.Kind)
                .Distinct()
                .ToArray())
        {
            ExecutablePathAvailable = !string.IsNullOrWhiteSpace(request.Window.ExecutablePath),

            FailedProbeIds = failedProbeIds.ToArray()
        };

        lock (_observationSync)
        {
            while (_observations.Count >= ObservationCapacity)
            {
                _observations.Dequeue();
            }

            _observations.Enqueue(observation);
        }
    }


    private static void ValidateProbeIds(IReadOnlyList<IGameplayEvidenceProbe> probes)
    {
        foreach (var probe in probes)
        {
            ArgumentNullException.ThrowIfNull(probe);
            if (string.IsNullOrWhiteSpace(probe.Id))
            {
                throw new ArgumentException(
                    "Gameplay evidence probe IDs must be non-empty.",
                    nameof(probes));
            }
        }

        var duplicate = probes
            .GroupBy(probe => probe.Id, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new ArgumentException(
                $"Duplicate gameplay evidence probe ID '{duplicate.Key}'.",
                nameof(probes));
        }
    }

    private static string SanitizeProcessName(string? processName) =>
        string.IsNullOrWhiteSpace(processName)
            ? "unknown"
            : processName.Replace('|', '_').Replace('\r', '_').Replace('\n', '_');
}
