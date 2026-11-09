namespace FlowIME.Core.Settings;

public sealed record AppSettingsSnapshot(
    GameplayHotkeyGuardSettings GameplayHotkeyGuard,
    GameplayKeyboardBaselineSettings GameplayKeyboardBaseline,
    InputStatusOverlaySettings InputStatusOverlay)
{
    public AppStateMemorySettings AppStateMemory { get; init; } = AppStateMemorySettings.Default;

    public static AppSettingsSnapshot Default { get; } =
        new(
            GameplayHotkeyGuardSettings.Default,
            GameplayKeyboardBaselineSettings.Default,
            InputStatusOverlaySettings.Default);
}
