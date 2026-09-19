namespace FlowIME.Core.Decisions;

/// <summary>
/// Small thread-safe bridge between the Windows capability/execution layer and
/// the pure decision layer. The state is ready only when the user enabled the
/// baseline, automation is active and a standard US keyboard layout is available.
/// </summary>
public sealed class GameplayKeyboardBaselineState
{
    private int _ready;

    public bool IsReady => Volatile.Read(ref _ready) != 0;

    public void SetReady(bool ready) =>
        Volatile.Write(ref _ready, ready ? 1 : 0);
}
