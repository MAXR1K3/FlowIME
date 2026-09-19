using FlowIME.Windows.Interop;

namespace FlowIME.Windows.Input;

/// <summary>
/// Activates the Microsoft Pinyin TSF profile for the current desktop and verifies
/// that it became the active keyboard text-service profile before IMM conversion
/// mode is read or changed.
/// </summary>
internal sealed class TsfMicrosoftPinyinProfileActivator : IMicrosoftPinyinProfileActivator
{
    private const uint TfProfileTypeInputProcessor = 0x0001;
    private const uint TfIppmfForSession = 0x20000000;
    private const ushort ChineseSimplifiedLanguageId = 0x0804;

    private static readonly Guid MicrosoftPinyinClsid =
        new("81D4E9C9-1D3B-41BC-9E6C-4B40BF79E35E");

    private static readonly Guid MicrosoftPinyinProfileGuid =
        new("FA550B04-5AD7-411F-A5AC-CA038EC515D7");

    public MicrosoftPinyinProfileActivationResult ActivateForSession()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Microsoft Pinyin profile activation requires Windows.");
        }

        return TsfProfileOperationGate.Run(
            "activate-microsoft-pinyin",
            ActivateForSessionCore);
    }

    private static MicrosoftPinyinProfileActivationResult ActivateForSessionCore()
    {
        using var com = ComInitialization.EnterMta();
        object? instance = null;
        try
        {
            var managerClsid = TsfInterop.ClsidInputProcessorProfiles;
            var managerIid = TsfInterop.IidInputProcessorProfileMgr;
            var createHr = TsfInterop.CoCreateInstance(
                in managerClsid,
                0,
                TsfInterop.ClsctxInprocServer,
                in managerIid,
                out instance);

            if (createHr < 0 || instance is not ITfInputProcessorProfileMgr manager)
            {
                return Failure(
                    createHr,
                    "CoCreateInstance(CLSID_TF_InputProcessorProfiles) failed.");
            }

            var category = TsfInterop.GuidTfcatTipKeyboard;
            var currentHr = manager.GetActiveProfile(in category, out var current);
            if (currentHr == 0 && IsMicrosoftPinyin(current))
            {
                return new MicrosoftPinyinProfileActivationResult(
                    Success: true,
                    HResult: 0,
                    VerifiedActive: true,
                    WasAlreadyActive: true,
                    Error: null);
            }

            var pinyinClsid = MicrosoftPinyinClsid;
            var pinyinProfile = MicrosoftPinyinProfileGuid;
            var activateHr = manager.ActivateProfile(
                TfProfileTypeInputProcessor,
                ChineseSimplifiedLanguageId,
                in pinyinClsid,
                in pinyinProfile,
                0,
                TfIppmfForSession);

            if (activateHr != 0)
            {
                return Failure(
                    activateHr,
                    "ITfInputProcessorProfileMgr.ActivateProfile failed.");
            }

            var verifyHr = manager.GetActiveProfile(in category, out var active);
            if (verifyHr != 0)
            {
                return new MicrosoftPinyinProfileActivationResult(
                    Success: false,
                    HResult: verifyHr,
                    VerifiedActive: false,
                    WasAlreadyActive: false,
                    Error: "Microsoft Pinyin activation could not be verified.");
            }

            var verified = IsMicrosoftPinyin(active);

            return verified
                ? new MicrosoftPinyinProfileActivationResult(
                    Success: true,
                    HResult: 0,
                    VerifiedActive: true,
                    WasAlreadyActive: false,
                    Error: null)
                : new MicrosoftPinyinProfileActivationResult(
                    Success: false,
                    HResult: 0,
                    VerifiedActive: false,
                    WasAlreadyActive: false,
                    Error: "A different input profile remained active after activation.");
        }
        finally
        {
            TsfComObject.Release(ref instance, "activate-microsoft-pinyin");
        }
    }

    private static bool IsMicrosoftPinyin(TfInputProcessorProfile profile) =>
        profile.ProfileType == TfProfileTypeInputProcessor &&
        profile.LanguageId == ChineseSimplifiedLanguageId &&
        profile.Clsid == MicrosoftPinyinClsid &&
        profile.ProfileGuid == MicrosoftPinyinProfileGuid;

    private static MicrosoftPinyinProfileActivationResult Failure(
        int hResult,
        string error) =>
        new(
            Success: false,
            HResult: hResult,
            VerifiedActive: false,
            WasAlreadyActive: false,
            Error: error);
}
