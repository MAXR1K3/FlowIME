using FlowIME.Windows.Input;

namespace FlowIME.Windows.Tests.Input;

public sealed class WeChatInputMethodProfileIdentityTests
{
    [Fact]
    public void IsMatch_accepts_the_profile_observed_during_p5b_discovery()
    {
        var profile = CreateProfile(
            languageId: 0x0804,
            clsid: WeChatInputMethodProfileIdentity.Clsid,
            profileGuid: WeChatInputMethodProfileIdentity.ProfileGuid);

        Assert.True(WeChatInputMethodProfileIdentity.IsMatch(profile));
    }

    [Fact]
    public void IsMatch_rejects_same_language_with_different_profile()
    {
        var profile = CreateProfile(
            languageId: 0x0804,
            clsid: Guid.NewGuid(),
            profileGuid: WeChatInputMethodProfileIdentity.ProfileGuid);

        Assert.False(WeChatInputMethodProfileIdentity.IsMatch(profile));
    }

    [Fact]
    public void IsMatch_rejects_failed_snapshot()
    {
        var profile = CreateProfile(
            languageId: 0x0804,
            clsid: WeChatInputMethodProfileIdentity.Clsid,
            profileGuid: WeChatInputMethodProfileIdentity.ProfileGuid) with { Success = false };

        Assert.False(WeChatInputMethodProfileIdentity.IsMatch(profile));
    }

    private static TsfProfileSnapshot CreateProfile(
        ushort languageId,
        Guid clsid,
        Guid profileGuid) =>
        new(
            Success: true,
            HResult: 0,
            ProfileType: 1,
            LanguageId: languageId,
            Clsid: clsid,
            ProfileGuid: profileGuid,
            CategoryId: Guid.Empty,
            SubstituteKeyboardLayout: 0,
            Capabilities: 0,
            KeyboardLayout: 0,
            Flags: 0,
            Error: null);
}
