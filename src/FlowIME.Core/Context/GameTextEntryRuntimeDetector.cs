namespace FlowIME.Core.Context;

/// <summary>
/// Projects adapter-driven GameTextEntry runtime state into the ordinary context
/// pipeline. P8C.2 uses this for both per-game hotkey sessions and standard editable
/// control focus sessions.
/// </summary>
public sealed class GameTextEntryRuntimeDetector : IInputContextDetector
{
    public const string DetectorId = "300.game-text-entry-runtime";

    private readonly GameTextEntryRuntimeState _state;

    public GameTextEntryRuntimeDetector(GameTextEntryRuntimeState state)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
    }

    public string Id => DetectorId;

    public int Order => 300;

    public ValueTask<IReadOnlyList<InputContextSignal>> DetectAsync(
        ContextDetectionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var application = ApplicationIdentity.FromWindow(request.Window);
        if (!_state.IsActiveFor(
                application,
                request.Window.ProcessId,
                request.Window.Hwnd))
        {
            return ValueTask.FromResult<IReadOnlyList<InputContextSignal>>([]);
        }

        return ValueTask.FromResult<IReadOnlyList<InputContextSignal>>(
            [
                new(
                    InputContextSignalKind.GameTextEntry,
                    DetectorId,
                    ContextSignalConfidence.Certain)
            ]);
    }
}
