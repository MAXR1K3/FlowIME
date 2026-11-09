namespace FlowIME.App.ViewModels;

internal enum GameLibraryFilter
{
    All,
    Configured,
    NotSetUp
}

/// <summary>One titled section of the game library list.</summary>
internal sealed class GameLibraryGroup(string title, IEnumerable<GameTextEntryTargetItemViewModel> items)
    : List<GameTextEntryTargetItemViewModel>(items)
{
    public string Title { get; } = title;

    public string CountLabel => Count.ToString(System.Globalization.CultureInfo.CurrentCulture);
}

/// <summary>
/// Search, filter and section the game library. Configured games come first
/// so the few that matter are not buried under every scanned store entry.
/// </summary>
internal static class GameLibraryView
{
    internal static IReadOnlyList<GameTextEntryTargetItemViewModel> Filter(
        IEnumerable<GameTextEntryTargetItemViewModel> items,
        string? query,
        GameLibraryFilter filter)
    {
        var text = query?.Trim();
        return items
            .Where(item => filter switch
            {
                GameLibraryFilter.Configured => item.HasProfile,
                GameLibraryFilter.NotSetUp => !item.HasProfile,
                _ => true
            })
            .Where(item => string.IsNullOrEmpty(text) ||
                item.DisplayName.Contains(text, StringComparison.CurrentCultureIgnoreCase) ||
                item.SourceName.Contains(text, StringComparison.CurrentCultureIgnoreCase))
            .ToArray();
    }

    /// <summary>Sections for the "all" view; empty sections are left out.</summary>
    internal static IReadOnlyList<GameLibraryGroup> Group(IEnumerable<GameTextEntryTargetItemViewModel> items)
    {
        var list = items.ToArray();
        return new[]
            {
                new GameLibraryGroup("已配置", list.Where(static item => item.HasProfile)),
                new GameLibraryGroup("未设置", list.Where(static item => !item.HasProfile))
            }
            .Where(static group => group.Count > 0)
            .ToArray();
    }

    internal static string TabLabel(GameLibraryFilter filter, IEnumerable<GameTextEntryTargetItemViewModel> items)
    {
        var list = items as IReadOnlyCollection<GameTextEntryTargetItemViewModel> ?? items.ToArray();
        return filter switch
        {
            GameLibraryFilter.Configured => $"已配置 {list.Count(static item => item.HasProfile)}",
            GameLibraryFilter.NotSetUp => $"未设置 {list.Count(static item => !item.HasProfile)}",
            _ => $"全部 {list.Count}"
        };
    }
}
