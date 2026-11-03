using FlowIME.Core.Models;
using FlowIME.Core.Rules;

namespace FlowIME.App.Services;

internal enum QuickRuleChoice
{
    Chinese,
    English,
    Keep,
    Remove
}

internal enum TrayCommand
{
    None,
    Open,
    OpenRules,
    QuickRuleChinese,
    QuickRuleEnglish,
    QuickRuleKeep,
    QuickRuleRemove,
    PauseFifteenMinutes,
    PauseOneHour,
    PauseUntilResumed,
    Resume,
    Exit
}

/// <summary>
/// One row of the tray context menu. Text after a tab is drawn right-aligned
/// by Windows, which the menu uses for short secondary hints.
/// </summary>
internal sealed record TrayMenuItem(
    string Label,
    TrayCommand Command = TrayCommand.None,
    bool IsEnabled = true,
    bool IsChecked = false,
    bool IsDefault = false,
    IReadOnlyList<TrayMenuItem>? Children = null)
{
    internal static TrayMenuItem Separator { get; } = new(string.Empty);

    internal bool IsSeparator => Label.Length == 0;
}

internal sealed record TrayMenuState(
    bool AutomationEnabled,
    DateTimeOffset? PausedUntil,
    QuickRuleTarget? QuickRuleTarget);

/// <summary>
/// Builds the tray context menu: open the app, set a rule for the app the user
/// just left, pause or resume automation, exit. Kept free of Win32 so the
/// layout can be tested.
/// </summary>
internal static class TrayMenu
{
    private const int MaxApplicationNameLength = 24;

    internal static IReadOnlyList<TrayMenuItem> Build(TrayMenuState state, TimeZoneInfo? timeZone = null) =>
    [
        new("打开 FlowIME", TrayCommand.Open, IsDefault: true),
        TrayMenuItem.Separator,
        BuildQuickRule(state.QuickRuleTarget),
        TrayMenuItem.Separator,
        BuildAutomation(state, timeZone ?? TimeZoneInfo.Local),
        TrayMenuItem.Separator,
        new("退出 FlowIME", TrayCommand.Exit)
    ];

    internal static QuickRuleChoice? ChoiceFor(TrayCommand command) => command switch
    {
        TrayCommand.QuickRuleChinese => QuickRuleChoice.Chinese,
        TrayCommand.QuickRuleEnglish => QuickRuleChoice.English,
        TrayCommand.QuickRuleKeep => QuickRuleChoice.Keep,
        TrayCommand.QuickRuleRemove => QuickRuleChoice.Remove,
        _ => null
    };

    internal static TimeSpan? PauseDurationFor(TrayCommand command) => command switch
    {
        TrayCommand.PauseFifteenMinutes => TimeSpan.FromMinutes(15),
        TrayCommand.PauseOneHour => TimeSpan.FromHours(1),
        _ => null
    };

    /// <summary>Tooltip shown when hovering the tray icon.</summary>
    internal static string DescribeStatus(bool automationEnabled, DateTimeOffset? pausedUntil, TimeZoneInfo? timeZone = null) =>
        $"FlowIME · {DescribeAutomation(automationEnabled, pausedUntil, timeZone)}";

    /// <summary>Automation state shown in the tooltip and the tray flyout header.</summary>
    internal static string DescribeAutomation(bool automationEnabled, DateTimeOffset? pausedUntil, TimeZoneInfo? timeZone = null) =>
        automationEnabled
            ? "自动切换运行中"
            : pausedUntil is { } until
                ? $"已暂停，{FormatTime(until, timeZone ?? TimeZoneInfo.Local)} 自动恢复"
                : "自动切换已暂停";

    /// <summary>When a paused automation comes back, for the tray flyout.</summary>
    internal static string DescribeResume(DateTimeOffset? pausedUntil, TimeZoneInfo? timeZone = null) =>
        pausedUntil is { } until
            ? $"{FormatTime(until, timeZone ?? TimeZoneInfo.Local)} 自动恢复"
            : "需要手动恢复";

    internal static string DescribeRule(ApplicationRule? rule) => rule switch
    {
        null => "未设置",
        { Enabled: false } => "规则已停用",
        _ => rule.Action switch
        {
            InputAction.Chinese => "进入时：中文",
            InputAction.English => "进入时：英文",
            InputAction.StandardUsKeyboard => "进入时：美式键盘",
            _ => "进入时：保持不变"
        }
    };

    private static TrayMenuItem BuildQuickRule(QuickRuleTarget? target)
    {
        if (target is null)
        {
            return new("最近使用的应用\t暂无", IsEnabled: false);
        }

        var rule = target.ExistingRule;
        var current = rule is { Enabled: true } ? rule.Action : (InputAction?)null;
        var children = new List<TrayMenuItem>
        {
            new("进入时切换到中文", TrayCommand.QuickRuleChinese, IsChecked: current == InputAction.Chinese),
            new("进入时切换到英文", TrayCommand.QuickRuleEnglish, IsChecked: current == InputAction.English),
            new("进入时保持不变", TrayCommand.QuickRuleKeep, IsChecked: current == InputAction.Keep),
            TrayMenuItem.Separator
        };

        if (rule is not null)
        {
            children.Add(new("移除此应用的规则", TrayCommand.QuickRuleRemove));
        }

        children.Add(new("在 FlowIME 中管理规则…", TrayCommand.OpenRules));

        return new($"{Shorten(target.DisplayName)}\t{DescribeRule(rule)}", Children: children);
    }

    private static TrayMenuItem BuildAutomation(TrayMenuState state, TimeZoneInfo timeZone)
    {
        if (!state.AutomationEnabled)
        {
            var hint = state.PausedUntil is { } until
                ? $"{FormatTime(until, timeZone)} 自动恢复"
                : "已暂停";
            return new($"恢复自动切换\t{hint}", TrayCommand.Resume);
        }

        return new(
            "暂停自动切换",
            Children:
            [
                new("暂停 15 分钟", TrayCommand.PauseFifteenMinutes),
                new("暂停 1 小时", TrayCommand.PauseOneHour),
                new("暂停，直到手动恢复", TrayCommand.PauseUntilResumed)
            ]);
    }

    private static string FormatTime(DateTimeOffset value, TimeZoneInfo timeZone) =>
        TimeZoneInfo.ConvertTime(value, timeZone).ToString("HH:mm");

    // Menu text treats '&' as a mnemonic prefix and a tab as a column break.
    private static string Shorten(string name)
    {
        var clean = name.Replace('\t', ' ');
        if (clean.Length > MaxApplicationNameLength)
        {
            clean = clean[..(MaxApplicationNameLength - 1)] + "…";
        }

        return clean.Replace("&", "&&", StringComparison.Ordinal);
    }
}
