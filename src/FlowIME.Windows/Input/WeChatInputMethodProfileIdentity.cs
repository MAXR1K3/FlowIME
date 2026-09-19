namespace FlowIME.Windows.Input;

/// <summary>
/// Exact TSF profile identity observed and mutation-validated for WeChat Input
/// Method during P5B discovery. If a future WeChat release changes this identity,
/// detection fails closed instead of applying OpenStatus semantics to another IME.
/// </summary>
public static class WeChatInputMethodProfileIdentity
{
    private const uint TfProfileTypeInputProcessor = 0x0001;
    private const ushort ChineseSimplifiedLanguageId = 0x0804;

    public static readonly Guid Clsid =
        new("86598FB9-66A2-463E-B9C2-AEB906D477AD");

    public static readonly Guid ProfileGuid =
        new("607FDF85-FCC8-4DBD-A365-41296F980C9C");

    public static bool IsMatch(TsfProfileSnapshot profile) =>
        profile.Success &&
        profile.ProfileType == TfProfileTypeInputProcessor &&
        profile.LanguageId == ChineseSimplifiedLanguageId &&
        profile.Clsid == Clsid &&
        profile.ProfileGuid == ProfileGuid;
}
