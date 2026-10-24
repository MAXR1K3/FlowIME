using FlowIME.App.Services;

namespace FlowIME.App.ViewModels;

internal sealed record GameplayDetectionStatusViewModel(
    bool IsActive,
    string Title,
    string Description)
{
    internal static GameplayDetectionStatusViewModel Create(
        RecentGameplayTarget? active,
        RecentGameplayTarget? recent,
        DateTimeOffset now)
    {
        if (active is not null)
        {
            return new GameplayDetectionStatusViewModel(
                true,
                $"正在保护：{DisplayName(active)}",
                "刚刚检测到 · 游戏保护已启用");
        }

        if (recent is null)
        {
            return new GameplayDetectionStatusViewModel(
                false,
                "当前未检测到游戏",
                "自动识别未命中时，请添加实际游戏 EXE 并选择“指定为游戏”。扫描结果需保存配置后才作为指定游戏生效。");
        }

        return new GameplayDetectionStatusViewModel(
            false,
            "当前未检测到游戏",
            $"最近识别：{DisplayName(recent)} · {FormatAge(recent.SeenAt, now)}");
    }

    private static string DisplayName(RecentGameplayTarget target) =>
        string.IsNullOrWhiteSpace(target.ProcessName) ? "未知游戏" : target.ProcessName.Trim();

    private static string FormatAge(DateTimeOffset seenAt, DateTimeOffset now)
    {
        var age = now - seenAt;
        if (age < TimeSpan.FromMinutes(1))
        {
            return "刚刚";
        }

        if (age < TimeSpan.FromHours(1))
        {
            return $"{Math.Max(1, (int)age.TotalMinutes)} 分钟前";
        }

        if (age < TimeSpan.FromDays(1))
        {
            return $"{Math.Max(1, (int)age.TotalHours)} 小时前";
        }

        return seenAt.ToLocalTime().ToString("MM-dd HH:mm");
    }
}
