using FlowIME.Core.Context;
using FlowIME.Windows.Input;

namespace FlowIME.Windows.Tests.Input;

public sealed class GameTextEntryGestureMatcherTests
{
    [Fact]
    public void Matches_only_exact_modifier_state()
    {
        IReadOnlyList<GameTextEntryKeyGesture> gestures =
        [new(0x54), new(0x59, GameTextEntryModifierKeys.Control)];

        var plainT = GameTextEntryGestureMatcher.Match(
            gestures,
            0x54,
            new GameplayModifierState(false, false, false, false));
        var shiftedT = GameTextEntryGestureMatcher.Match(
            gestures,
            0x54,
            new GameplayModifierState(false, false, true, false));
        var ctrlY = GameTextEntryGestureMatcher.Match(
            gestures,
            0x59,
            new GameplayModifierState(true, false, false, false));

        Assert.Equal(new GameTextEntryKeyGesture(0x54), plainT);
        Assert.Null(shiftedT);
        Assert.Equal(
            new GameTextEntryKeyGesture(0x59, GameTextEntryModifierKeys.Control),
            ctrlY);
    }
}
