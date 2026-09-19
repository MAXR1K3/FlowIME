using System.Runtime.InteropServices;
using FlowIME.Windows.Interop;

namespace FlowIME.Windows.Windowing;

internal sealed class ProcessPathResolver
{
    private const int InitialBufferLength = 1024;
    private const int MaximumWindowsPathBufferLength = 32768;

    public string? TryResolve(uint processId)
    {
        var process = Kernel32Native.OpenProcess(
            Kernel32Native.ProcessQueryLimitedInformation,
            false,
            processId);

        if (process == 0)
        {
            return null;
        }

        try
        {
            var path = TryQuery(process, InitialBufferLength, out var nativeError);
            if (path is not null || nativeError != Kernel32Native.ErrorInsufficientBuffer)
            {
                return path;
            }

            return TryQuery(process, MaximumWindowsPathBufferLength, out _);
        }
        finally
        {
            _ = Kernel32Native.CloseHandle(process);
        }
    }

    private static string? TryQuery(nint process, int bufferLength, out int nativeError)
    {
        var buffer = new char[bufferLength];
        var length = (uint)buffer.Length;
        Marshal.SetLastPInvokeError(0);
        if (!Kernel32Native.QueryFullProcessImageNameW(process, 0, buffer, ref length))
        {
            nativeError = Marshal.GetLastPInvokeError();
            return null;
        }

        nativeError = 0;
        return new string(buffer, 0, checked((int)length));
    }
}
