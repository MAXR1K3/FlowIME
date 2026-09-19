using FlowIME.App.Services;

namespace FlowIME.App.Tests.Services;

public sealed class GameplayExitTransitionTrackerTests
{
    [Fact]
    public void First_sample_establishes_baseline_without_transition()
    {
        var tracker = new GameplayExitTransitionTracker();

        Assert.Equal(GameplayTransition.None, tracker.Update(isGameplay: true));
    }

    [Fact]
    public void Game_to_non_game_reports_exit_once()
    {
        var tracker = new GameplayExitTransitionTracker();
        _ = tracker.Update(isGameplay: true);

        Assert.Equal(GameplayTransition.Exited, tracker.Update(isGameplay: false));
        Assert.Equal(GameplayTransition.None, tracker.Update(isGameplay: false));
    }

    [Fact]
    public void Non_game_to_game_reports_entry_once()
    {
        var tracker = new GameplayExitTransitionTracker();
        _ = tracker.Update(isGameplay: false);

        Assert.Equal(GameplayTransition.Entered, tracker.Update(isGameplay: true));
        Assert.Equal(GameplayTransition.None, tracker.Update(isGameplay: true));
    }
}
