using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using FlowIME.Core.Abstractions;
using FlowIME.Core.Models;
using FlowIME.Core.Rules;

namespace FlowIME.Infrastructure.Configuration;

public sealed class JsonRuleRepository : IRuleRepository, IDisposable
{
    private const int MaxCorruptArtifacts = 5;
    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

    private readonly AppPaths _paths;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private IReadOnlyList<ApplicationRule> _snapshot = Array.Empty<ApplicationRule>();
    private GlobalDefaultTarget? _globalDefault;
    private RuleRepositoryDiagnostics _diagnostics;
    private bool _loaded;
    private bool _disposed;

    public JsonRuleRepository(
        AppPaths? paths = null,
        TimeProvider? timeProvider = null)
    {
        _paths = paths ?? new AppPaths();
        _timeProvider = timeProvider ?? TimeProvider.System;
        _diagnostics = CreateDiagnostics(RuleRepositoryHealthState.NotLoaded, "not-loaded");
    }

    public RuleRepositoryDiagnostics Diagnostics => Volatile.Read(ref _diagnostics);

    public async ValueTask<RuleConfigurationSnapshot> GetConfigurationAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedCoreAsync(cancellationToken).ConfigureAwait(false);
            return new RuleConfigurationSnapshot(_snapshot, _globalDefault);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<IReadOnlyList<ApplicationRule>> GetRulesAsync(
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

    public async ValueTask<GlobalDefaultTarget?> GetGlobalDefaultAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedCoreAsync(cancellationToken).ConfigureAwait(false);
            return _globalDefault;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask ReplaceRulesAsync(
        IReadOnlyList<ApplicationRule> rules,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var normalizedRules = Array.AsReadOnly(
            rules.Select(NormalizeLegacyProvider).ToArray());

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedCoreAsync(cancellationToken).ConfigureAwait(false);
            await PersistSnapshotCoreAsync(
                    normalizedRules,
                    _globalDefault,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask ReplaceGlobalDefaultAsync(
        GlobalDefaultTarget? target,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var normalizedTarget = NormalizeGlobalDefaultForSave(target);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedCoreAsync(cancellationToken).ConfigureAwait(false);
            await PersistSnapshotCoreAsync(
                    _snapshot,
                    normalizedTarget,
                    cancellationToken)
                .ConfigureAwait(false);
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

        var loaded = await LoadSnapshotAsync(cancellationToken).ConfigureAwait(false);
        _snapshot = loaded.Rules;
        _globalDefault = loaded.GlobalDefault;
        _loaded = true;
    }

    private async ValueTask PersistSnapshotCoreAsync(
        IReadOnlyList<ApplicationRule> rules,
        GlobalDefaultTarget? globalDefault,
        CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(_paths.RootDirectory);
            CleanupStaleTempFiles();

            var document = RulesDocument.Create(rules, globalDefault);
            var primaryTempPath = CreateTempPath(_paths.RulesFilePath);
            var backupTempPath = CreateTempPath(_paths.RulesBackupFilePath);
            string? backupWarning = null;

            try
            {
                await WriteDocumentAsync(primaryTempPath, document, cancellationToken)
                    .ConfigureAwait(false);

                // A write is not committed until the exact bytes can be parsed back
                // using the production schema. This catches truncation/serialization
                // problems before rules.json is replaced.
                _ = await ReadRulesFileAsync(primaryTempPath, cancellationToken)
                    .ConfigureAwait(false);

                CommitTempFile(primaryTempPath, _paths.RulesFilePath);

                try
                {
                    // Keep a second independently flushed copy of the latest valid
                    // document. If rules.json is later truncated/corrupted, startup
                    // can restore this last-known-good version automatically.
                    await WriteDocumentAsync(backupTempPath, document, CancellationToken.None)
                        .ConfigureAwait(false);
                    _ = await ReadRulesFileAsync(backupTempPath, CancellationToken.None)
                        .ConfigureAwait(false);
                    CommitTempFile(backupTempPath, _paths.RulesBackupFilePath);
                }
                catch (Exception ex) when (
                    ex is IOException or UnauthorizedAccessException or JsonException)
                {
                    // The authoritative file has already been committed. A backup
                    // refresh failure must not make the UI report that the save itself
                    // failed, but diagnostics should expose the degraded copy.
                    backupWarning = $"backup-refresh-failed:{ex.GetType().Name}";
                    Trace.WriteLine(
                        $"[FlowIME.Configuration] utc={DateTimeOffset.UtcNow:O} " +
                        $"stage=backup-refresh result=failed type={ex.GetType().Name}");
                }

                _snapshot = Array.AsReadOnly(rules.ToArray());
                _globalDefault = globalDefault;
                _loaded = true;
                SetDiagnostics(
                    RuleRepositoryHealthState.Healthy,
                    backupWarning ?? "primary-and-backup-valid");
            }
            finally
            {
                DeleteIfExists(primaryTempPath);
                DeleteIfExists(backupTempPath);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            SetDiagnostics(
                RuleRepositoryHealthState.SaveFailed,
                $"save-failed:{ex.GetType().Name}");
            throw;
        }
    }

    private async ValueTask<RepositorySnapshot> LoadSnapshotAsync(
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_paths.RootDirectory);
        CleanupStaleTempFiles();

        if (File.Exists(_paths.RulesFilePath))
        {
            try
            {
                var snapshot = await ReadRulesFileAsync(
                        _paths.RulesFilePath,
                        cancellationToken)
                    .ConfigureAwait(false);
                var backupWarning = await RefreshBackupBestEffortAsync(snapshot)
                    .ConfigureAwait(false);
                SetDiagnostics(
                    RuleRepositoryHealthState.Healthy,
                    backupWarning ?? "primary-valid-backup-refreshed");
                return snapshot;
            }
            catch (JsonException ex)
            {
                var corruptArtifact = MoveCorruptArtifact(_paths.RulesFilePath);
                Trace.WriteLine(
                    $"[FlowIME.Configuration] utc={DateTimeOffset.UtcNow:O} " +
                    $"stage=primary-load result=corrupt type={ex.GetType().Name}");
                return await RecoverFromBackupAsync(corruptArtifact, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        if (File.Exists(_paths.RulesBackupFilePath))
        {
            return await RecoverFromBackupAsync(
                    corruptPrimaryArtifact: null,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        SetDiagnostics(RuleRepositoryHealthState.Missing, "no-rules-file");
        return RepositorySnapshot.Empty;
    }

    private async ValueTask<RepositorySnapshot> RecoverFromBackupAsync(
        string? corruptPrimaryArtifact,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_paths.RulesBackupFilePath))
        {
            SetDiagnostics(
                RuleRepositoryHealthState.ResetAfterCorruption,
                "no-valid-backup",
                corruptPrimaryArtifact);
            PruneCorruptArtifacts();
            return RepositorySnapshot.Empty;
        }

        try
        {
            var recovered = await ReadRulesFileAsync(
                    _paths.RulesBackupFilePath,
                    cancellationToken)
                .ConfigureAwait(false);

            var recoveryDocument = RulesDocument.Create(
                recovered.Rules,
                recovered.GlobalDefault);
            var restoreTempPath = CreateTempPath(_paths.RulesFilePath);
            try
            {
                await WriteDocumentAsync(
                        restoreTempPath,
                        recoveryDocument,
                        cancellationToken)
                    .ConfigureAwait(false);
                CommitTempFile(restoreTempPath, _paths.RulesFilePath);
            }
            finally
            {
                DeleteIfExists(restoreTempPath);
            }

            SetDiagnostics(
                RuleRepositoryHealthState.RecoveredFromBackup,
                "restored-last-good-backup",
                corruptPrimaryArtifact);
            PruneCorruptArtifacts();
            return recovered;
        }
        catch (JsonException ex)
        {
            var corruptBackupArtifact = MoveCorruptArtifact(_paths.RulesBackupFilePath);
            Trace.WriteLine(
                $"[FlowIME.Configuration] utc={DateTimeOffset.UtcNow:O} " +
                $"stage=backup-load result=corrupt type={ex.GetType().Name}");

            SetDiagnostics(
                RuleRepositoryHealthState.ResetAfterCorruption,
                "primary-and-backup-corrupt",
                corruptPrimaryArtifact ?? corruptBackupArtifact);
            PruneCorruptArtifacts();
            return RepositorySnapshot.Empty;
        }
    }

    private static async ValueTask<RepositorySnapshot> ReadRulesFileAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        var document = await JsonSerializer.DeserializeAsync<RulesDocument>(
                stream,
                SerializerOptions,
                cancellationToken)
            .ConfigureAwait(false);

        if (document is null)
        {
            throw new JsonException("The rules document is empty.");
        }

        if (document.SchemaVersion != RulesDocument.LegacySchemaVersion &&
            document.SchemaVersion != RulesDocument.CurrentSchemaVersion)
        {
            throw new JsonException(
                $"Unsupported rules schema version {document.SchemaVersion}.");
        }

        if (document.Rules is null)
        {
            throw new JsonException("The rules collection is missing.");
        }

        var normalizedRules = Array.AsReadOnly(
            document.Rules
                .Select(NormalizeLegacyProvider)
                .ToArray());
        var normalizedDefault = NormalizeGlobalDefaultFromDocument(document.DefaultTarget);
        return new RepositorySnapshot(normalizedRules, normalizedDefault);
    }

    private static ApplicationRule NormalizeLegacyProvider(ApplicationRule rule)
    {
        var match = rule.Match with
        {
            ProcessPath = NormalizeOptional(rule.Match.ProcessPath),
            ProcessName = NormalizeOptional(rule.Match.ProcessName),
            WindowTitleContains = NormalizeOptional(rule.Match.WindowTitleContains),
            WindowClass = NormalizeOptional(rule.Match.WindowClass),
            PackageFamilyName = NormalizeOptional(rule.Match.PackageFamilyName),
            ApplicationUserModelId = NormalizeOptional(rule.Match.ApplicationUserModelId)
        };
        return rule with
        {
            Match = match,
            ProviderId = string.IsNullOrWhiteSpace(rule.ProviderId)
                ? InputMethodProviderIds.MicrosoftPinyin
                : InputMethodProviderIds.Normalize(rule.ProviderId)
        };
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static GlobalDefaultTarget? NormalizeGlobalDefaultForSave(
        GlobalDefaultTarget? target)
    {
        if (target is null)
        {
            return null;
        }

        if (target.Action is not (InputAction.Chinese or InputAction.English))
        {
            throw new ArgumentException(
                "The global default must explicitly choose Chinese or English.",
                nameof(target));
        }

        return target.Normalize();
    }

    private static GlobalDefaultTarget? NormalizeGlobalDefaultFromDocument(
        GlobalDefaultTarget? target)
    {
        if (target is null)
        {
            return null;
        }

        if (target.Action is not (InputAction.Chinese or InputAction.English))
        {
            throw new JsonException(
                "The global default must explicitly choose Chinese or English.");
        }

        return target.Normalize();
    }

    private async ValueTask<string?> RefreshBackupBestEffortAsync(
        RepositorySnapshot snapshot)
    {
        var backupTempPath = CreateTempPath(_paths.RulesBackupFilePath);
        try
        {
            var document = RulesDocument.Create(snapshot.Rules, snapshot.GlobalDefault);
            await WriteDocumentAsync(backupTempPath, document, CancellationToken.None)
                .ConfigureAwait(false);
            _ = await ReadRulesFileAsync(backupTempPath, CancellationToken.None)
                .ConfigureAwait(false);
            CommitTempFile(backupTempPath, _paths.RulesBackupFilePath);
            return null;
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Trace.WriteLine(
                $"[FlowIME.Configuration] utc={DateTimeOffset.UtcNow:O} " +
                $"stage=backup-refresh result=failed type={ex.GetType().Name}");
            return $"backup-refresh-failed:{ex.GetType().Name}";
        }
        finally
        {
            DeleteIfExists(backupTempPath);
        }
    }

    private static async Task WriteDocumentAsync(
        string path,
        RulesDocument document,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.WriteThrough);

        await JsonSerializer.SerializeAsync(
                stream,
                document,
                SerializerOptions,
                cancellationToken)
            .ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        stream.Flush(flushToDisk: true);
    }

    private static void CommitTempFile(string tempPath, string destinationPath)
    {
        // Source and destination are deliberately created in the same directory.
        // On Windows this maps to a same-volume replace/move rather than a copy,
        // avoiding an observable half-written destination file.
        File.Move(tempPath, destinationPath, overwrite: true);
    }

    private string MoveCorruptArtifact(string sourcePath)
    {
        if (!File.Exists(sourcePath))
        {
            return sourcePath;
        }

        var timestamp = _timeProvider
            .GetUtcNow()
            .UtcDateTime
            .ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var stem = Path.GetFileNameWithoutExtension(sourcePath);
        var backupPath = Path.Combine(
            _paths.RootDirectory,
            $"{stem}.corrupt-{timestamp}.json");

        if (File.Exists(backupPath))
        {
            backupPath = Path.Combine(
                _paths.RootDirectory,
                $"{stem}.corrupt-{timestamp}-{Guid.NewGuid():N}.json");
        }

        File.Move(sourcePath, backupPath);
        return backupPath;
    }

    private void CleanupStaleTempFiles()
    {
        if (!Directory.Exists(_paths.RootDirectory))
        {
            return;
        }

        foreach (var path in Directory.EnumerateFiles(
                     _paths.RootDirectory,
                     "rules*.tmp",
                     SearchOption.TopDirectoryOnly))
        {
            DeleteIfExists(path);
        }
    }

    private void PruneCorruptArtifacts()
    {
        if (!Directory.Exists(_paths.RootDirectory))
        {
            return;
        }

        try
        {
            var artifacts = Directory
                .EnumerateFiles(
                    _paths.RootDirectory,
                    "*.corrupt-*.json",
                    SearchOption.TopDirectoryOnly)
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .ThenByDescending(file => file.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (var artifact in artifacts.Skip(MaxCorruptArtifacts))
            {
                DeleteIfExists(artifact.FullName);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string CreateTempPath(string destinationPath) =>
        $"{destinationPath}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";

    private static void DeleteIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void SetDiagnostics(
        RuleRepositoryHealthState state,
        string? detail,
        string? corruptArtifactPath = null) =>
        Volatile.Write(
            ref _diagnostics,
            CreateDiagnostics(state, detail, corruptArtifactPath));

    private RuleRepositoryDiagnostics CreateDiagnostics(
        RuleRepositoryHealthState state,
        string? detail,
        string? corruptArtifactPath = null) =>
        new(
            state,
            detail,
            _timeProvider.GetUtcNow(),
            _paths.RulesFilePath,
            _paths.RulesBackupFilePath,
            corruptArtifactPath);

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private sealed record RepositorySnapshot(
        IReadOnlyList<ApplicationRule> Rules,
        GlobalDefaultTarget? GlobalDefault)
    {
        public static RepositorySnapshot Empty { get; } =
            new(Array.Empty<ApplicationRule>(), null);
    }
}
