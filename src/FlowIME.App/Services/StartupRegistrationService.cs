using Microsoft.Win32;

namespace FlowIME.App.Services;

/// <summary>
/// Registers the current unpackaged FlowIME executable in the current user's
/// Run key. Startup launches in background mode so automation begins without
/// flashing the settings window at sign-in.
/// </summary>
public sealed class StartupRegistrationService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "FlowIME";

    private readonly string _commandLine;

    public StartupRegistrationService()
        : this(
            Environment.ProcessPath ??
            throw new InvalidOperationException(
                "The current FlowIME executable path could not be resolved."))
    {
    }

    internal StartupRegistrationService(string executablePath)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Windows startup registration requires Windows.");
        }

        _commandLine = BuildCommandLine(executablePath);
    }

    public bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            var current = key?.GetValue(ValueName) as string;
            return string.Equals(
                current,
                _commandLine,
                StringComparison.OrdinalIgnoreCase);
        }
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException(
                "The current user's Windows startup registry key could not be opened.");

        if (enabled)
        {
            key.SetValue(ValueName, _commandLine, RegistryValueKind.String);
            return;
        }

        key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    internal static string BuildCommandLine(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        if (executablePath.Contains('"'))
        {
            throw new ArgumentException(
                "Executable paths containing quotation marks are not supported.",
                nameof(executablePath));
        }

        var commandLine = $"\"{executablePath}\" {LaunchOptions.BackgroundArgument}";
        if (commandLine.Length > 260)
        {
            throw new InvalidOperationException(
                "The FlowIME startup command exceeds the Windows Run-key 260-character limit.");
        }

        return commandLine;
    }
}
