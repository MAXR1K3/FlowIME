using FlowIME.Core.Settings;
using FlowIME.Windows.Input;

namespace FlowIME.Windows.Tests.Input;

public sealed class GameplayHotkeyChordMatcherTests
{
    [Fact]
    public void Default_settings_block_win_space()
    {
        var state = new GameplayModifierState(
            Ctrl: false,
            Alt: false,
            Shift: false,
            Win: true);

        var result = GameplayHotkeyChordMatcher.MatchKeyDown(
            GameplayHotkeyChordMatcher.VkSpace,
            state,
            GameplayHotkeyGuardSettings.Default);

        Assert.Equal(GameplayHotkeyChord.WinSpace, result);
    }

    [Fact]
    public void Default_settings_block_ctrl_space()
    {
        var state = new GameplayModifierState(
            Ctrl: true,
            Alt: false,
            Shift: false,
            Win: false);

        var result = GameplayHotkeyChordMatcher.MatchKeyDown(
            GameplayHotkeyChordMatcher.VkSpace,
            state,
            GameplayHotkeyGuardSettings.Default);

        Assert.Equal(GameplayHotkeyChord.CtrlSpace, result);
    }

    [Fact]
    public void Legacy_language_hotkeys_are_not_blocked_by_default()
    {
        var state = new GameplayModifierState(
            Ctrl: false,
            Alt: true,
            Shift: true,
            Win: false);

        var result = GameplayHotkeyChordMatcher.MatchKeyDown(
            GameplayHotkeyChordMatcher.VkLeftShift,
            state,
            GameplayHotkeyGuardSettings.Default);

        Assert.Equal(GameplayHotkeyChord.None, result);
    }

    [Theory]
    [InlineData(true, false, GameplayHotkeyChord.AltShift)]
    [InlineData(false, true, GameplayHotkeyChord.CtrlShift)]
    public void Legacy_language_hotkeys_can_be_opted_in(
        bool alt,
        bool ctrl,
        GameplayHotkeyChord expected)
    {
        var settings = GameplayHotkeyGuardSettings.Default with
        {
            BlockLegacyLanguageHotkeys = true
        };
        var state = new GameplayModifierState(
            Ctrl: ctrl,
            Alt: alt,
            Shift: true,
            Win: false);

        var result = GameplayHotkeyChordMatcher.MatchKeyDown(
            GameplayHotkeyChordMatcher.VkRightShift,
            state,
            settings);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Ordinary_space_is_not_blocked_without_a_switch_modifier()
    {
        var result = GameplayHotkeyChordMatcher.MatchKeyDown(
            GameplayHotkeyChordMatcher.VkSpace,
            default,
            GameplayHotkeyGuardSettings.Default);

        Assert.Equal(GameplayHotkeyChord.None, result);
    }

    [Fact]
    public void Modifier_state_tracks_left_and_right_variants()
    {
        var state = default(GameplayModifierState);
        state = GameplayHotkeyChordMatcher.UpdateModifier(
            state,
            GameplayHotkeyChordMatcher.VkRightControl,
            isDown: true);
        state = GameplayHotkeyChordMatcher.UpdateModifier(
            state,
            GameplayHotkeyChordMatcher.VkLeftWin,
            isDown: true);

        Assert.True(state.Ctrl);
        Assert.True(state.Win);

        state = GameplayHotkeyChordMatcher.UpdateModifier(
            state,
            GameplayHotkeyChordMatcher.VkRightControl,
            isDown: false);
        state = GameplayHotkeyChordMatcher.UpdateModifier(
            state,
            GameplayHotkeyChordMatcher.VkLeftWin,
            isDown: false);

        Assert.False(state.Ctrl);
        Assert.False(state.Win);
    }
    [Theory]
    [InlineData(true, true, true, false, true)]
    [InlineData(false, true, true, false, false)]
    [InlineData(true, false, true, false, false)]
    [InlineData(true, true, false, false, false)]
    [InlineData(true, true, true, true, false)]
    public void Guard_gate_requires_enabled_gameplay_and_disarms_for_text_entry(
        bool settingEnabled,
        bool automationEnabled,
        bool gameContext,
        bool gameTextEntry,
        bool expected)
    {
        var settings = GameplayHotkeyGuardSettings.Default with
        {
            Enabled = settingEnabled
        };

        var armed = GameplayHotkeyGuardGate.ShouldArm(
            settings,
            automationEnabled,
            gameContext,
            gameTextEntry);

        Assert.Equal(expected, armed);
    }

}
