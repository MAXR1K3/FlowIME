namespace FlowIME.Core.Settings;

/// <summary>
/// Gameplay keyboard baseline preferences. When enabled and the standard US
/// keyboard layout is available in the current Windows session, FlowIME may
/// switch a positively classified gameplay window to that layout and suppress
/// ordinary application/global IME rules for the duration of gameplay.
/// </summary>
public sealed record GameplayKeyboardBaselineSettings
{
    public bool Enabled { get; init; } = true;

    public static GameplayKeyboardBaselineSettings Default { get; } = new();
}
