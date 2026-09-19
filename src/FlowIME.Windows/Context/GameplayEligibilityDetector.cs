using System.Diagnostics;
using FlowIME.Core.Context;

namespace FlowIME.Windows.Context;

/// <summary>
/// Conservatively promotes a geometrically fullscreen foreground window to the Game
/// context only when at least one strong, independent gameplay fact is present.
/// Fullscreen alone is intentionally insufficient: browser F11, video playback,
/// presentations and remote-desktop sessions must remain ordinary Fullscreen context.
/// </summary>
public sealed record GameplayEligibilityObservation(
    DateTimeOffset Timestamp,
    string ProcessName,
    bool IsFullscreen,
    bool IsEligible,
    ContextSignalConfidence Confidence,
    string Reason,
    IReadOnlyList<GameplayEvidenceKind> Evidence);

public sealed class GameplayEligibilityDetector : IInputContextDetector
{
    internal const string DetectorId = "windows.gameplay.eligibility";

    private const int ObservationCapacity = 64;

    private readonly IWindowPresentationProbe _presentationProbe;
    private readonly GameplayEligibilityEvaluator _evaluator;
    private readonly IReadOnlyList<IGameplayEvidenceProbe> _evidenceProbes;
    private readonly object _observationSync = new();
    private readonly Queue<GameplayEligibilityObservation> _observations = new();

    public GameplayEligibilityDetector()
        : this(
            new Win32WindowPresentationProbe(),
            new GameplayEligibilityEvaluator(),
            [
                new GameConfigStoreEvidenceProbe(),
                new Direct3DFullscreenEvidenceProbe()
            ])
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Gameplay eligibility detection requires Windows.");
        }
    }

    internal GameplayEligibilityDetector(
        IWindowPresentationProbe presentationProbe,
        GameplayEligibilityEvaluator evaluator,
        IEnumerable<IGameplayEvidenceProbe> evidenceProbes)
    {
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

        var presentation = _presentationProbe.Capture(request.Window.Hwnd);
        cancellationToken.ThrowIfCancellationRequested();

        var isFullscreen = presentation.IsFullscreen(
            FullscreenWindowDetector.DefaultEdgeTolerancePixels);
        if (!isFullscreen)
        {
            return ValueTask.FromResult<IReadOnlyList<InputContextSignal>>(
                Array.Empty<InputContextSignal>());
        }

        var evidence = new List<GameplayEvidence>();
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
            catch (Exception ex)
            {
                Trace.WriteLine(
                    $"[FlowIME.Gameplay] utc={DateTimeOffset.UtcNow:O} " +
                    $"probe={probe.Id} result=failed type={ex.GetType().Name}");
            }
        }

        var assessment = _evaluator.Evaluate(isFullscreen, evidence);
        RecordObservation(request, assessment, isFullscreen: true);
        Trace.WriteLine(
            $"[FlowIME.Gameplay] utc={DateTimeOffset.UtcNow:O} " +
            $"process={SanitizeProcessName(request.Window.ProcessName)} " +
            $"eligible={assessment.IsEligible} reason={assessment.Reason} " +
            $"evidence={string.Join(',', assessment.Evidence.Select(item => item.Kind))}");

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
        bool isFullscreen)
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
                .ToArray());

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
