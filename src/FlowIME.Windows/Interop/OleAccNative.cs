using System.Runtime.InteropServices;

namespace FlowIME.Windows.Interop;

internal static class OleAccNative
{
    internal const uint ObjIdClient = 0xFFFFFFFC;
    internal const int RoleSystemDocument = 0x0F;
    internal const int RoleSystemText = 0x2A;
    internal const int StateSystemReadOnly = 0x00000040;

    [DllImport("oleacc.dll")]
    internal static extern int AccessibleObjectFromEvent(
        nint hwnd,
        uint objectId,
        uint childId,
        [MarshalAs(UnmanagedType.Interface)] out IAccessibleDispatch? accessible,
        [MarshalAs(UnmanagedType.Struct)] out object? childVariant);

    [DllImport("oleacc.dll")]
    internal static extern int AccessibleObjectFromWindow(
        nint hwnd,
        uint objectId,
        ref Guid interfaceId,
        [MarshalAs(UnmanagedType.Interface)] out IAccessibleDispatch? accessible);

    [ComImport]
    [Guid("618736E0-3C3D-11CF-810C-00AA00389B71")]
    [InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
    internal interface IAccessibleDispatch
    {
        [DispId(-5006)]
        [return: MarshalAs(UnmanagedType.Struct)]
        object? get_accRole([In, Optional, MarshalAs(UnmanagedType.Struct)] object? child);

        [DispId(-5007)]
        [return: MarshalAs(UnmanagedType.Struct)]
        object? get_accState([In, Optional, MarshalAs(UnmanagedType.Struct)] object? child);
    }
}
