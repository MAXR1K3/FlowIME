using System.Diagnostics;
using FlowIME.Core.Models;
using FlowIME.Core.Rules;

namespace FlowIME.App.Services;

/// <summary>
/// The application a tray quick rule applies to: the last real application the
/// user worked in, plus the application rule that currently matches it.
/// </summary>
internal sealed record QuickRuleTarget(
    string DisplayName,
    WindowContext Window,
    ApplicationRule? ExistingRule);

/// <summary>
/// Remembers the last foreground application that can own a rule. Opening the
/// tray menu activates the taskbar, so the menu cannot use the live foreground;
/// shell surfaces are skipped so the target stays on the application the user
/// came from.
/// </summary>
internal sealed class QuickRuleTargetTracker(Func<WindowContext, string> resolveDisplayName)
{
    private static readonly HashSet<string> ShellProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "ShellExperienceHost",
        "StartMenuExperienceHost",
        "SearchHost",
        "SearchApp",
        "LockApp",
        "TextInputHost",
        "ShellHost"
    };

    private static readonly HashSet<string> ExplorerShellClasses = new(StringComparer.Ordinal)
    {
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "NotifyIconOverflowWindow",
        "TopLevelWindowForOverflowXamlIsland",
        "Progman",
        "WorkerW"
    };

    private readonly Func<WindowContext, string> _resolveDisplayName =
        resolveDisplayName ?? throw new ArgumentNullException(nameof(resolveDisplayName));
    private QuickRuleTarget? _current;

    public QuickRuleTarget? Current => Volatile.Read(ref _current);

    public void Observe(CurrentStateSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!IsEligible(snapshot.Window))
        {
            return;
        }

        var rule = snapshot.ResolutionSource == RuleResolutionSource.ApplicationRule
            ? snapshot.MatchedRule
            : null;
        var previous = Current;
        var displayName = !string.IsNullOrWhiteSpace(rule?.DisplayName)
            ? rule.DisplayName!
            : previous is not null && SameExecutable(previous.Window, snapshot.Window)
                ? previous.DisplayName
                : _resolveDisplayName(snapshot.Window);
        Volatile.Write(ref _current, new QuickRuleTarget(displayName, snapshot.Window, rule));
    }

    /// <summary>
    /// Records a rule the user just set from the tray, since the next foreground
    /// snapshot is the taskbar and would not refresh the matched rule.
    /// </summary>
    public void UpdateRule(QuickRuleTarget target, ApplicationRule? rule)
    {
        ArgumentNullException.ThrowIfNull(target);
        var current = Current;
        if (current is not null && SameExecutable(current.Window, target.Window))
        {
            Volatile.Write(ref _current, current with
            {
                ExistingRule = rule,
                DisplayName = !string.IsNullOrWhiteSpace(rule?.DisplayName)
                    ? rule.DisplayName!
                    : current.DisplayName
            });
        }
    }

    internal static bool IsEligible(WindowContext window)
    {
        if (string.IsNullOrWhiteSpace(window.ExecutablePath) ||
            ShellProcesses.Contains(window.ProcessName))
        {
            return false;
        }

        return !(string.Equals(window.ProcessName, "explorer", StringComparison.OrdinalIgnoreCase) &&
                 window.WindowClass is { } windowClass &&
                 ExplorerShellClasses.Contains(windowClass));
    }

    /// <summary>
    /// "微信" reads better than "Weixin" in a menu; use the executable's file
    /// description like the add-application picker does.
    /// </summary>
    internal static string ResolveDisplayName(WindowContext window)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(window.ExecutablePath!);
            if (!string.IsNullOrWhiteSpace(info.FileDescription))
            {
                return info.FileDescription.Trim();
            }

            if (!string.IsNullOrWhiteSpace(info.ProductName))
            {
                return info.ProductName.Trim();
            }
        }
        catch (Exception ex) when (ex is FileNotFoundException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception)
        {
        }

        return window.ProcessName;
    }

    private static bool SameExecutable(WindowContext left, WindowContext right) =>
        string.Equals(left.ExecutablePath, right.ExecutablePath, StringComparison.OrdinalIgnoreCase);
}
