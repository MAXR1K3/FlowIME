using System.Diagnostics;

namespace FlowIME.Core.Context;

public sealed class InputContextEngine : IInputContextEngine
{
    private readonly IReadOnlyList<IInputContextDetector> _detectors;

    public InputContextEngine(IEnumerable<IInputContextDetector>? detectors = null)
    {
        var configured = (detectors ?? Array.Empty<IInputContextDetector>()).ToArray();
        ValidateDetectorIds(configured);
        _detectors = configured
            .OrderBy(detector => detector.Order)
            .ThenBy(detector => detector.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private static void ValidateDetectorIds(IReadOnlyList<IInputContextDetector> detectors)
    {
        foreach (var detector in detectors)
        {
            ArgumentNullException.ThrowIfNull(detector);
            if (string.IsNullOrWhiteSpace(detector.Id))
            {
                throw new ArgumentException(
                    "Context detector IDs must be non-empty.",
                    nameof(detectors));
            }
        }

        var duplicate = detectors
            .GroupBy(detector => detector.Id, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new ArgumentException(
                $"Duplicate context detector ID '{duplicate.Key}'.",
                nameof(detectors));
        }
    }

    public async ValueTask<InputContextSnapshot> ResolveAsync(
        ContextDetectionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var application = ApplicationIdentity.FromWindow(request.Window);
        var signals = new List<InputContextSignal>
        {
            new(
                InputContextSignalKind.Application,
                "core.application",
                ContextSignalConfidence.Certain)
        };

        foreach (var detector in _detectors)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var detected = await detector
                    .DetectAsync(request, cancellationToken)
                    .ConfigureAwait(false);

                if (detected is null)
                {
                    continue;
                }

                foreach (var signal in detected)
                {
                    if (signal is null ||
                        string.IsNullOrWhiteSpace(signal.Source))
                    {
                        continue;
                    }

                    if (!signals.Contains(signal))
                    {
                        signals.Add(signal);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Trace.WriteLine(
                    $"[FlowIME.Context] utc={DateTimeOffset.UtcNow:O} " +
                    $"detector={detector.Id} result=failed type={ex.GetType().Name}");
            }
        }

        NormalizeDerivedSignals(signals);

        return new InputContextSnapshot(
            request.Window,
            application,
            request.Trigger,
            request.FocusHwnd,
            request.Timestamp,
            signals.AsReadOnly());
    }
    private static void NormalizeDerivedSignals(List<InputContextSignal> signals)
    {
        var hasGame = signals.Any(signal => signal.Kind == InputContextSignalKind.Game);
        var hasGameTextEntry = signals.Any(
            signal => signal.Kind == InputContextSignalKind.GameTextEntry);

        // GameTextEntry is a child state of Gameplay, never a stand-alone desktop
        // context. A stale/runtime adapter signal must therefore fail closed rather
        // than disarming the Gameplay baseline in a non-game application.
        if (hasGameTextEntry && !hasGame)
        {
            signals.RemoveAll(signal => signal.Kind == InputContextSignalKind.GameTextEntry);
            hasGameTextEntry = false;
        }

        // A confirmed in-game text-entry session is also generic text input. This
        // derived signal lets later policies share text-input semantics without every
        // game adapter having to emit two separate facts.
        if (hasGameTextEntry &&
            !signals.Any(signal => signal.Kind == InputContextSignalKind.TextInput))
        {
            signals.Add(new InputContextSignal(
                InputContextSignalKind.TextInput,
                "core.game-text-entry-derived",
                ContextSignalConfidence.Certain));
        }
    }

}
