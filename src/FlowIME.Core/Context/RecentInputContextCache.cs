namespace FlowIME.Core.Context;

/// <summary>
/// Shares the most recent context resolution between consumers of the same native
/// foreground/focus notification. Matching is intentionally exact, including the
/// event timestamp, so a later event always performs fresh detection.
/// </summary>
public sealed class RecentInputContextCache : IInputContextEngine
{
    private readonly object _sync = new();
    private readonly IInputContextEngine _inner;
    private ContextDetectionRequest? _request;
    private Lazy<Task<InputContextSnapshot>>? _resolution;

    public RecentInputContextCache(IInputContextEngine inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public async ValueTask<InputContextSnapshot> ResolveAsync(
        ContextDetectionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        Lazy<Task<InputContextSnapshot>> resolution;
        lock (_sync)
        {
            if (!Equals(_request, request) || _resolution is null)
            {
                _request = request;
                _resolution = new Lazy<Task<InputContextSnapshot>>(
                    () => ResolveSharedAsync(request),
                    LazyThreadSafetyMode.ExecutionAndPublication);
            }

            resolution = _resolution;
        }

        return await resolution.Value
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<InputContextSnapshot> ResolveSharedAsync(
        ContextDetectionRequest request) =>
        await _inner
            .ResolveAsync(request, CancellationToken.None)
            .ConfigureAwait(false);
}
