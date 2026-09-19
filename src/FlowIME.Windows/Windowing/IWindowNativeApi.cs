namespace FlowIME.Windows.Windowing;

internal interface IWindowNativeApi
{
    uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    string? GetWindowTitle(nint hwnd);

    string? GetWindowClass(nint hwnd);

    string? GetExecutablePath(uint processId);

    string? GetProcessName(uint processId, string? executablePath);

    string? GetPackageFamilyName(uint processId);

    string? GetApplicationUserModelId(uint processId);
}
