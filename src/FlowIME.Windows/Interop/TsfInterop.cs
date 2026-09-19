using System.Runtime.InteropServices;

namespace FlowIME.Windows.Interop;

internal static class TsfInterop
{
    internal const uint ClsctxInprocServer = 0x1;
    internal const uint CoinitMultithreaded = 0x0;
    internal const int RpcEChangedMode = unchecked((int)0x80010106);

    internal static readonly Guid ClsidInputProcessorProfiles =
        new("33C53A50-F456-4884-B049-85FD643ECFED");

    internal static readonly Guid IidInputProcessorProfileMgr =
        new("71C6E74C-0F28-11D8-A82A-00065B84435C");

    internal static readonly Guid GuidTfcatTipKeyboard =
        new("34745C63-B2F0-4784-8B67-5E12C8701A31");

    internal static readonly Guid ClsidThreadMgr =
        new("529A9E6B-6587-4F23-AB9E-9C7D683E3C50");

    internal static readonly Guid IidThreadMgr =
        new("AA80E801-2021-11D2-93E0-0060B067B86E");

    internal static readonly Guid GuidCompartmentKeyboardOpenClose =
        new("58273AAD-01BB-4164-95C6-755BA0B5162D");

    internal static readonly Guid GuidCompartmentKeyboardInputModeConversion =
        new("CCF05DD8-4A87-11D7-A6E2-00065B84435C");

    [DllImport("ole32.dll")]
    internal static extern int CoInitializeEx(nint reserved, uint coInit);

    [DllImport("ole32.dll")]
    internal static extern void CoUninitialize();

    [DllImport("ole32.dll")]
    internal static extern int CoCreateInstance(
        in Guid rclsid,
        nint outer,
        uint clsContext,
        in Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out object instance);
}

[StructLayout(LayoutKind.Sequential)]
internal struct TfInputProcessorProfile
{
    internal uint ProfileType;
    internal ushort LanguageId;
    internal Guid Clsid;
    internal Guid ProfileGuid;
    internal Guid CategoryId;
    internal nint SubstituteKeyboardLayout;
    internal uint Capabilities;
    internal nint KeyboardLayout;
    internal uint Flags;
}

[ComImport]
[Guid("71C6E74C-0F28-11D8-A82A-00065B84435C")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ITfInputProcessorProfileMgr
{
    [PreserveSig]
    int ActivateProfile(
        uint profileType,
        ushort languageId,
        in Guid clsid,
        in Guid profileGuid,
        nint keyboardLayout,
        uint flags);

    [PreserveSig]
    int DeactivateProfile(
        uint profileType,
        ushort languageId,
        in Guid clsid,
        in Guid profileGuid,
        nint keyboardLayout,
        uint flags);

    [PreserveSig]
    int GetProfile(
        uint profileType,
        ushort languageId,
        in Guid clsid,
        in Guid profileGuid,
        nint keyboardLayout,
        out TfInputProcessorProfile profile);

    [PreserveSig]
    int EnumProfiles(ushort languageId, out nint enumerator);

    [PreserveSig]
    int ReleaseInputProcessor(in Guid clsid, uint flags);

    [PreserveSig]
    int RegisterProfile(
        in Guid clsid,
        ushort languageId,
        in Guid profileGuid,
        [MarshalAs(UnmanagedType.LPWStr)] string description,
        uint descriptionLength,
        [MarshalAs(UnmanagedType.LPWStr)] string iconFile,
        uint iconFileLength,
        uint iconIndex,
        nint substituteKeyboardLayout,
        uint preferredLayout,
        [MarshalAs(UnmanagedType.Bool)] bool enabledByDefault,
        uint flags);

    [PreserveSig]
    int UnregisterProfile(
        in Guid clsid,
        ushort languageId,
        in Guid profileGuid,
        uint flags);

    [PreserveSig]
    int GetActiveProfile(
        in Guid categoryId,
        out TfInputProcessorProfile profile);
}

[ComImport]
[Guid("AA80E801-2021-11D2-93E0-0060B067B86E")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ITfThreadMgr
{
    [PreserveSig]
    int Activate(out uint clientId);

    [PreserveSig]
    int Deactivate();

    [PreserveSig]
    int CreateDocumentMgr(out nint documentManager);

    [PreserveSig]
    int EnumDocumentMgrs(out nint enumerator);

    [PreserveSig]
    int GetFocus(out nint documentManager);

    [PreserveSig]
    int SetFocus(nint documentManager);

    [PreserveSig]
    int AssociateFocus(
        nint hwnd,
        nint newDocumentManager,
        out nint previousDocumentManager);

    [PreserveSig]
    int IsThreadFocus([MarshalAs(UnmanagedType.Bool)] out bool threadFocus);

    [PreserveSig]
    int GetFunctionProvider(in Guid clsid, out nint functionProvider);

    [PreserveSig]
    int EnumFunctionProviders(out nint enumerator);

    [PreserveSig]
    int GetGlobalCompartment([MarshalAs(UnmanagedType.Interface)] out ITfCompartmentMgr manager);
}

[ComImport]
[Guid("7DCF57AC-18AD-438B-824D-979BFFB74B7C")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ITfCompartmentMgr
{
    [PreserveSig]
    int GetCompartment(
        in Guid compartmentId,
        [MarshalAs(UnmanagedType.Interface)] out ITfCompartment compartment);

    [PreserveSig]
    int ClearCompartment(uint clientId, in Guid compartmentId);

    [PreserveSig]
    int EnumCompartments(out nint enumerator);
}

[ComImport]
[Guid("BB08F7A9-607A-4384-8623-056892B64371")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ITfCompartment
{
    [PreserveSig]
    int SetValue(uint clientId, [In, MarshalAs(UnmanagedType.Struct)] ref object value);

    [PreserveSig]
    int GetValue([MarshalAs(UnmanagedType.Struct)] out object value);
}
