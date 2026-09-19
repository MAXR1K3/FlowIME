using System.Runtime.InteropServices;

namespace FlowIME.Windows.Interop;

internal static class Kernel32Native
{
    internal const uint ProcessQueryLimitedInformation = 0x1000;
    internal const int ErrorInsufficientBuffer = 122;
    internal const int AppModelErrorNoPackage = 15700;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint GetModuleHandleW(string? moduleName);

    [DllImport("kernel32.dll")]
    internal static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern nint OpenProcess(
        uint desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(nint handle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool QueryFullProcessImageNameW(
        nint process,
        uint flags,
        [Out] char[] executableName,
        ref uint size);


    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetApplicationUserModelId(
        nint process,
        ref uint applicationUserModelIdLength,
        [Out] char[]? applicationUserModelId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetPackageFamilyName(
        nint process,
        ref uint packageFamilyNameLength,
        [Out] char[]? packageFamilyName);
}
