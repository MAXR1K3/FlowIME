namespace FlowIME.Core.Context;

/// <summary>
/// Context detectors observe the current window/focus state and emit classification
/// signals. They never mutate the input method. A detector failure is isolated by
/// InputContextEngine so one optional detector cannot stop the automation pipeline.
/// Implementations must be side-effect free and safe for concurrent/repeated sampling;
/// automation and status projection may evaluate the same native state independently.
/// </summary>
public interface IInputContextDetector
{
    string Id { get; }

    int Order { get; }

    ValueTask<IReadOnlyList<InputContextSignal>> DetectAsync(
        ContextDetectionRequest request,
        CancellationToken cancellationToken = default);
}
