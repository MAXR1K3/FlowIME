using FlowIME.Core.Settings;

namespace FlowIME.Core.Tests.Settings;

public sealed class GameplayHotkeyGuardSettingsTests
{
    [Fact]
    public void Defaults_prioritize_modern_shortcuts_without_stealing_legacy_game_binds()
    {
        var settings = GameplayHotkeyGuardSettings.Default;

        Assert.True(settings.Enabled);
        Assert.True(settings.BlockWinSpace);
        Assert.True(settings.BlockCtrlSpace);
        Assert.False(settings.BlockLegacyLanguageHotkeys);
    }
}
