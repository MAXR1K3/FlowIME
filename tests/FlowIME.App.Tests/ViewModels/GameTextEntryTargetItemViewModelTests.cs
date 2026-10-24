using FlowIME.App.Services;
using FlowIME.App.ViewModels;
using FlowIME.Core.Context;

namespace FlowIME.App.Tests.ViewModels;

public sealed class GameTextEntryTargetItemViewModelTests
{
    [Fact]
    public void Builds_distinct_targets_for_each_profile_and_the_recent_unconfigured_game()
    {
        var profiles = new[]
        {
            Profile("game:a", "Alpha", "chat:a"),
            Profile("game:b", "Bravo", "chat:b")
        };
        var recent = new RecentGameplayTarget("game:c", "charlie", DateTimeOffset.UtcNow);

        var items = GameTextEntryTargetItemViewModel.Build(profiles, recent);

        Assert.Equal(3, items.Count);
        Assert.Equal(new[] { "game:c", "game:a", "game:b" }, items.Select(item => item.ApplicationIdentityKey));
        Assert.True(items.Single(item => item.ApplicationIdentityKey == "game:a").HasProfile);
        Assert.False(items.Single(item => item.ApplicationIdentityKey == "game:c").HasProfile);
        Assert.True(items.Single(item => item.ApplicationIdentityKey == "game:c").IsRecent);
    }

    [Fact]
    public void Recent_game_matching_an_existing_profile_is_not_duplicated_and_is_selected_first()
    {
        var profiles = new[]
        {
            Profile("game:a", "Alpha", "chat:a"),
            Profile("game:b", "Bravo", "chat:b")
        };
        var recent = new RecentGameplayTarget("game:b", "bravo-process", DateTimeOffset.UtcNow);

        var items = GameTextEntryTargetItemViewModel.Build(profiles, recent);

        Assert.Equal(2, items.Count);
        Assert.Equal("game:b", items[0].ApplicationIdentityKey);
        Assert.Equal("Bravo", items[0].DisplayName);
        Assert.True(items[0].IsRecent);
        Assert.NotNull(items[0].ExistingProfile);
    }

    [Fact]
    public void Includes_scanned_unlinked_games_without_creating_profiles()
    {
        var discovered = new[]
        {
            new GameLibraryScanEntry(
                "Steam",
                "123",
                "Library Game",
                @"C:\Games\LibraryGame",
                ExecutablePath: null,
                ExecutableCandidates: [@"C:\Games\LibraryGame\A.exe", @"C:\Games\LibraryGame\B.exe"])
        };

        var item = Assert.Single(GameTextEntryTargetItemViewModel.Build([], null, discovered));

        Assert.Equal("library:steam:123", item.ApplicationIdentityKey);
        Assert.Equal("Steam", item.SourceName);
        Assert.False(item.CanConfigureDirectly);
        Assert.False(item.HasProfile);
    }

    [Fact]
    public void Recent_and_discovered_alias_merge_into_original_profile_without_rekeying_it()
    {
        var profile = Profile("path:main-game", "Main Game", "chat:main") with
        { ExecutablePath = @"C:\Games\main.exe", AdditionalExecutablePaths = [@"C:\Games\dx12.exe"] };
        var recent = new RecentGameplayTarget("path:dx12", "dx12", DateTimeOffset.UtcNow, @"C:\Games\dx12.exe");
        var game = new GameLibraryScanEntry("Steam", "test", "Game", @"C:\Games", @"C:\Games\dx12.exe", [@"C:\Games\dx12.exe"], null);
        var item = Assert.Single(GameTextEntryTargetItemViewModel.Build([profile], recent, [game]));
        Assert.Equal(profile.ApplicationIdentityKey, item.ApplicationIdentityKey);
        Assert.Equal(profile.ExecutablePath, item.ExecutablePath);
        Assert.Equal(profile.Id, item.ExistingProfile?.Id);
        Assert.True(item.IsRecent);
    }

    [Theory]
    [InlineData(GameplayClassification.Game, false, "指定游戏 · 聊天已停用")]
    [InlineData(GameplayClassification.NotGame, true, "已排除游戏识别")]
    [InlineData(GameplayClassification.Automatic, true, "自动识别 · 聊天已配置")]
    public void Status_distinguishes_classification_from_chat_enabled(GameplayClassification classification, bool enabled, string expected)
    {
        var profile = Profile("path:test", "Game", "chat:test") with { Classification = classification, Enabled = enabled };
        var item = Assert.Single(GameTextEntryTargetItemViewModel.Build([profile], null));
        Assert.Equal(expected, item.StatusLabel);
    }

    private static GameTextEntryProfile Profile(string key, string displayName, string id) =>
        new()
        {
            Id = id,
            ApplicationIdentityKey = key,
            ApplicationDisplayName = displayName,
            DetectionMode = GameTextEntryDetectionMode.StandardTextControl
        };
}
