using System.ComponentModel;
using System.Diagnostics;
using FlowIME.Core.Models;

namespace FlowIME.App.Services;

public static class ExecutableCandidateFactory
{
    public static RunningApplication Create(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new ArgumentException("An executable path is required.", nameof(executablePath));
        }

        var path = Path.GetFullPath(executablePath);
        if (!path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
        {
            throw new ArgumentException("Select an existing .exe file.", nameof(executablePath));
        }

        var processName = Path.GetFileNameWithoutExtension(path);
        var displayName = ResolveDisplayName(path, processName);
        return new RunningApplication(
            displayName,
            processName,
            path,
            MainWindowHandle: 0,
            ProcessId: 0);
    }

    private static string ResolveDisplayName(string path, string fallback)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            if (!string.IsNullOrWhiteSpace(info.FileDescription))
            {
                return info.FileDescription.Trim();
            }

            if (!string.IsNullOrWhiteSpace(info.ProductName))
            {
                return info.ProductName.Trim();
            }
        }
        catch (Exception ex) when (ex is FileNotFoundException or UnauthorizedAccessException or Win32Exception)
        {
        }

        return fallback;
    }
}
