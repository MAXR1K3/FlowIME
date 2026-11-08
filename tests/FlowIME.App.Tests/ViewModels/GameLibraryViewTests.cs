using FlowIME.App.ViewModels;
using FlowIME.Core.Context;

namespace FlowIME.App.Tests.ViewModels;

public sealed class GameLibraryViewTests
{
    private static readonly GameTextEntryTargetItemViewModel[] Items =
    [
        Configured("Overwatch"),
        Configured("VALORANT"),
        Scanned("Battlefield 6", "Steam"),
        Scanned("Cyberpunk 2077", "Epic"),
        Scanned("Counter-Strike 2", "Steam")
    ];

    [Fact]
    public void Configured_games_form_the_first_section()
    {
        var groups = GameLibraryView.Group(Items);

        Assert.Equal(["已配置", "未设置"], groups.Select(static group => group.Title));
        Assert.Equal(["Overwatch", "VALORANT"], groups[0].Select(static item => item.DisplayName));
        Assert.Equal("3", groups[1].CountLabel);
    }

    [Fact]
    public void Empty_sections_are_left_out()
    {
        var groups = GameLibraryView.Group(Items.Where(static item => !item.HasProfile));

        Assert.Equal(["未设置"], groups.Select(static group => group.Title));
    }

    [Fact]
    public void Filter_and_search_combine()
    {
        Assert.Equal(2, GameLibraryView.Filter(Items, null, GameLibraryFilter.Configured).Count);
        Assert.Equal(
            ["Battlefield 6", "Counter-Strike 2"],
            GameLibraryView.Filter(Items, " steam ", GameLibraryFilter.NotSetUp).Select(static item => item.DisplayName));
        Assert.Empty(GameLibraryView.Filter(Items, "overwatch", GameLibraryFilter.NotSetUp));
    }

    [Fact]
    public void Tabs_show_how_many_games_each_holds()
    {
        Assert.Equal("全部 5", GameLibraryView.TabLabel(GameLibraryFilter.All, Items));
        Assert.Equal("已配置 2", GameLibraryView.TabLabel(GameLibraryFilter.Configured, Items));
        Assert.Equal("未设置 3", GameLibraryView.TabLabel(GameLibraryFilter.NotSetUp, Items));
    }

    private static GameTextEntryTargetItemViewModel Configured(string name) =>
        new(
            $"path:{name}",
            name,
            IsRecent: false,
            new GameTextEntryProfile
            {
                Id = $"chat:{name}",
                ApplicationIdentityKey = $"path:{name}",
                ApplicationDisplayName = name
            },
            "配置");

    private static GameTextEntryTargetItemViewModel Scanned(string name, string source) =>
        new($"library:{name}", name, IsRecent: false, ExistingProfile: null, source);
}
