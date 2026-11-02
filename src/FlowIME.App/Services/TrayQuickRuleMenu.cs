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

internal sealed record TrayMenuEntry(
    uint CommandId,
    string Label,
    bool IsEnabled = true,
    bool IsChecked = false,
    QuickRuleChoice? Choice = null);

/// <summary>
/// Builds the quick-rule section at the top of the tray menu: one click sets what
/// happens when the user returns to the application they just left.
/// </summary>
internal static class TrayQuickRuleMenu
{
    internal const uint HeaderCommand = 1100;
    internal const uint ChineseCommand = 1101;
    internal const uint EnglishCommand = 1102;
    internal const uint KeepCommand = 1103;
    internal const uint RemoveCommand = 1104;

    internal static IReadOnlyList<TrayMenuEntry> Build(QuickRuleTarget? target)
    {
        if (target is null)
        {
            return [new(HeaderCommand, "切换到某个应用后，可在这里为它设置规则", IsEnabled: false)];
        }

        var rule = target.ExistingRule;
        var current = rule is { Enabled: true } ? rule.Action : (InputAction?)null;
        var entries = new List<TrayMenuEntry>
        {
            new(HeaderCommand, $"{target.DisplayName} · {DescribeRule(rule)}", IsEnabled: false),
            new(ChineseCommand, "进入时切换到中文", IsChecked: current == InputAction.Chinese, Choice: QuickRuleChoice.Chinese),
            new(EnglishCommand, "进入时切换到英文", IsChecked: current == InputAction.English, Choice: QuickRuleChoice.English),
            new(KeepCommand, "进入时保持不变", IsChecked: current == InputAction.Keep, Choice: QuickRuleChoice.Keep)
        };

        if (rule is not null)
        {
            entries.Add(new(RemoveCommand, "移除此应用的规则", Choice: QuickRuleChoice.Remove));
        }

        return entries;
    }

    internal static QuickRuleChoice? ChoiceFor(uint commandId) => commandId switch
    {
        ChineseCommand => QuickRuleChoice.Chinese,
        EnglishCommand => QuickRuleChoice.English,
        KeepCommand => QuickRuleChoice.Keep,
        RemoveCommand => QuickRuleChoice.Remove,
        _ => null
    };

    internal static string DescribeRule(ApplicationRule? rule) => rule switch
    {
        null => "未设置规则",
        { Enabled: false } => "规则已停用",
        _ => rule.Action switch
        {
            InputAction.Chinese => "当前：中文",
            InputAction.English => "当前：英文",
            InputAction.StandardUsKeyboard => "当前：美式键盘",
            _ => "当前：保持不变"
        }
    };
}
