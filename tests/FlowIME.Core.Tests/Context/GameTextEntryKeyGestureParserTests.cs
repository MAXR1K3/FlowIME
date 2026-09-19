using FlowIME.Core.Context;

namespace FlowIME.Core.Tests.Context;

public sealed class GameTextEntryKeyGestureParserTests
{
    [Fact]
    public void Parses_common_chat_keys_and_modifiers()
    {
        var gestures = GameTextEntryKeyGestureParser.ParseList("Enter, T, Ctrl+Y, F2, Slash");

        Assert.Equal(5, gestures.Count);
        Assert.Contains(new GameTextEntryKeyGesture(0x0D), gestures);
        Assert.Contains(new GameTextEntryKeyGesture(0x54), gestures);
        Assert.Contains(
            new GameTextEntryKeyGesture(0x59, GameTextEntryModifierKeys.Control),
            gestures);
        Assert.Contains(new GameTextEntryKeyGesture(0x71), gestures);
        Assert.Contains(new GameTextEntryKeyGesture(0xBF), gestures);
    }

    [Fact]
    public void Formatting_is_canonical_and_round_trips()
    {
        var gesture = new GameTextEntryKeyGesture(
            0x0D,
            GameTextEntryModifierKeys.Control | GameTextEntryModifierKeys.Shift);

        var formatted = GameTextEntryKeyGestureParser.Format(gesture);
        var parsed = GameTextEntryKeyGestureParser.Parse(formatted);

        Assert.Equal("Ctrl+Shift+Enter", formatted);
        Assert.Equal(gesture, parsed);
    }

    [Fact]
    public void Duplicate_list_items_are_removed()
    {
        var gestures = GameTextEntryKeyGestureParser.ParseList("T, t, T");

        Assert.Single(gestures);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ctrl")]
    [InlineData("Ctrl+T+Y")]
    [InlineData("Mouse1")]
    public void Invalid_single_gesture_is_rejected(string gesture)
    {
        Assert.Throws<FormatException>(() => GameTextEntryKeyGestureParser.Parse(gesture));
    }
}
