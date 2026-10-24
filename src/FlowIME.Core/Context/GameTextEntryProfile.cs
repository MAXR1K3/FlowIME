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

public enum GameplayClassification
{
    Automatic,
    Game,
    NotGame
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

    public IReadOnlyList<string> AdditionalExecutablePaths { get; init; } =
        Array.Empty<string>();

    public GameplayClassification Classification { get; init; } =
        GameplayClassification.Game;

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

        var duplicateTarget = next
            .SelectMany(profile => TargetTokens(profile).Select(token => (profile, token)))
            .GroupBy(item => item.token, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Select(item => item.profile.Id).Distinct(StringComparer.Ordinal).Count() > 1);
        if (duplicateTarget is not null)
        {
            throw new ArgumentException(
                $"Conflicting GameTextEntry target '{duplicateTarget.Key}'.",
                nameof(profiles));
        }

        Interlocked.Exchange(ref _profiles, next);
    }

    private static IEnumerable<string> TargetTokens(GameTextEntryProfile profile)
    {
        yield return $"identity:{profile.ApplicationIdentityKey}";
        if (profile.ExecutablePath is not null)
        {
            yield return $"path:{profile.ExecutablePath}";
            yield return $"identity:{CreatePathIdentity(profile.ExecutablePath).Key}";
        }
        foreach (var path in profile.AdditionalExecutablePaths)
        {
            yield return $"path:{path}";
            yield return $"identity:{CreatePathIdentity(path).Key}";
        }
    }

    public GameTextEntryProfile? Resolve(ApplicationIdentity application)
    {
        ArgumentNullException.ThrowIfNull(application);
        return Volatile.Read(ref _profiles)
            .FirstOrDefault(profile =>
                profile.Enabled &&
                profile.Classification != GameplayClassification.NotGame &&
                Matches(profile, application));
    }

    public GameTextEntryProfile? ResolveDeclaredProfile(ApplicationIdentity application)
    {
        ArgumentNullException.ThrowIfNull(application);
        return Volatile.Read(ref _profiles)
            .FirstOrDefault(profile => Matches(profile, application));
    }

    private static bool Matches(GameTextEntryProfile profile, ApplicationIdentity application)
    {
        if (IsTrustedIdentity(application.Key) &&
            StringComparer.OrdinalIgnoreCase.Equals(profile.ApplicationIdentityKey, application.Key))
        {
            return true;
        }

        var path = TryNormalizePath(application.ExecutablePath);
        return path is not null &&
            (StringComparer.OrdinalIgnoreCase.Equals(profile.ExecutablePath, path) ||
             profile.AdditionalExecutablePaths.Any(item => StringComparer.OrdinalIgnoreCase.Equals(item, path)));
    }

    private static GameTextEntryProfile NormalizeAndValidate(GameTextEntryProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var id = NormalizeRequired(profile.Id, nameof(profile.Id));
        var applicationKey = NormalizeRequired(
            profile.ApplicationIdentityKey,
            nameof(profile.ApplicationIdentityKey));

        const GameTextEntryDetectionMode knownDetectionModes =
            GameTextEntryDetectionMode.StandardTextControl |
            GameTextEntryDetectionMode.HotkeyProfile |
            GameTextEntryDetectionMode.Manual |
            GameTextEntryDetectionMode.Adapter;
        if (profile.DetectionMode == GameTextEntryDetectionMode.None ||
            (profile.DetectionMode & ~knownDetectionModes) != 0)
        {
            throw new ArgumentException(
                "GameTextEntry profile detection mode is invalid.",
                nameof(profile));
        }

        if (!Enum.IsDefined(profile.Classification))
        {
            throw new ArgumentException(
                "GameTextEntry profile classification is invalid.",
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

        var additionalPaths = (profile.AdditionalExecutablePaths ?? Array.Empty<string>())
            .Select(path => NormalizePath(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return profile with
        {
            Id = id,
            ApplicationIdentityKey = applicationKey,
            ApplicationDisplayName = displayName,
            ExecutablePath = string.IsNullOrWhiteSpace(profile.ExecutablePath)
                ? null
                : NormalizePath(profile.ExecutablePath),
            AdditionalExecutablePaths = Array.AsReadOnly(additionalPaths),
            ProviderId = providerId,
            EnterGestures = enterGestures,
            ExitGestures = exitGestures
        };
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Executable path cannot be empty.", nameof(path));
        }

        var candidate = path.Trim().Trim('"').Replace('/', '\\');
        if (candidate.Length == 0 || IsDevicePath(candidate) ||
            candidate.EndsWith('\\') || !Path.IsPathFullyQualified(candidate))
        {
            throw new ArgumentException(
                $"Executable path '{path}' must be a fully qualified local or UNC .exe path.",
                nameof(path));
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(candidate);
        }
        catch (ArgumentException exception)
        {
            throw new ArgumentException(
                $"Executable path '{path}' is invalid.",
                nameof(path),
                exception);
        }

        if (!fullPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
            Directory.Exists(fullPath))
        {
            throw new ArgumentException(
                $"Executable path '{path}' must identify an .exe file.",
                nameof(path));
        }

        return fullPath.TrimEnd('\\');
    }

    private static string? TryNormalizePath(string? path) =>
        string.IsNullOrWhiteSpace(path) ? null : TryNormalizePathValue(path);

    private static string? TryNormalizePathValue(string path)
    {
        try
        {
            return NormalizePath(path);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static bool IsTrustedIdentity(string key) =>
        key.StartsWith("path:", StringComparison.OrdinalIgnoreCase) ||
        key.StartsWith("aumid:", StringComparison.OrdinalIgnoreCase) ||
        key.StartsWith("pfn:", StringComparison.OrdinalIgnoreCase);

    private static bool IsDevicePath(string path) =>
        path.StartsWith("\\\\?\\", StringComparison.Ordinal) ||
        path.StartsWith("\\\\.\\", StringComparison.Ordinal) ||
        path.StartsWith("\\??\\", StringComparison.Ordinal);

    private static ApplicationIdentity CreatePathIdentity(string path) =>
        ApplicationIdentity.FromWindow(new WindowContext(
            nint.Zero,
            0,
            0,
            string.Empty,
            path,
            null,
            null,
            null));

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
