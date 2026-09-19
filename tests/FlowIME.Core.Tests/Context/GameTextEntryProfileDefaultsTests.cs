using FlowIME.Core.Context;

namespace FlowIME.Core.Tests.Context;

public sealed class GameTextEntryProfileDefaultsTests
{
    [Fact]
    public void CreateDefault_uses_safe_visible_chat_controls()
    {
        var profile = GameTextEntryProfile.CreateDefault("exe:example", "Example Game");

        Assert.True(profile.Enabled);
        Assert.True(profile.DetectionMode.HasFlag(GameTextEntryDetectionMode.StandardTextControl));
        Assert.True(profile.DetectionMode.HasFlag(GameTextEntryDetectionMode.HotkeyProfile));
        Assert.Equal([new GameTextEntryKeyGesture(0x0D)], profile.EnterGestures);
        Assert.Equal(
            [new GameTextEntryKeyGesture(0x0D), new GameTextEntryKeyGesture(0x1B)],
            profile.ExitGestures);
    }
}
