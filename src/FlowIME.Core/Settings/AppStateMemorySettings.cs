namespace FlowIME.Core.Settings;

/// <summary>
/// Per-application input mode memory. When enabled, an application that falls
/// through to the global default returns to the Chinese/English mode it was in
/// when the user left it; the global default only applies on the first visit.
/// </summary>
public sealed record AppStateMemorySettings
{
    public bool Enabled { get; init; }

    public static AppStateMemorySettings Default { get; } = new();
}
