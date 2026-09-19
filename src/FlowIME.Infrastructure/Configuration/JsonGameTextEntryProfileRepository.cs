using System.Text.Json;
using System.Text.Json.Serialization;
using FlowIME.Core.Abstractions;
using FlowIME.Core.Context;

namespace FlowIME.Infrastructure.Configuration;

/// <summary>
/// Independent, fail-safe store for per-game text-entry profiles. A malformed file
/// falls back to an empty profile set and never blocks normal FlowIME startup.
/// </summary>
public sealed class JsonGameTextEntryProfileRepository : IGameTextEntryProfileRepository, IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly AppPaths _paths;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private GameTextEntryProfile[] _profiles = [];
    private bool _loaded;
    private bool _disposed;

    public JsonGameTextEntryProfileRepository(AppPaths? paths = null)
    {
        _paths = paths ?? new AppPaths();
    }

    public async ValueTask<IReadOnlyList<GameTextEntryProfile>> GetProfilesAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedCoreAsync(cancellationToken).ConfigureAwait(false);
            return _profiles.ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask ReplaceProfilesAsync(
        IEnumerable<GameTextEntryProfile> profiles,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Reuse the Core registry as the single normalization/validation authority.
        var validator = new GameTextEntryProfileRegistry();
        validator.ReplaceProfiles(profiles);
        var normalized = validator.Profiles.ToArray();

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedCoreAsync(cancellationToken).ConfigureAwait(false);
            await PersistCoreAsync(normalized, cancellationToken).ConfigureAwait(false);
            _profiles = normalized;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _gate.Dispose();
    }

    private async ValueTask EnsureLoadedCoreAsync(CancellationToken cancellationToken)
    {
        if (_loaded)
        {
            return;
        }

        _profiles = await LoadCoreAsync(cancellationToken).ConfigureAwait(false);
        _loaded = true;
    }

    private async ValueTask<GameTextEntryProfile[]> LoadCoreAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_paths.GameTextEntryProfilesFilePath))
        {
            return [];
        }

        try
        {
            await using var stream = new FileStream(
                _paths.GameTextEntryProfilesFilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var document = await JsonSerializer.DeserializeAsync<ProfileDocument>(
                    stream,
                    SerializerOptions,
                    cancellationToken)
                .ConfigureAwait(false);

            if (document is null || document.SchemaVersion != 1)
            {
                return [];
            }

            var validator = new GameTextEntryProfileRegistry();
            validator.ReplaceProfiles(document.Profiles ?? []);
            return validator.Profiles.ToArray();
        }
        catch (JsonException)
        {
            return [];
        }
        catch (ArgumentException)
        {
            return [];
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    private async ValueTask PersistCoreAsync(
        IReadOnlyList<GameTextEntryProfile> profiles,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_paths.RootDirectory);
        var tempPath = _paths.GameTextEntryProfilesFilePath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                        stream,
                        new ProfileDocument
                        {
                            SchemaVersion = 1,
                            Profiles = profiles.ToArray()
                        },
                        SerializerOptions,
                        cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(tempPath, _paths.GameTextEntryProfilesFilePath, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch
            {
                // Non-authoritative temp files are harmless.
            }
        }
    }

    private sealed class ProfileDocument
    {
        public int SchemaVersion { get; set; }

        public GameTextEntryProfile[] Profiles { get; set; } = [];
    }
}
