using FlowIME.Core.Context;
using FlowIME.Core.Decisions;
using FlowIME.Core.Models;

namespace FlowIME.Core.Tests.Decisions;

public sealed class GameplayKeyboardBaselinePolicyTests
{
    [Fact]
    public void Ready_game_context_returns_keep_veto()
    {
        var state = new GameplayKeyboardBaselineState();
        state.SetReady(true);
        var policy = new GameplayKeyboardBaselinePolicy(state);

        var match = policy.Evaluate(Context(InputContextSignalKind.Game));

        Assert.NotNull(match);
        Assert.Equal(InputAction.Keep, match!.Action);
        Assert.Equal("gameplay-us-keyboard-baseline", match.Reason);
    }

    [Fact]
    public void Not_ready_does_not_override_existing_rules()
    {
        var state = new GameplayKeyboardBaselineState();
        var policy = new GameplayKeyboardBaselinePolicy(state);

        Assert.Null(policy.Evaluate(Context(InputContextSignalKind.Game)));
    }

    [Fact]
    public void Game_text_entry_bypasses_baseline_for_text_entry_policy()
    {
        var state = new GameplayKeyboardBaselineState();
        state.SetReady(true);
        var policy = new GameplayKeyboardBaselinePolicy(state);

        Assert.Null(policy.Evaluate(Context(
            InputContextSignalKind.Game,
            InputContextSignalKind.GameTextEntry)));
    }

    private static InputContextSnapshot Context(params InputContextSignalKind[] kinds)
    {
        var window = new WindowContext(
            (nint)0x10,
            10,
            11,
            "game",
            @"C:\Games\game.exe",
            "Game",
            "GameWindow",
            null);
        var signals = new List<InputContextSignal>
        {
            new(InputContextSignalKind.Application, "test")
        };
        signals.AddRange(kinds.Select(kind => new InputContextSignal(kind, "test")));
        return new InputContextSnapshot(
            window,
            ApplicationIdentity.FromWindow(window),
            InputContextTrigger.ForegroundChanged,
            0,
            DateTimeOffset.UtcNow,
            signals);
    }
}
