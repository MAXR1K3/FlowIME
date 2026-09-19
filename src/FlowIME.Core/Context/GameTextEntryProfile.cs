using FlowIME.Core.Models;

namespace FlowIME.Core.Context;

[Flags]
public enum GameTextEntryDetectionMode
{
    None = 0,
    StandardTextControl = 1 << 0,
    HotkeyProfile = 1 << 1,
    Manual = 1 << 2,
    Adapter = 1 << 3
}

[Flags]
public enum GameTextEntryModifierKeys
{
    None = 0,
    Control = 1 << 0,
    Alt = 1 << 1,
    Shift = 1 << 2,
    Windows = 1 << 3
}

/// <summary>
/// Windows virtual-key gesture used by per-game chat profiles. P8C.2 observes only
/// gestures explicitly configured by the user and never consumes the original key.
/// </summary>
public sealed record GameTextEntryKeyGesture(
    uint VirtualKey,
    GameTextEntryModifierKeys Modifiers = GameTextEntryModifierKeys.None);

/// <summary>
/// Immutable per-game text-entry profile contract. Profiles are stored separately
/// from rules/settings and keyed by the stable P8A application identity.
/// </summary>
public sealed record GameTextEntryProfile
{
    public required string Id { get; init; }

    public required string ApplicationIdentityKey { get; init; }

    public string ApplicationDisplayName { get; init; } = "Game";

    public string? ExecutablePath { get; init; }

    public bool Enabled { get; init; } = true;

    public GameTextEntryDetectionMode DetectionMode { get; init; } =
        GameTextEntryDetectionMode.StandardTextControl;

    public string? ProviderId { get; init; }

    public InputAction Action { get; init; } = InputAction.Keep;

    public IReadOnlyList<GameTextEntryKeyGesture> EnterGestures { get; init; } =
        Array.Empty<GameTextEntryKeyGesture>();

    public IReadOnlyList<GameTextEntryKeyGesture> ExitGestures { get; init; } =
        Array.Empty<GameTextEntryKeyGesture>();

    public static GameTextEntryProfile CreateDefault(
        string applicationIdentityKey,
        string applicationDisplayName) =>
        new()
        {
            Id = $"game-chat:{applicationIdentityKey}",
            ApplicationIdentityKey = applicationIdentityKey,
            ApplicationDisplayName = applicationDisplayName,
            Enabled = true,
            DetectionMode = GameTextEntryDetectionMode.StandardTextControl |
                GameTextEntryDetectionMode.HotkeyProfile,
            EnterGestures = [new GameTextEntryKeyGesture(0x0D)],
            ExitGestures =
            [
                new GameTextEntryKeyGesture(0x0D),
                new GameTextEntryKeyGesture(0x1B)
            ]
        };
}

/// <summary>
/// Lock-free read / replace-all registry for persisted per-game text-entry profiles.
/// </summary>
public sealed class GameTextEntryProfileRegistry
{
    private GameTextEntryProfile[] _profiles = [];

    public IReadOnlyList<GameTextEntryProfile> Profiles =>
        Volatile.Read(ref _profiles).ToArray();

    public void ReplaceProfiles(IEnumerable<GameTextEntryProfile>? profiles)
    {
        var next = (profiles ?? Array.Empty<GameTextEntryProfile>())
            .Select(NormalizeAndValidate)
            .OrderBy(profile => profile.Id, StringComparer.Ordinal)
            .ToArray();

        var duplicateId = next
            .GroupBy(profile => profile.Id, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateId is not null)
        {
            throw new ArgumentException(
                $"Duplicate GameTextEntry profile ID '{duplicateId.Key}'.",
                nameof(profiles));
        }

        var duplicateApplication = next
            .Where(profile => profile.Enabled)
            .GroupBy(profile => profile.ApplicationIdentityKey, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateApplication is not null)
        {
            throw new ArgumentException(
                $"Multiple enabled GameTextEntry profiles target '{duplicateApplication.Key}'.",
                nameof(profiles));
        }

        Interlocked.Exchange(ref _profiles, next);
    }

    public GameTextEntryProfile? Resolve(ApplicationIdentity application)
    {
        ArgumentNullException.ThrowIfNull(application);
        return Volatile.Read(ref _profiles)
            .FirstOrDefault(profile =>
                profile.Enabled &&
                StringComparer.Ordinal.Equals(
                    profile.ApplicationIdentityKey,
                    application.Key));
    }

    private static GameTextEntryProfile NormalizeAndValidate(GameTextEntryProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var id = NormalizeRequired(profile.Id, nameof(profile.Id));
        var applicationKey = NormalizeRequired(
            profile.ApplicationIdentityKey,
            nameof(profile.ApplicationIdentityKey));

        if (profile.DetectionMode == GameTextEntryDetectionMode.None)
        {
            throw new ArgumentException(
                "GameTextEntry profile detection mode cannot be None.",
                nameof(profile));
        }

        var displayName = NormalizeOptional(profile.ApplicationDisplayName, "Game");

        var providerId = profile.Action == InputAction.Keep ||
            string.IsNullOrWhiteSpace(profile.ProviderId)
            ? null
            : InputMethodProviderIds.Normalize(profile.ProviderId);
        if (profile.Action is InputAction.Chinese or InputAction.English &&
            string.IsNullOrWhiteSpace(providerId))
        {
            throw new ArgumentException(
                "Chinese/English GameTextEntry targets require a provider ID.",
                nameof(profile));
        }

        var enterGestures = (profile.EnterGestures ?? Array.Empty<GameTextEntryKeyGesture>())
            .Distinct()
            .ToArray();
        var exitGestures = (profile.ExitGestures ?? Array.Empty<GameTextEntryKeyGesture>())
            .Distinct()
            .ToArray();

        if (profile.DetectionMode.HasFlag(GameTextEntryDetectionMode.HotkeyProfile) &&
            (enterGestures.Length == 0 || exitGestures.Length == 0))
        {
            throw new ArgumentException(
                "Hotkey GameTextEntry profiles require at least one enter and exit gesture.",
                nameof(profile));
        }

        return profile with
        {
            Id = id,
            ApplicationIdentityKey = applicationKey,
            ApplicationDisplayName = displayName,
            ExecutablePath = string.IsNullOrWhiteSpace(profile.ExecutablePath)
                ? null
                : profile.ExecutablePath.Trim(),
            ProviderId = providerId,
            EnterGestures = enterGestures,
            ExitGestures = exitGestures
        };
    }

    private static string NormalizeOptional(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return value.Trim()
            .Replace('|', '_')
            .Replace('\r', ' ')
            .Replace('\n', ' ');
    }

    private static string NormalizeRequired(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value cannot be empty.", parameterName);
        }

        return value.Trim();
    }
}
