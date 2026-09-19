namespace FlowIME.Infrastructure.Configuration;

public sealed class AppPaths
{
    public AppPaths(string? rootDirectory = null)
    {
        RootDirectory = rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FlowIME");

        if (string.IsNullOrWhiteSpace(RootDirectory))
        {
            throw new ArgumentException(
                "The FlowIME data directory must not be empty.",
                nameof(rootDirectory));
        }
    }

    public string RootDirectory { get; }

    public string RulesFilePath => Path.Combine(RootDirectory, "rules.json");

    public string RulesBackupFilePath => Path.Combine(RootDirectory, "rules.last-good.json");

    public string SettingsFilePath => Path.Combine(RootDirectory, "settings.json");

    public string GameTextEntryProfilesFilePath => Path.Combine(RootDirectory, "game-text-entry-profiles.json");

    public string LogsDirectory => Path.Combine(RootDirectory, "logs");

    public string AutomationLogPath => Path.Combine(LogsDirectory, "automation.log");
}
