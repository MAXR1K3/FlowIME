namespace FlowIME.Core.Models;

public sealed record RunningApplication(
    string DisplayName,
    string ProcessName,
    string ExecutablePath,
    nint MainWindowHandle,
    uint ProcessId,
    string? PackageFamilyName = null,
    string? ApplicationUserModelId = null);
