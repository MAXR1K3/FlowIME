using FlowIME.Windows.Interop;

namespace FlowIME.Windows.Input;

public sealed class TsfInputProfileInspector : IInputProfileInspector
{
    public TsfProfileSnapshot GetActiveKeyboardProfile()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("TSF profile inspection requires Windows.");
        }

        return TsfProfileOperationGate.Run(
            "inspect-active-profile",
            GetActiveKeyboardProfileCore);
    }

    private static TsfProfileSnapshot GetActiveKeyboardProfileCore()
    {
        using var com = ComInitialization.EnterMta();
        object? instance = null;
        try
        {
            var clsid = TsfInterop.ClsidInputProcessorProfiles;
            var iid = TsfInterop.IidInputProcessorProfileMgr;
            var createHr = TsfInterop.CoCreateInstance(
                in clsid,
                0,
                TsfInterop.ClsctxInprocServer,
                in iid,
                out instance);

            if (createHr < 0 || instance is not ITfInputProcessorProfileMgr manager)
            {
                return Failure(createHr, "CoCreateInstance(CLSID_TF_InputProcessorProfiles) failed.");
            }

            var category = TsfInterop.GuidTfcatTipKeyboard;
            var hr = manager.GetActiveProfile(in category, out var profile);
            if (hr != 0)
            {
                return Failure(hr, "ITfInputProcessorProfileMgr.GetActiveProfile failed or returned no active profile.");
            }

            return new TsfProfileSnapshot(
                true,
                hr,
                profile.ProfileType,
                profile.LanguageId,
                profile.Clsid,
                profile.ProfileGuid,
                profile.CategoryId,
                profile.SubstituteKeyboardLayout,
                profile.Capabilities,
                profile.KeyboardLayout,
                profile.Flags,
                null);
        }
        finally
        {
            TsfComObject.Release(ref instance, "inspect-active-profile");
        }
    }

    private static TsfProfileSnapshot Failure(int hr, string error) =>
        new(
            false,
            hr,
            0,
            0,
            Guid.Empty,
            Guid.Empty,
            Guid.Empty,
            0,
            0,
            0,
            0,
            error);
}
