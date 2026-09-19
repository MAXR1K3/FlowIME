using FlowIME.Windows.Interop;

namespace FlowIME.Windows.Windowing;

internal sealed class ApplicationUserModelIdResolver
{
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
            uint length = 0;
            var firstResult = Kernel32Native.GetApplicationUserModelId(
                process,
                ref length,
                null);
            if (firstResult != Kernel32Native.ErrorInsufficientBuffer || length == 0)
            {
                return null;
            }

            var buffer = new char[checked((int)length)];
            var secondResult = Kernel32Native.GetApplicationUserModelId(
                process,
                ref length,
                buffer);
            if (secondResult != 0 || length == 0)
            {
                return null;
            }

            var stringLength = checked((int)length);
            if (stringLength > 0 && buffer[stringLength - 1] == '\0')
            {
                stringLength--;
            }

            return stringLength == 0
                ? null
                : new string(buffer, 0, stringLength);
        }
        finally
        {
            _ = Kernel32Native.CloseHandle(process);
        }
    }
}
