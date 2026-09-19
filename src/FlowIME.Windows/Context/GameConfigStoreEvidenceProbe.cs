using System.Security;
using FlowIME.Core.Context;
using FlowIME.Core.Models;
using Microsoft.Win32;

namespace FlowIME.Windows.Context;

internal interface IGameConfigStoreReader
{
    bool IsKnownGame(string executablePath);
}

/// <summary>
/// Read-only access to Windows' per-user GameConfigStore. The store is treated as
/// optional system metadata, never as a source of truth: missing/inaccessible keys
/// simply contribute no evidence. FlowIME never creates or mutates GameConfigStore
/// entries.
/// </summary>
internal sealed class WindowsGameConfigStoreReader : IGameConfigStoreReader
{
    private const string ChildrenRegistryPath = @"System\GameConfigStore\Children";
    private const string MatchedExecutableValueName = "MatchedExeFullPath";

    public bool IsKnownGame(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return false;
        }

        try
        {
            using var children = Registry.CurrentUser.OpenSubKey(ChildrenRegistryPath, writable: false);
            if (children is null)
            {
                return false;
            }

            foreach (var childName in children.GetSubKeyNames())
            {
                using var child = children.OpenSubKey(childName, writable: false);
                var matchedPath = child?.GetValue(MatchedExecutableValueName) as string;
                if (PathsEqual(matchedPath, executablePath))
                {
                    return true;
                }
            }
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return false;
        }

        return false;
    }

    internal static bool PathsEqual(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        return NormalizePath(left).Equals(
            NormalizePath(right),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path) =>
        path.Trim().Trim('"').Replace('/', '\\').TrimEnd('\\');
}

/// <summary>
/// Converts Windows GameConfigStore membership into semantic gameplay evidence and
/// caches the result so focus changes inside a fullscreen game do not repeatedly
/// enumerate the registry. Negative results have a short TTL because Windows may add
/// metadata shortly after a game starts for the first time.
/// </summary>
internal sealed class GameConfigStoreEvidenceProbe : IGameplayEvidenceProbe
{
    internal const string ProbeId = "windows.gameplay.game-config-store";
    private static readonly TimeSpan PositiveCacheDuration = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan NegativeCacheDuration = TimeSpan.FromSeconds(10);

    private readonly IGameConfigStoreReader _reader;
    private readonly TimeProvider _timeProvider;
    private readonly object _sync = new();
    private readonly Dictionary<string, CacheEntry> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    public GameConfigStoreEvidenceProbe()
        : this(new WindowsGameConfigStoreReader(), TimeProvider.System)
    {
    }

    internal GameConfigStoreEvidenceProbe(
        IGameConfigStoreReader reader,
        TimeProvider? timeProvider = null)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public string Id => ProbeId;

    public int Order => 100;

    public GameplayEvidence? Capture(WindowContext window)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (string.IsNullOrWhiteSpace(window.ExecutablePath))
        {
            return null;
        }

        var path = window.ExecutablePath.Trim();
        var now = _timeProvider.GetUtcNow();
        bool isKnownGame;

        lock (_sync)
        {
            if (_cache.TryGetValue(path, out var cached) && cached.ExpiresAt > now)
            {
                isKnownGame = cached.IsKnownGame;
            }
            else
            {
                isKnownGame = _reader.IsKnownGame(path);
                _cache[path] = new CacheEntry(
                    isKnownGame,
                    now + (isKnownGame ? PositiveCacheDuration : NegativeCacheDuration));
            }
        }

        return isKnownGame
            ? new GameplayEvidence(
                GameplayEvidenceKind.WindowsGameMetadata,
                ProbeId,
                ContextSignalConfidence.High)
            : null;
    }

    private sealed record CacheEntry(bool IsKnownGame, DateTimeOffset ExpiresAt);
}
