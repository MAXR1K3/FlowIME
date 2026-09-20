using FlowIME.App.Services;

namespace FlowIME.App.Tests.Services;

public sealed class ActiveGameplayTargetTrackerTests
{
    [Fact]
    public void Enter_exit_and_reenter_emit_ordered_state_changes()
    {
        var tracker = new ActiveGameplayTargetTracker();
        var target = new RecentGameplayTarget(
            "game:alpha",
            "alpha.exe",
            DateTimeOffset.UtcNow);

        var entered = tracker.Update(target);
        var duplicate = tracker.Update(target with { SeenAt = target.SeenAt.AddSeconds(1) });

        Assert.True(entered.Changed);
        Assert.True(entered.Entered);
        Assert.False(duplicate.Changed);
        Assert.Equal(target.SeenAt.AddSeconds(1), duplicate.ActiveTarget?.SeenAt);
        Assert.Equal(target.SeenAt.AddSeconds(1), tracker.Current?.SeenAt);

        var exited = tracker.Update(null);
        var reentered = tracker.Update(target with { SeenAt = target.SeenAt.AddSeconds(2) });

        Assert.True(exited.Changed);
        Assert.False(exited.Entered);
        Assert.Null(exited.ActiveTarget);
        Assert.True(reentered.Changed);
        Assert.True(reentered.Entered);
        Assert.Equal("game:alpha", tracker.Current?.ApplicationIdentityKey);
    }
}
