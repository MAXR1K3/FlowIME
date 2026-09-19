using System.Text.Json;
using FlowIME.Core.Abstractions;
using FlowIME.Core.Settings;

namespace FlowIME.Infrastructure.Configuration;

/// <summary>
/// Small, independently persisted application-preference store. Rules remain in
/// rules.json so behavioral preferences cannot corrupt or invalidate rule recovery.
/// Unknown/malformed settings fall back to safe defaults rather than blocking startup.
/// </summary>
public sealed class JsonAppSettingsRepository : IAppSettingsRepository, IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly AppPaths _paths;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private AppSettingsSnapshot _snapshot = AppSettingsSnapshot.Default;
    private bool _loaded;
    private bool _disposed;

    public JsonAppSettingsRepository(AppPaths? paths = null)
    {
        _paths = paths ?? new AppPaths();
    }

    public async ValueTask<AppSettingsSnapshot> GetAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedCoreAsync(cancellationToken).ConfigureAwait(false);
            return _snapshot;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask SaveGameplayHotkeyGuardAsync(
        GameplayHotkeyGuardSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedCoreAsync(cancellationToken).ConfigureAwait(false);
            var next = _snapshot with { GameplayHotkeyGuard = settings };
            await PersistCoreAsync(next, cancellationToken).ConfigureAwait(false);
            _snapshot = next;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask SaveGameplayKeyboardBaselineAsync(
        GameplayKeyboardBaselineSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedCoreAsync(cancellationToken).ConfigureAwait(false);
            var next = _snapshot with { GameplayKeyboardBaseline = settings };
            await PersistCoreAsync(next, cancellationToken).ConfigureAwait(false);
            _snapshot = next;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask SaveInputStatusOverlayAsync(
        InputStatusOverlaySettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedCoreAsync(cancellationToken).ConfigureAwait(false);
            var next = _snapshot with { InputStatusOverlay = settings.Normalize() };
            await PersistCoreAsync(next, cancellationToken).ConfigureAwait(false);
            _snapshot = next;
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

        _snapshot = await LoadCoreAsync(cancellationToken).ConfigureAwait(false);
        _loaded = true;
    }

    private async ValueTask<AppSettingsSnapshot> LoadCoreAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_paths.SettingsFilePath))
        {
            return AppSettingsSnapshot.Default;
        }

        try
        {
            await using var stream = new FileStream(
                _paths.SettingsFilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var document = await JsonSerializer.DeserializeAsync<SettingsDocument>(
                    stream,
                    SerializerOptions,
                    cancellationToken)
                .ConfigureAwait(false);

            return document?.ToSnapshot() ?? AppSettingsSnapshot.Default;
        }
        catch (JsonException)
        {
            return AppSettingsSnapshot.Default;
        }
        catch (IOException)
        {
            return AppSettingsSnapshot.Default;
        }
        catch (UnauthorizedAccessException)
        {
            return AppSettingsSnapshot.Default;
        }
    }

    private async ValueTask PersistCoreAsync(
        AppSettingsSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_paths.RootDirectory);
        var tempPath = _paths.SettingsFilePath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            var document = SettingsDocument.Create(snapshot);
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
                        document,
                        SerializerOptions,
                        cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(tempPath, _paths.SettingsFilePath, overwrite: true);
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
                // A stale settings temp file is non-authoritative and harmless.
            }
        }
    }

    private sealed class SettingsDocument
    {
        private const int CurrentSchemaVersion = 3;

        public int SchemaVersion { get; set; }

        public GameplayHotkeyGuardSettings? GameplayHotkeyGuard { get; set; }

        public GameplayKeyboardBaselineSettings? GameplayKeyboardBaseline { get; set; }

        public InputStatusOverlaySettings? InputStatusOverlay { get; set; }

        public SettingsDocument()
        {
        }

        internal static SettingsDocument Create(AppSettingsSnapshot snapshot) =>
            new()
            {
                SchemaVersion = CurrentSchemaVersion,
                GameplayHotkeyGuard = snapshot.GameplayHotkeyGuard,
                GameplayKeyboardBaseline = snapshot.GameplayKeyboardBaseline,
                InputStatusOverlay = snapshot.InputStatusOverlay
            };

        internal AppSettingsSnapshot ToSnapshot() =>
            new(
                GameplayHotkeyGuard ?? GameplayHotkeyGuardSettings.Default,
                GameplayKeyboardBaseline ?? GameplayKeyboardBaselineSettings.Default,
                (InputStatusOverlay ?? InputStatusOverlaySettings.Default).Normalize());
    }
}
