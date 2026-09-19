namespace FlowIME.Infrastructure.Configuration;

public enum RuleRepositoryHealthState
{
    NotLoaded,
    Missing,
    Healthy,
    RecoveredFromBackup,
    ResetAfterCorruption,
    SaveFailed
}

public sealed record RuleRepositoryDiagnostics(
    RuleRepositoryHealthState State,
    string? Detail,
    DateTimeOffset ObservedAtUtc,
    string RulesFilePath,
    string BackupFilePath,
    string? CorruptArtifactPath = null);
