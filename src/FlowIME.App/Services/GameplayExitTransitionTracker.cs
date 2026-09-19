namespace FlowIME.App.Services;

internal enum GameplayTransition
{
    None,
    Entered,
    Exited
}

/// <summary>
/// Tracks resolved Gameplay context transitions independently from the immediate
/// raw-foreground disarm path. This lets the app request a post-game rule restore
/// only after the new foreground has actually been resolved as non-gameplay.
/// </summary>
internal sealed class GameplayExitTransitionTracker
{
    private bool _initialized;
    private bool _wasGameplay;

    internal GameplayTransition Update(bool isGameplay)
    {
        if (!_initialized)
        {
            _initialized = true;
            _wasGameplay = isGameplay;
            return GameplayTransition.None;
        }

        if (_wasGameplay == isGameplay)
        {
            return GameplayTransition.None;
        }

        _wasGameplay = isGameplay;
        return isGameplay
            ? GameplayTransition.Entered
            : GameplayTransition.Exited;
    }
}
