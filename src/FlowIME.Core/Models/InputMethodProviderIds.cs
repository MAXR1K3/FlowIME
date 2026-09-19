namespace FlowIME.Core.Models;

/// <summary>
/// Persistence-facing IDs for built-in input-method providers. Once written to
/// rules.json these values must remain stable across releases.
/// </summary>
public static class InputMethodProviderIds
{
    public const string MicrosoftPinyin = "microsoft-pinyin";
    public const string WeChat = "wechat-input-method";

    public static string Normalize(string? providerId) =>
        string.IsNullOrWhiteSpace(providerId)
            ? MicrosoftPinyin
            : providerId.Trim();
}
