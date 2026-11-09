using FlowIME.Core.Context;

namespace FlowIME.Core.Tests.Context;

public sealed class PointerMenuTrackerTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 9, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Enters_menu_only_after_the_pointer_stays_visible()
    {
        var tracker = new PointerMenuTracker();

        Assert.Equal(PointerMenuTransition.None, tracker.Observe(true, At(0)));
        Assert.Equal(PointerMenuTransition.None, tracker.Observe(true, At(1000)));
        Assert.Equal(PointerMenuTransition.Enter, tracker.Observe(true, At(1500)));
        Assert.Equal(PointerMenuTransition.None, tracker.Observe(true, At(2000)));
        Assert.True(tracker.InMenu);
    }

    [Fact]
    public void A_brief_pointer_during_play_does_not_enter_the_menu()
    {
        // COD shows its pointer for about a second on kill cams and score screens.
        var tracker = new PointerMenuTracker();

        tracker.Observe(false, At(0));
        Assert.Equal(PointerMenuTransition.None, tracker.Observe(true, At(500)));
        Assert.Equal(PointerMenuTransition.None, tracker.Observe(true, At(1500)));
        Assert.Equal(PointerMenuTransition.None, tracker.Observe(false, At(2000)));
        Assert.False(tracker.InMenu);
    }

    [Fact]
    public void A_short_hide_on_a_menu_page_change_keeps_menu_mode()
    {
        var tracker = new PointerMenuTracker();
        tracker.Observe(true, At(0));
        tracker.Observe(true, At(1500));

        Assert.Equal(PointerMenuTransition.None, tracker.Observe(false, At(2000)));
        Assert.Equal(PointerMenuTransition.None, tracker.Observe(false, At(3000)));
        Assert.Equal(PointerMenuTransition.None, tracker.Observe(true, At(3500)));
        Assert.True(tracker.InMenu);
    }

    [Fact]
    public void Leaves_menu_once_the_pointer_stays_hidden()
    {
        var tracker = new PointerMenuTracker();
        tracker.Observe(true, At(0));
        tracker.Observe(true, At(1500));

        Assert.Equal(PointerMenuTransition.None, tracker.Observe(false, At(2000)));
        Assert.Equal(PointerMenuTransition.Exit, tracker.Observe(false, At(4000)));
        Assert.False(tracker.InMenu);
    }

    [Fact]
    public void Reset_forgets_history()
    {
        var tracker = new PointerMenuTracker();
        tracker.Observe(true, At(0));
        tracker.Observe(true, At(1500));

        tracker.Reset();

        Assert.False(tracker.InMenu);
        Assert.Equal(PointerMenuTransition.None, tracker.Observe(true, At(1600)));
    }

    private static DateTimeOffset At(int milliseconds) => Start.AddMilliseconds(milliseconds);
}
