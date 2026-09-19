namespace FlowIME.Core.Rules;

public sealed record ApplicationMatch(
    string? ProcessPath = null,
    string? ProcessName = null,
    string? WindowTitleContains = null,
    string? WindowClass = null,
    string? PackageFamilyName = null,
    string? ApplicationUserModelId = null);
