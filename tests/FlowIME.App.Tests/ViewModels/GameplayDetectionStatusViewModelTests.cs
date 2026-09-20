using FlowIME.App.Services;
using FlowIME.App.ViewModels;

namespace FlowIME.App.Tests.ViewModels;

public sealed class GameplayDetectionStatusViewModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_reports_the_active_game_as_current_protection()
    {
        var active = new RecentGameplayTarget("game:alpha", "alpha.exe", Now.AddSeconds(-4));

        var status = GameplayDetectionStatusViewModel.Create(active, active, Now);

        Assert.True(status.IsActive);
        Assert.Equal("正在保护：alpha.exe", status.Title);
        Assert.Equal("刚刚检测到 · 游戏保护已启用", status.Description);
    }

    [Fact]
    public void Create_labels_an_inactive_target_as_recent_instead_of_current()
    {
        var recent = new RecentGameplayTarget("game:alpha", "alpha.exe", Now.AddMinutes(-3));

        var status = GameplayDetectionStatusViewModel.Create(null, recent, Now);

        Assert.False(status.IsActive);
        Assert.Equal("当前未检测到游戏", status.Title);
        Assert.Equal("最近识别：alpha.exe · 3 分钟前", status.Description);
    }

    [Fact]
    public void Create_explains_that_detection_is_automatic_before_any_game_is_seen()
    {
        var status = GameplayDetectionStatusViewModel.Create(null, null, Now);

        Assert.False(status.IsActive);
        Assert.Equal("当前未检测到游戏", status.Title);
        Assert.Equal("进入游戏后会自动识别，无需手动扫描。", status.Description);
    }
}
