using FlowIME.Core.Abstractions;
using FlowIME.Core.Models;

namespace FlowIME.Windows.Windowing;

public sealed class WindowResolver : IWindowResolver
{
    private readonly IWindowNativeApi _native;

    public WindowResolver()
        : this(new Win32WindowNativeApi())
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Window resolution requires Windows.");
        }
    }

    internal WindowResolver(IWindowNativeApi native)
    {
        _native = native ?? throw new ArgumentNullException(nameof(native));
    }

    public ValueTask<WindowContext?> ResolveAsync(
        nint hwnd,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (hwnd == 0)
        {
            return ValueTask.FromResult<WindowContext?>(null);
        }

        var threadId = _native.GetWindowThreadProcessId(hwnd, out var processId);
        if (threadId == 0 || processId == 0)
        {
            return ValueTask.FromResult<WindowContext?>(null);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var executablePath = _native.GetExecutablePath(processId);
        var processName = _native.GetProcessName(processId, executablePath) ?? string.Empty;

        var context = new WindowContext(
            Hwnd: hwnd,
            ProcessId: processId,
            ThreadId: threadId,
            ProcessName: processName,
            ExecutablePath: executablePath,
            WindowTitle: _native.GetWindowTitle(hwnd),
            WindowClass: _native.GetWindowClass(hwnd),
            PackageFamilyName: _native.GetPackageFamilyName(processId),
            ApplicationUserModelId: _native.GetApplicationUserModelId(processId));

        return ValueTask.FromResult<WindowContext?>(context);
    }
}
