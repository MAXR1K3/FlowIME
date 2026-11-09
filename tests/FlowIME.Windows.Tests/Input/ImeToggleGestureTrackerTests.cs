using FlowIME.Windows.Input;

namespace FlowIME.Windows.Tests.Input;

public sealed class ImeToggleGestureTrackerTests
{
    private const uint VkA = 0x41;
    private const uint VkLeftShift = GameplayHotkeyChordMatcher.VkLeftShift;
    private const uint VkLeftControl = GameplayHotkeyChordMatcher.VkLeftControl;
    private const uint VkSpace = GameplayHotkeyChordMatcher.VkSpace;

    [Fact]
    public void Lone_shift_tap_is_a_toggle()
    {
        var keys = new KeySequence();

        Assert.Equal(ImeToggleGesture.None, keys.Down(VkLeftShift));
        Assert.Equal(ImeToggleGesture.LoneShift, keys.Up(VkLeftShift));
    }

    [Fact]
    public void Held_shift_with_auto_repeat_is_still_a_toggle()
    {
        var keys = new KeySequence();

        keys.Down(VkLeftShift);
        keys.Down(VkLeftShift);
        keys.Down(VkLeftShift);

        Assert.Equal(ImeToggleGesture.LoneShift, keys.Up(VkLeftShift));
    }

    [Fact]
    public void Shift_used_as_modifier_is_not_a_toggle()
    {
        var keys = new KeySequence();

        keys.Down(VkLeftShift);
        keys.Down(VkA);
        keys.Up(VkA);

        Assert.Equal(ImeToggleGesture.None, keys.Up(VkLeftShift));
    }

    [Fact]
    public void Ctrl_shift_is_not_a_lone_shift()
    {
        var keys = new KeySequence();

        keys.Down(VkLeftControl);
        keys.Down(VkLeftShift);

        Assert.Equal(ImeToggleGesture.None, keys.Up(VkLeftShift));
    }

    [Fact]
    public void Ctrl_space_is_a_toggle()
    {
        var keys = new KeySequence();

        keys.Down(VkLeftControl);

        Assert.Equal(ImeToggleGesture.CtrlSpace, keys.Down(VkSpace));
        Assert.Equal(ImeToggleGesture.None, keys.Up(VkSpace));
    }

    [Fact]
    public void Plain_space_is_not_a_toggle()
    {
        var keys = new KeySequence();

        Assert.Equal(ImeToggleGesture.None, keys.Down(VkSpace));
    }

    private sealed class KeySequence
    {
        private readonly ImeToggleGestureTracker _tracker = new();
        private GameplayModifierState _modifiers;

        public ImeToggleGesture Down(uint virtualKey) => Key(virtualKey, isDown: true);

        public ImeToggleGesture Up(uint virtualKey) => Key(virtualKey, isDown: false);

        private ImeToggleGesture Key(uint virtualKey, bool isDown)
        {
            // Mirrors the hook: the tracker sees modifiers before this transition.
            var gesture = _tracker.OnKey(virtualKey, isDown, _modifiers);
            _modifiers = GameplayHotkeyChordMatcher.UpdateModifier(
                _modifiers,
                virtualKey,
                isDown);
            return gesture;
        }
    }
}
