using FlowIME.Core.Context;

namespace FlowIME.App.Services;

internal static class GameProfileTargetValidator
{
    // Call after registry normalization: this gate is deliberately separate from
    // legacy loading so one old unresolved profile cannot discard all profiles.
    internal static bool HasReliableTarget(GameTextEntryProfile profile) =>
        new[] { "path:", "aumid:", "pfn:" }.Any(prefix =>
            profile.ApplicationIdentityKey.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
            profile.ApplicationIdentityKey.Length > prefix.Length) ||
        !string.IsNullOrWhiteSpace(profile.ExecutablePath) ||
        profile.AdditionalExecutablePaths.Any(path => !string.IsNullOrWhiteSpace(path));

    internal static void RequireReliableTarget(GameTextEntryProfile profile)
    {
        if (!HasReliableTarget(profile))
            throw new ArgumentException("请关联实际游戏 EXE；仅进程名不足以可靠识别游戏。", nameof(profile));
    }
}
