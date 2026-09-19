namespace FlowIME.Windows.Input;

internal static class MicrosoftPinyinProfileIdentity
{
    private const uint TfProfileTypeInputProcessor = 0x0001;
    private const ushort ChineseSimplifiedLanguageId = 0x0804;

    private static readonly Guid MicrosoftPinyinClsid =
        new("81D4E9C9-1D3B-41BC-9E6C-4B40BF79E35E");

    private static readonly Guid MicrosoftPinyinProfileGuid =
        new("FA550B04-5AD7-411F-A5AC-CA038EC515D7");

    public static bool IsMatch(TsfProfileSnapshot profile) =>
        profile.Success &&
        profile.ProfileType == TfProfileTypeInputProcessor &&
        profile.LanguageId == ChineseSimplifiedLanguageId &&
        profile.Clsid == MicrosoftPinyinClsid &&
        profile.ProfileGuid == MicrosoftPinyinProfileGuid;
}
