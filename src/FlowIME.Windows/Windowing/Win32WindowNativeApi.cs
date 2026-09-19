using System.ComponentModel;
using System.Diagnostics;
using FlowIME.Windows.Interop;

namespace FlowIME.Windows.Windowing;

internal sealed class Win32WindowNativeApi : IWindowNativeApi
{
    private readonly ProcessPathResolver _pathResolver = new();
    private readonly PackageFamilyNameResolver _packageResolver = new();
    private readonly ApplicationUserModelIdResolver _aumidResolver = new();

    public uint GetWindowThreadProcessId(nint hwnd, out uint processId) =>
        User32Native.GetWindowThreadProcessId(hwnd, out processId);

    public string? GetWindowTitle(nint hwnd)
    {
        var length = User32Native.GetWindowTextLengthW(hwnd);
        if (length <= 0)
        {
            return null;
        }

        var buffer = new char[length + 1];
        var copied = User32Native.GetWindowTextW(hwnd, buffer, buffer.Length);
        return copied > 0 ? new string(buffer, 0, copied) : null;
    }

    public string? GetWindowClass(nint hwnd)
    {
        var buffer = new char[256];
        var copied = User32Native.GetClassNameW(hwnd, buffer, buffer.Length);
        return copied > 0 ? new string(buffer, 0, copied) : null;
    }

    public string? GetExecutablePath(uint processId) =>
        _pathResolver.TryResolve(processId);

    public string? GetProcessName(uint processId, string? executablePath)
    {
        if (!string.IsNullOrWhiteSpace(executablePath))
        {
            return Path.GetFileNameWithoutExtension(executablePath);
        }

        try
        {
            using var process = Process.GetProcessById(checked((int)processId));
            return process.ProcessName;
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (Win32Exception)
        {
            return null;
        }
    }

    public string? GetPackageFamilyName(uint processId) =>
        _packageResolver.TryResolve(processId);

    public string? GetApplicationUserModelId(uint processId) =>
        _aumidResolver.TryResolve(processId);
}
