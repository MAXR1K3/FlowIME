using FlowIME.App.Services;
using FlowIME.App.ViewModels;
using FlowIME.Core.Context;

namespace FlowIME.App.Tests.Services;

public sealed class GameProfileTargetValidatorTests
{
    [Theory]
    [InlineData("process:game")]
    [InlineData("exe:game.exe")]
    [InlineData("path:")]
    public void Weak_identity_without_path_cannot_be_saved_and_is_not_shown_as_configured(string key)
    {
        var profile = GameTextEntryProfile.CreateDefault(key, "Game");
        Assert.False(GameProfileTargetValidator.HasReliableTarget(profile));
        Assert.Throws<ArgumentException>(() => GameProfileTargetValidator.RequireReliableTarget(profile));
        var row = Assert.Single(GameTextEntryTargetItemViewModel.Build([profile], null));
        Assert.Equal("需要关联实际游戏 EXE", row.StatusLabel);
        Assert.True(row.CanConfigureDirectly); // Keep old data editable rather than discarding it.
    }

    [Theory]
    [InlineData("path:legacy-hash")]
    [InlineData("aumid:game!app")]
    [InlineData("pfn:game_package")]
    public void Trusted_legacy_identity_does_not_require_visible_path(string key)
    {
        Assert.True(GameProfileTargetValidator.HasReliableTarget(GameTextEntryProfile.CreateDefault(key, "Game")));
    }

    [Fact]
    public void Explicit_alias_repairs_weak_legacy_profile()
    {
        var profile = GameTextEntryProfile.CreateDefault("process:game", "Game") with
        { AdditionalExecutablePaths = [@"C:\Games\game.exe"] };
        Assert.True(GameProfileTargetValidator.HasReliableTarget(profile));
        GameProfileTargetValidator.RequireReliableTarget(profile);
    }
}
