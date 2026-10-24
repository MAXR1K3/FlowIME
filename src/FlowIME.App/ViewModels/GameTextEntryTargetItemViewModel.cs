using FlowIME.App.Services;
using FlowIME.Core.Context;
using FlowIME.Core.Models;
using Microsoft.UI.Xaml.Media;

namespace FlowIME.App.ViewModels;

/// <summary>
/// A game-library row merged from local stores, profiles and recent detection.
/// Discovery never creates or overwrites a profile.
/// </summary>
internal sealed record GameTextEntryTargetItemViewModel(
    string ApplicationIdentityKey,
    string DisplayName,
    bool IsRecent,
    GameTextEntryProfile? ExistingProfile,
    string SourceName = "手动",
    string? ExecutablePath = null,
    string? ArtworkPath = null,
    IReadOnlyList<string>? ExecutableCandidates = null)
{
    public bool HasProfile => ExistingProfile is not null;

    public bool CanConfigureDirectly =>
        HasProfile || IsRecent || !string.IsNullOrWhiteSpace(ExecutablePath);

    public string StatusLabel => ExistingProfile is { } profile
        ? !GameProfileTargetValidator.HasReliableTarget(profile)
            ? "需要关联实际游戏 EXE"
            : profile.Classification switch
        {
            GameplayClassification.NotGame => "已排除游戏识别",
            GameplayClassification.Automatic => profile.Enabled ? "自动识别 · 聊天已配置" : "自动识别 · 聊天已停用",
            _ => profile.Enabled ? "指定游戏 · 聊天已配置" : "指定游戏 · 聊天已停用"
        }
        : CanConfigureDirectly ? "未配置" : "需要关联游戏程序";

    public string ShortcutSummary => ExistingProfile is null
        ? "默认：打开 Enter · 发送 Enter · 取消 Esc"
        : $"打开 {GameTextEntryKeyGestureParser.FormatList(ExistingProfile.EnterGestures)} · " +
          $"发送/取消 {GameTextEntryKeyGestureParser.FormatList(ExistingProfile.ExitGestures)}";

    public ImageSource? Icon { get; set; }

    public string SourceLabel => StringComparer.Ordinal.Equals(SourceName, "配置")
        ? string.Empty
        : SourceName;

    internal RecentGameplayTarget ToGameplayTarget() =>
        new(ApplicationIdentityKey, DisplayName, DateTimeOffset.UtcNow, ExecutablePath);

    public override string ToString() =>
        IsRecent
            ? $"{DisplayName} · 最近检测 · {StatusLabel}"
            : $"{DisplayName} · {SourceName} · {StatusLabel}";

    internal static IReadOnlyList<GameTextEntryTargetItemViewModel> Build(
        IEnumerable<GameTextEntryProfile>? profiles,
        RecentGameplayTarget? recent,
        IEnumerable<GameLibraryScanEntry>? discovered = null)
    {
        var profileArray = (profiles ?? Array.Empty<GameTextEntryProfile>())
            .GroupBy(profile => profile.ApplicationIdentityKey, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
        var registry = new GameTextEntryProfileRegistry();
        registry.ReplaceProfiles(profileArray);
        GameTextEntryProfile? FindProfile(string key, string? path) =>
            profileArray.FirstOrDefault(profile => StringComparer.Ordinal.Equals(profile.ApplicationIdentityKey, key)) ??
            registry.ResolveDeclaredProfile(FlowIME.Core.Context.ApplicationIdentity.FromWindow(
                new WindowContext(0, 0, 0, string.Empty, path, null, null, null)));
        var recentProfile = recent is null ? null : FindProfile(recent.ApplicationIdentityKey, recent.ExecutablePath);
        var items = profileArray
            .Select(profile => new GameTextEntryTargetItemViewModel(
                profile.ApplicationIdentityKey,
                profile.ApplicationDisplayName,
                recentProfile?.Id == profile.Id,
                profile,
                "配置",
                profile.ExecutablePath ?? (recentProfile?.Id == profile.Id
                        ? recent?.ExecutablePath
                        : null)))
            .ToList();

        foreach (var game in discovered ?? Array.Empty<GameLibraryScanEntry>())
        {
            var key = $"library:{game.SourceName.ToLowerInvariant()}:{game.StoreId}";
            if (!string.IsNullOrWhiteSpace(game.ExecutablePath))
            {
                try
                {
                    var application = ExecutableCandidateFactory.Create(game.ExecutablePath);
                    key = FlowIME.Core.Context.ApplicationIdentity
                        .FromRunningApplication(application)
                        .Key;
                }
                catch (Exception ex) when (
                    ex is IOException or UnauthorizedAccessException or ArgumentException)
                {
                }
            }

            var existing = FindProfile(key, game.ExecutablePath);
            key = existing?.ApplicationIdentityKey ?? key;
            var duplicateIndex = items.FindIndex(item =>
                StringComparer.Ordinal.Equals(item.ApplicationIdentityKey, key));
            var item = new GameTextEntryTargetItemViewModel(
                key,
                existing?.ApplicationDisplayName ?? game.DisplayName,
                recent is not null && (StringComparer.Ordinal.Equals(recent.ApplicationIdentityKey, key) ||
                    existing is not null && recentProfile?.Id == existing.Id),
                existing,
                game.SourceName,
                existing?.ExecutablePath ?? game.ExecutablePath,
                game.ArtworkPath,
                game.ExecutableCandidates);
            if (duplicateIndex >= 0)
            {
                items[duplicateIndex] = item;
            }
            else
            {
                items.Add(item);
            }
        }

        if (recent is not null && recentProfile is null &&
            !items.Any(item => StringComparer.Ordinal.Equals(
                item.ApplicationIdentityKey,
                recent.ApplicationIdentityKey)))
        {
            items.Add(new GameTextEntryTargetItemViewModel(
                recent.ApplicationIdentityKey,
                recent.ProcessName,
                IsRecent: true,
                ExistingProfile: null,
                SourceName: "最近检测",
                ExecutablePath: recent.ExecutablePath));
        }

        return items
            .OrderByDescending(item => item.IsRecent)
            .ThenByDescending(item => item.HasProfile)
            .ThenBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.ApplicationIdentityKey, StringComparer.Ordinal)
            .ToArray();
    }
}
