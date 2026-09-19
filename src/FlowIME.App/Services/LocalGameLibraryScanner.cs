using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace FlowIME.App.Services;

internal sealed record GameLibraryScanEntry(
    string SourceName,
    string StoreId,
    string DisplayName,
    string InstallDirectory,
    string? ExecutablePath,
    IReadOnlyList<string> ExecutableCandidates,
    string? ArtworkPath = null);

internal sealed partial class LocalGameLibraryScanner
{
    private static readonly string[] ExcludedExecutableFragments =
    [
        "unins", "uninstall", "crash", "report", "redist", "setup", "installer",
        "easyanticheat", "eac", "beservice", "battleye", "launcher", "bootstrap"
    ];

    private readonly IReadOnlyList<string> _steamRoots;
    private readonly IReadOnlyList<string> _epicManifestDirectories;

    internal LocalGameLibraryScanner(
        IEnumerable<string> steamRoots,
        IEnumerable<string> epicManifestDirectories)
    {
        _steamRoots = NormalizeExistingDirectories(steamRoots);
        _epicManifestDirectories = NormalizeExistingDirectories(epicManifestDirectories);
    }

    internal static LocalGameLibraryScanner CreateDefault()
    {
        var steamRoots = new List<string>();
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            if (key?.GetValue("SteamPath") is string path)
            {
                steamRoots.Add(path);
            }
        }
        catch
        {
        }

        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrWhiteSpace(programFilesX86))
        {
            steamRoots.Add(Path.Combine(programFilesX86, "Steam"));
        }

        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var epicDirectories = string.IsNullOrWhiteSpace(programData)
            ? Array.Empty<string>()
            :
            [
                Path.Combine(programData, "Epic", "EpicGamesLauncher", "Data", "Manifests")
            ];
        return new LocalGameLibraryScanner(steamRoots, epicDirectories);
    }

    internal async ValueTask<IReadOnlyList<GameLibraryScanEntry>> ScanAsync(
        CancellationToken cancellationToken = default)
    {
        var entries = new List<GameLibraryScanEntry>();
        foreach (var steamRoot in ExpandSteamLibraryRoots(_steamRoots))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ScanSteamRootAsync(steamRoot, entries, cancellationToken).ConfigureAwait(false);
        }

        foreach (var manifestDirectory in _epicManifestDirectories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ScanEpicDirectoryAsync(manifestDirectory, entries, cancellationToken).ConfigureAwait(false);
        }

        return entries
            .GroupBy(entry => $"{entry.SourceName}:{entry.StoreId}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(entry => entry.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static async Task ScanSteamRootAsync(
        string steamRoot,
        ICollection<GameLibraryScanEntry> entries,
        CancellationToken cancellationToken)
    {
        var steamApps = Path.Combine(steamRoot, "steamapps");
        if (!Directory.Exists(steamApps))
        {
            return;
        }

        foreach (var manifestPath in EnumerateFilesSafely(steamApps, "appmanifest_*.acf"))
        {
            try
            {
                var text = await File.ReadAllTextAsync(manifestPath, cancellationToken).ConfigureAwait(false);
                var appId = ReadVdfValue(text, "appid");
                var name = ReadVdfValue(text, "name");
                var installDirName = ReadVdfValue(text, "installdir");
                if (string.IsNullOrWhiteSpace(appId) ||
                    string.IsNullOrWhiteSpace(name) ||
                    string.IsNullOrWhiteSpace(installDirName))
                {
                    continue;
                }

                var commonRoot = Path.Combine(steamApps, "common");
                if (!TryResolveContainedDirectory(commonRoot, installDirName, out var installDirectory))
                {
                    continue;
                }

                var candidates = FindExecutableCandidates(installDirectory);
                entries.Add(new GameLibraryScanEntry(
                    "Steam",
                    appId,
                    name,
                    installDirectory,
                    candidates.Count == 1 ? candidates[0] : null,
                    candidates,
                    FindSteamArtwork(steamRoot, appId)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
            }
        }
    }

    private static async Task ScanEpicDirectoryAsync(
        string manifestDirectory,
        ICollection<GameLibraryScanEntry> entries,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(manifestDirectory))
        {
            return;
        }

        foreach (var manifestPath in EnumerateFilesSafely(manifestDirectory, "*.item"))
        {
            try
            {
                await using var stream = File.OpenRead(manifestPath);
                using var document = await JsonDocument.ParseAsync(
                    stream,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                var root = document.RootElement;
                var storeId = ReadJsonString(root, "AppName");
                var name = ReadJsonString(root, "DisplayName");
                var installDirectoryValue = ReadJsonString(root, "InstallLocation");
                if (string.IsNullOrWhiteSpace(storeId) ||
                    string.IsNullOrWhiteSpace(name) ||
                    string.IsNullOrWhiteSpace(installDirectoryValue))
                {
                    continue;
                }

                if (!TryResolveManifestInstallDirectory(installDirectoryValue, out var installDirectory))
                {
                    continue;
                }

                var candidates = FindExecutableCandidates(installDirectory);
                var executable = ResolveLaunchExecutable(
                    installDirectory,
                    ReadJsonString(root, "LaunchExecutable"),
                    candidates);
                entries.Add(new GameLibraryScanEntry(
                    "Epic",
                    storeId,
                    name,
                    installDirectory,
                    executable ?? (candidates.Count == 1 ? candidates[0] : null),
                    candidates));
            }
            catch (Exception ex) when (
                ex is IOException or UnauthorizedAccessException or ArgumentException or JsonException)
            {
            }
        }
    }

    private static IReadOnlyList<string> ExpandSteamLibraryRoots(IEnumerable<string> roots)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            result.Add(root);
            var libraryFile = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(libraryFile))
            {
                continue;
            }

            try
            {
                var text = File.ReadAllText(libraryFile);
                foreach (Match match in VdfPathRegex().Matches(text))
                {
                    var path = match.Groups[1].Value.Replace("\\\\", "\\");
                    if (Directory.Exists(path))
                    {
                        result.Add(Path.GetFullPath(path));
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
            }
        }
        return result.ToArray();
    }

    private static IReadOnlyList<string> FindExecutableCandidates(string installDirectory)
    {
        var result = new List<string>();
        var root = Path.GetFullPath(installDirectory)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var pending = new Queue<(string Directory, int Depth)>();
        pending.Enqueue((installDirectory, 0));

        while (pending.Count > 0 && result.Count < 64)
        {
            var (directory, depth) = pending.Dequeue();
            try
            {
                foreach (var executable in Directory.EnumerateFiles(directory, "*.exe"))
                {
                    var fullPath = Path.GetFullPath(executable);
                    var fileName = Path.GetFileNameWithoutExtension(fullPath);
                    if (fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase) &&
                        !ExcludedExecutableFragments.Any(fragment =>
                            fileName.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
                    {
                        result.Add(fullPath);
                    }
                }

                if (depth >= 3)
                {
                    continue;
                }

                foreach (var child in Directory.EnumerateDirectories(directory))
                {
                    if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0)
                    {
                        pending.Enqueue((child, depth + 1));
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        return result
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? ResolveLaunchExecutable(
        string installDirectory,
        string? launchExecutable,
        IReadOnlyList<string> candidates)
    {
        if (string.IsNullOrWhiteSpace(launchExecutable))
        {
            return null;
        }

        try
        {
            var root = Path.GetFullPath(installDirectory)
                .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var candidate = Path.GetFullPath(Path.Combine(installDirectory, launchExecutable));
            return candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase) &&
                   File.Exists(candidate) &&
                   candidates.Contains(candidate, StringComparer.OrdinalIgnoreCase)
                ? candidate
                : null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static string? FindSteamArtwork(string steamRoot, string appId)
    {
        var libraryCache = Path.Combine(steamRoot, "appcache", "librarycache");
        var candidates = new[]
        {
            Path.Combine(libraryCache, appId, "library_600x900.jpg"),
            Path.Combine(libraryCache, $"{appId}_library_600x900.jpg"),
            Path.Combine(libraryCache, appId, "header.jpg"),
            Path.Combine(libraryCache, $"{appId}_header.jpg")
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static string? ReadVdfValue(string text, string key)
    {
        var match = Regex.Match(
            text,
            $"\\\"{Regex.Escape(key)}\\\"\\s+\\\"(?<value>[^\\\"]*)\\\"",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success ? match.Groups["value"].Value : null;
    }

    private static string? ReadJsonString(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static IReadOnlyList<string> NormalizeExistingDirectories(IEnumerable<string> directories)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in directories ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            try
            {
                var fullPath = Path.GetFullPath(path);
                if (Directory.Exists(fullPath))
                {
                    result.Add(fullPath);
                }
            }
            catch (Exception ex) when (
                ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
            }
        }

        return result.ToArray();
    }

    private static IReadOnlyList<string> EnumerateFilesSafely(string directory, string pattern)
    {
        try
        {
            return Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly).ToArray();
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return [];
        }
    }

    private static bool TryResolveContainedDirectory(
        string allowedRoot,
        string relativePath,
        out string directory)
    {
        directory = string.Empty;
        try
        {
            var root = Path.GetFullPath(allowedRoot);
            var candidate = Path.GetFullPath(Path.Combine(root, relativePath));
            var relative = Path.GetRelativePath(root, candidate);
            if (Path.IsPathRooted(relative) ||
                relative.Equals("..", StringComparison.Ordinal) ||
                relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                !Directory.Exists(candidate))
            {
                return false;
            }

            directory = candidate;
            return true;
        }
        catch (Exception ex) when (
            ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static bool TryResolveManifestInstallDirectory(string path, out string directory)
    {
        directory = string.Empty;
        try
        {
            if (!Path.IsPathFullyQualified(path))
            {
                return false;
            }

            var candidate = Path.GetFullPath(path);
            var volumeRoot = Path.GetPathRoot(candidate)?.TrimEnd(Path.DirectorySeparatorChar);
            if (string.Equals(
                    candidate.TrimEnd(Path.DirectorySeparatorChar),
                    volumeRoot,
                    StringComparison.OrdinalIgnoreCase) ||
                !Directory.Exists(candidate) ||
                (File.GetAttributes(candidate) & FileAttributes.ReparsePoint) != 0)
            {
                return false;
            }

            directory = candidate;
            return true;
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or ArgumentException or
                NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    [GeneratedRegex("\\\"path\\\"\\s+\\\"([^\\\"]+)\\\"", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VdfPathRegex();
}
