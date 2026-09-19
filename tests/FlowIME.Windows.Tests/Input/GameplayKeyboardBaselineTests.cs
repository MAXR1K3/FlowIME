using FlowIME.Core.Decisions;
using FlowIME.Core.Settings;
using FlowIME.Windows.Input;

namespace FlowIME.Windows.Tests.Input;

public sealed class GameplayKeyboardBaselineTests
{
    private static readonly nint UsLayout = unchecked((nint)0x04090409u);
    private static readonly nint ChineseLayout = unchecked((nint)0x08040804u);

    [Fact]
    public void Standard_us_layout_is_distinguished_from_us_international()
    {
        Assert.True(GameplayKeyboardBaseline.IsStandardUsKeyboard(UsLayout));
        Assert.True(GameplayKeyboardBaseline.IsStandardUsKeyboard((nint)0x00000409));
        Assert.False(GameplayKeyboardBaseline.IsStandardUsKeyboard(unchecked((nint)(long)0xF0020409u)));
        Assert.False(GameplayKeyboardBaseline.IsStandardUsKeyboard(ChineseLayout));
    }

    [Fact]
    public void Gameplay_switches_target_thread_to_us_and_marks_policy_ready()
    {
        var state = new GameplayKeyboardBaselineState();
        var native = new FakeNative([UsLayout], ChineseLayout);
        var baseline = new GameplayKeyboardBaseline(
            state,
            native,
            GameplayKeyboardBaselineSettings.Default);

        baseline.UpdateContext(true, false, (nint)0x55, (nint)0x66, 99, "game");
        var snapshot = baseline.GetSnapshot();

        Assert.True(state.IsReady);
        Assert.True(snapshot.UsKeyboardAvailable);
        Assert.True(snapshot.Active);
        Assert.Equal(GameplayKeyboardBaselineOutcome.Applied, snapshot.LastOutcome);
        Assert.Equal(1, native.RequestCount);
        Assert.Equal(UsLayout, native.CurrentLayout);
    }

    [Fact]
    public void Already_us_does_not_send_redundant_language_request()
    {
        var state = new GameplayKeyboardBaselineState();
        var native = new FakeNative([UsLayout], UsLayout);
        var baseline = new GameplayKeyboardBaseline(state, native);

        baseline.UpdateContext(true, false, (nint)0x55, (nint)0x66, 99, "game");

        Assert.Equal(0, native.RequestCount);
        Assert.Equal(
            GameplayKeyboardBaselineOutcome.AlreadyUsKeyboard,
            baseline.GetSnapshot().LastOutcome);
    }

    [Fact]
    public void Missing_us_keyboard_keeps_policy_unready_and_does_not_mutate()
    {
        var state = new GameplayKeyboardBaselineState();
        var native = new FakeNative([ChineseLayout], ChineseLayout);
        var baseline = new GameplayKeyboardBaseline(state, native);

        baseline.UpdateContext(true, false, (nint)0x55, (nint)0x66, 99, "game");
        var snapshot = baseline.GetSnapshot();

        Assert.False(state.IsReady);
        Assert.False(snapshot.UsKeyboardAvailable);
        Assert.Equal(GameplayKeyboardBaselineOutcome.UsKeyboardUnavailable, snapshot.LastOutcome);
        Assert.Equal(0, native.RequestCount);
    }

    [Fact]
    public void Game_text_entry_releases_baseline_without_mutating_layout()
    {
        var state = new GameplayKeyboardBaselineState();
        var native = new FakeNative([UsLayout], ChineseLayout);
        var baseline = new GameplayKeyboardBaseline(state, native);

        baseline.UpdateContext(true, true, (nint)0x55, (nint)0x66, 99, "game");

        Assert.True(state.IsReady);
        Assert.False(baseline.GetSnapshot().Active);
        Assert.Equal(0, native.RequestCount);
    }

    private sealed class FakeNative(
        IReadOnlyList<nint> layouts,
        nint currentLayout) : IGameplayKeyboardNativeApi
    {
        public int RequestCount { get; private set; }
        public nint CurrentLayout { get; private set; } = currentLayout;
        public bool FailRequest { get; init; }

        public IReadOnlyList<nint> GetKeyboardLayouts() => layouts;

        public nint GetKeyboardLayout(uint threadId) => CurrentLayout;

        public bool RequestInputLanguageChange(
            nint hwnd,
            nint keyboardLayout,
            out int errorCode)
        {
            RequestCount++;
            if (FailRequest)
            {
                errorCode = 5;
                return false;
            }

            CurrentLayout = keyboardLayout;
            errorCode = 0;
            return true;
        }
    }
}
