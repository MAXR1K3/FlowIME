using FlowIME.Core.Abstractions;
using FlowIME.Core.Models;
using FlowIME.Windows.Windowing;

namespace FlowIME.Windows.Applications;

public sealed class RunningApplicationCatalog : IRunningApplicationCatalog
{
    private readonly ITopLevelWindowSource _windows;
    private readonly IWindowResolver _resolver;
    private readonly IApplicationDisplayNameResolver _displayNames;
    private readonly uint _ignoredProcessId;
    private readonly IProcessApplicationSource _processes;

    public RunningApplicationCatalog()
        : this(
            new Win32TopLevelWindowSource(),
            new WindowResolver(),
            new FileVersionDisplayNameResolver(),
            checked((uint)Environment.ProcessId),
            new SystemProcessApplicationSource())
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Running application discovery requires Windows.");
        }
    }

    internal RunningApplicationCatalog(
        ITopLevelWindowSource windows,
        IWindowResolver resolver,
        IApplicationDisplayNameResolver displayNames,
        uint ignoredProcessId,
        IProcessApplicationSource? processes = null)
    {
        _windows = windows ?? throw new ArgumentNullException(nameof(windows));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _displayNames = displayNames ?? throw new ArgumentNullException(nameof(displayNames));
        _ignoredProcessId = ignoredProcessId;
        _processes = processes ?? new EmptyProcessApplicationSource();
    }

    public async ValueTask<IReadOnlyList<RunningApplication>> GetRunningApplicationsAsync(
        CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, RunningApplication>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var hwnd in _windows.EnumerateVisibleTopLevelWindows())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var window = await _resolver
                .ResolveAsync(hwnd, cancellationToken)
                .ConfigureAwait(false);
            if (window is null ||
                window.ProcessId == _ignoredProcessId ||
                string.IsNullOrWhiteSpace(window.ExecutablePath))
            {
                continue;
            }

            var path = window.ExecutablePath;
            var key = NormalizePath(path);
            if (result.ContainsKey(key))
            {
                continue;
            }

            var processName = string.IsNullOrWhiteSpace(window.ProcessName)
                ? Path.GetFileNameWithoutExtension(path)
                : window.ProcessName;
            var displayName = _displayNames.Resolve(path, processName);

            result[key] = new RunningApplication(
                displayName,
                processName,
                path,
                hwnd,
                window.ProcessId,
                window.PackageFamilyName,
                window.ApplicationUserModelId);
        }

        foreach (var process in _processes.Enumerate())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (process.ProcessId == _ignoredProcessId ||
                string.IsNullOrWhiteSpace(process.ExecutablePath))
            {
                continue;
            }

            var key = NormalizePath(process.ExecutablePath);
            if (result.ContainsKey(key))
            {
                continue;
            }

            var processName = string.IsNullOrWhiteSpace(process.ProcessName)
                ? Path.GetFileNameWithoutExtension(process.ExecutablePath)
                : process.ProcessName;
            result[key] = new RunningApplication(
                _displayNames.Resolve(process.ExecutablePath, processName),
                processName,
                process.ExecutablePath,
                MainWindowHandle: 0,
                process.ProcessId);
        }

        return result.Values
            .OrderBy(app => app.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(app => app.ExecutablePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path;
        }
    }
}
