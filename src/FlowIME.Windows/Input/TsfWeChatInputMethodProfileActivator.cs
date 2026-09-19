using FlowIME.Windows.Interop;

namespace FlowIME.Windows.Input;

/// <summary>
/// Activates the exact WeChat Input Method TSF profile validated during P5B
/// discovery and verifies that it became the active keyboard text-service profile.
/// </summary>
internal sealed class TsfWeChatInputMethodProfileActivator : IWeChatInputMethodProfileActivator
{
    private const uint TfProfileTypeInputProcessor = 0x0001;
    private const uint TfIppmfForSession = 0x20000000;
    private const ushort ChineseSimplifiedLanguageId = 0x0804;

    public WeChatInputMethodProfileActivationResult ActivateForSession()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "WeChat Input Method profile activation requires Windows.");
        }

        return TsfProfileOperationGate.Run(
            "activate-wechat",
            ActivateForSessionCore);
    }

    private static WeChatInputMethodProfileActivationResult ActivateForSessionCore()
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
            var wasAlreadyActive = currentHr == 0 && IsWeChat(current);

            var clsid = WeChatInputMethodProfileIdentity.Clsid;
            var profileGuid = WeChatInputMethodProfileIdentity.ProfileGuid;
            // Do not short-circuit when the session already reports WeChat active.
            // Per-window input-language history can leave the newly focused GUI
            // thread on a different profile (notably the US keyboard). FORSESSION
            // is intentionally reissued so TSF can propagate the requested profile
            // to all threads in the current desktop.
            var activateHr = manager.ActivateProfile(
                TfProfileTypeInputProcessor,
                ChineseSimplifiedLanguageId,
                in clsid,
                in profileGuid,
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
                return new WeChatInputMethodProfileActivationResult(
                    Success: false,
                    HResult: verifyHr,
                    VerifiedActive: false,
                    WasAlreadyActive: false,
                    Error: "WeChat Input Method activation could not be verified.");
            }

            return IsWeChat(active)
                ? new WeChatInputMethodProfileActivationResult(
                    Success: true,
                    HResult: 0,
                    VerifiedActive: true,
                    WasAlreadyActive: wasAlreadyActive,
                    Error: null)
                : new WeChatInputMethodProfileActivationResult(
                    Success: false,
                    HResult: 0,
                    VerifiedActive: false,
                    WasAlreadyActive: false,
                    Error: "A different input profile remained active after activation.");
        }
        finally
        {
            TsfComObject.Release(ref instance, "activate-wechat");
        }
    }

    private static bool IsWeChat(TfInputProcessorProfile profile) =>
        profile.ProfileType == TfProfileTypeInputProcessor &&
        profile.LanguageId == ChineseSimplifiedLanguageId &&
        profile.Clsid == WeChatInputMethodProfileIdentity.Clsid &&
        profile.ProfileGuid == WeChatInputMethodProfileIdentity.ProfileGuid;

    private static WeChatInputMethodProfileActivationResult Failure(
        int hResult,
        string error) =>
        new(
            Success: false,
            HResult: hResult,
            VerifiedActive: false,
            WasAlreadyActive: false,
            Error: error);
}
