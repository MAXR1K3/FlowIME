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

    private static GameTextEntryProfile Profile(string key, string displayName, string id) =>
        new()
        {
            Id = id,
            ApplicationIdentityKey = key,
            ApplicationDisplayName = displayName,
            DetectionMode = GameTextEntryDetectionMode.StandardTextControl
        };
}
