using FlowIME.Core.Settings;

namespace FlowIME.Core.Tests.Settings;

public sealed class GameplayKeyboardBaselineSettingsTests
{
    [Fact]
    public void Default_enables_safe_gameplay_us_keyboard_baseline()
    {
        Assert.True(GameplayKeyboardBaselineSettings.Default.Enabled);
    }
}
