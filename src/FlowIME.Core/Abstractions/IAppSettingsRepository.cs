using FlowIME.Core.Settings;

namespace FlowIME.Core.Abstractions;

public interface IAppSettingsRepository
{
    ValueTask<AppSettingsSnapshot> GetAsync(
        CancellationToken cancellationToken = default);

    ValueTask SaveGameplayHotkeyGuardAsync(
        GameplayHotkeyGuardSettings settings,
        CancellationToken cancellationToken = default);

    ValueTask SaveGameplayKeyboardBaselineAsync(
        GameplayKeyboardBaselineSettings settings,
        CancellationToken cancellationToken = default);

    ValueTask SaveInputStatusOverlayAsync(
        InputStatusOverlaySettings settings,
        CancellationToken cancellationToken = default);
}
