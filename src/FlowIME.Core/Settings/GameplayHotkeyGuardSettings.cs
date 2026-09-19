namespace FlowIME.Core.Settings;

/// <summary>
/// User-facing gameplay hotkey suppression preferences. The defaults intentionally
/// cover the two modern shortcuts that most often cause accidental IME changes
/// during gameplay. Legacy language hotkeys are opt-in because Ctrl+Shift / Alt+Shift
/// can also be legitimate game binds.
/// </summary>
public sealed record GameplayHotkeyGuardSettings
{
    public bool Enabled { get; init; } = true;

    public bool BlockWinSpace { get; init; } = true;

    public bool BlockCtrlSpace { get; init; } = true;

    public bool BlockLegacyLanguageHotkeys { get; init; }

    public static GameplayHotkeyGuardSettings Default { get; } = new();
}
