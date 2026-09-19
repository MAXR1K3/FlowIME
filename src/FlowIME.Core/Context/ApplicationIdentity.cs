using System.Security.Cryptography;
using System.Text;
using FlowIME.Core.Models;

namespace FlowIME.Core.Context;

public enum ApplicationIdentityKind
{
    ApplicationUserModelId,
    PackageFamilyName,
    ExecutablePath,
    ExecutableName,
    ProcessName
}

/// <summary>
/// Stable runtime identity for an application. Packaged identity is preferred over
/// versioned WindowsApps paths. Unpackaged applications use a hashed normalized
/// executable path so runtime keys stay collision-resistant without exposing a full
/// local path in diagnostics.
/// </summary>
public sealed record ApplicationIdentity(
    string Key,
    ApplicationIdentityKind Kind,
    string Value,
    string? ApplicationUserModelId,
    string? PackageFamilyName,
    string? ExecutableName,
    string? ExecutablePath,
    string ProcessName)
{
    public static ApplicationIdentity FromWindow(WindowContext window)
    {
        ArgumentNullException.ThrowIfNull(window);

        var aumid = Normalize(window.ApplicationUserModelId);
        var packageFamilyName = Normalize(window.PackageFamilyName);
        var executableName = GetExecutableName(window.ExecutablePath);
        var processName = Normalize(window.ProcessName) ?? string.Empty;

        if (aumid is not null)
        {
            return Create(
                ApplicationIdentityKind.ApplicationUserModelId,
                "aumid",
                aumid,
                window,
                executableName);
        }

        if (packageFamilyName is not null)
        {
            return Create(
                ApplicationIdentityKind.PackageFamilyName,
                "pfn",
                packageFamilyName,
                window,
                executableName);
        }

        var executablePath = Normalize(window.ExecutablePath);
        if (executablePath is not null && !LooksLikeVersionedPackagePath(executablePath))
        {
            return Create(
                ApplicationIdentityKind.ExecutablePath,
                "path",
                executablePath,
                window,
                executableName);
        }

        if (executableName is not null)
        {
            return Create(
                ApplicationIdentityKind.ExecutableName,
                "exe",
                executableName,
                window,
                executableName);
        }

        return Create(
            ApplicationIdentityKind.ProcessName,
            "process",
            processName,
            window,
            executableName);
    }

    public static ApplicationIdentity FromRunningApplication(RunningApplication application)
    {
        ArgumentNullException.ThrowIfNull(application);

        var aumid = Normalize(application.ApplicationUserModelId);
        var packageFamilyName = Normalize(application.PackageFamilyName);
        var executableName = GetExecutableName(application.ExecutablePath);
        var processName = Normalize(application.ProcessName) ?? string.Empty;

        var syntheticWindow = new WindowContext(
            application.MainWindowHandle,
            application.ProcessId,
            0,
            processName,
            application.ExecutablePath,
            null,
            null,
            packageFamilyName,
            aumid);

        if (aumid is not null)
        {
            return Create(
                ApplicationIdentityKind.ApplicationUserModelId,
                "aumid",
                aumid,
                syntheticWindow,
                executableName);
        }

        if (packageFamilyName is not null)
        {
            return Create(
                ApplicationIdentityKind.PackageFamilyName,
                "pfn",
                packageFamilyName,
                syntheticWindow,
                executableName);
        }

        var executablePath = Normalize(application.ExecutablePath);
        if (executablePath is not null && !LooksLikeVersionedPackagePath(executablePath))
        {
            return Create(
                ApplicationIdentityKind.ExecutablePath,
                "path",
                executablePath,
                syntheticWindow,
                executableName);
        }

        if (executableName is not null)
        {
            return Create(
                ApplicationIdentityKind.ExecutableName,
                "exe",
                executableName,
                syntheticWindow,
                executableName);
        }

        return Create(
            ApplicationIdentityKind.ProcessName,
            "process",
            processName,
            syntheticWindow,
            executableName);
    }

    private static ApplicationIdentity Create(
        ApplicationIdentityKind kind,
        string prefix,
        string value,
        WindowContext window,
        string? executableName) =>
        new(
            Key: CreateKey(prefix, value),
            Kind: kind,
            Value: value,
            ApplicationUserModelId: Normalize(window.ApplicationUserModelId),
            PackageFamilyName: Normalize(window.PackageFamilyName),
            ExecutableName: executableName,
            ExecutablePath: Normalize(window.ExecutablePath),
            ProcessName: Normalize(window.ProcessName) ?? string.Empty);

    private static string? GetExecutableName(string? path)
    {
        var normalized = Normalize(path);
        if (normalized is null)
        {
            return null;
        }

        var fileName = Path.GetFileName(normalized);
        return Normalize(fileName);
    }

    private static string CreateKey(string prefix, string value)
    {
        if (!prefix.Equals("path", StringComparison.Ordinal))
        {
            return $"{prefix}:{value.ToLowerInvariant()}";
        }

        var normalized = value.ToUpperInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return $"path:{Convert.ToHexString(hash.AsSpan(0, 12)).ToLowerInvariant()}";
    }

    private static bool LooksLikeVersionedPackagePath(string path) =>
        path.Contains(
            $"{Path.DirectorySeparatorChar}WindowsApps{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase) ||
        path.Contains("\\WindowsApps\\", StringComparison.OrdinalIgnoreCase);

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
