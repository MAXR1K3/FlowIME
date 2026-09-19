namespace FlowIME.Core.Models;

public sealed record WindowContext(
    nint Hwnd,
    uint ProcessId,
    uint ThreadId,
    string ProcessName,
    string? ExecutablePath,
    string? WindowTitle,
    string? WindowClass,
    string? PackageFamilyName,
    string? ApplicationUserModelId = null);
