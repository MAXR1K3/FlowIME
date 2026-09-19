using FlowIME.Core.Models;

namespace FlowIME.Core.Rules;

public sealed class RuleEngine : IRuleEngine
{
    public RuleMatchResult Match(
        WindowContext window,
        IReadOnlyCollection<ApplicationRule> rules)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(rules);

        var candidate = rules
            .Where(static rule => rule.Enabled)
            .Select(rule => new Candidate(rule, GetSpecificity(rule.Match)))
            .Where(candidate => candidate.Specificity > 0 && Matches(window, candidate.Rule.Match))
            .OrderByDescending(static candidate => candidate.Rule.Priority)
            .ThenByDescending(static candidate => candidate.Specificity)
            .ThenBy(static candidate => candidate.Rule.Id)
            .FirstOrDefault();

        return candidate is null
            ? RuleMatchResult.NoMatch
            : new RuleMatchResult(candidate.Rule, candidate.Rule.Action, true);
    }


    public RuleResolution Resolve(
        WindowContext window,
        IReadOnlyCollection<ApplicationRule> rules,
        GlobalDefaultTarget? globalDefault)
    {
        var match = Match(window, rules);
        if (match.IsMatch && match.Rule is not null)
        {
            return RuleResolution.FromApplicationRule(match.Rule);
        }

        return globalDefault is null
            ? RuleResolution.None
            : RuleResolution.FromGlobalDefault(globalDefault);
    }

    private static bool Matches(WindowContext window, ApplicationMatch match)
    {
        if (!MatchesApplicationIdentity(window, match))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(match.ProcessName) &&
            !EqualsIgnoreCase(window.ProcessName, match.ProcessName))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(match.WindowClass) &&
            !EqualsIgnoreCase(window.WindowClass, match.WindowClass))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(match.WindowTitleContains) &&
            (window.WindowTitle is null ||
             !window.WindowTitle.Contains(match.WindowTitleContains, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Resolves one application identity constraint using strongest-observed-first
    /// semantics. A persisted AUMID is authoritative when Windows exposes an AUMID
    /// for the current process. If that metadata is temporarily unavailable, matching
    /// degrades to PFN + executable name, then to the legacy path/name fallback.
    /// The display path therefore does not make a stable packaged identity brittle
    /// across versioned WindowsApps updates.
    /// </summary>
    private static bool MatchesApplicationIdentity(
        WindowContext window,
        ApplicationMatch match)
    {
        if (!string.IsNullOrWhiteSpace(match.ApplicationUserModelId) &&
            !string.IsNullOrWhiteSpace(window.ApplicationUserModelId))
        {
            return EqualsIgnoreCase(
                window.ApplicationUserModelId,
                match.ApplicationUserModelId);
        }

        if (!string.IsNullOrWhiteSpace(match.PackageFamilyName) &&
            !string.IsNullOrWhiteSpace(window.PackageFamilyName))
        {
            if (!EqualsIgnoreCase(
                    window.PackageFamilyName,
                    match.PackageFamilyName))
            {
                return false;
            }

            // A package can host more than one executable. Preserve the executable
            // discriminator when the rule has one, while ignoring versioned parent
            // directories.
            return string.IsNullOrWhiteSpace(match.ProcessPath) ||
                   MatchesExecutableNameFallback(window, match.ProcessPath);
        }

        if (!string.IsNullOrWhiteSpace(match.ProcessPath))
        {
            return EqualsIgnoreCase(window.ExecutablePath, match.ProcessPath) ||
                   MatchesExecutableNameFallback(window, match.ProcessPath);
        }

        // No path/package/AUMID constraint was configured. Legacy process-name,
        // class and title fields are evaluated by the caller.
        return true;
    }

    private static int GetSpecificity(ApplicationMatch match)
    {
        var score = 0;

        if (!string.IsNullOrWhiteSpace(match.WindowTitleContains))
        {
            score += 500;
        }

        if (!string.IsNullOrWhiteSpace(match.ApplicationUserModelId))
        {
            score += 450;
        }

        if (!string.IsNullOrWhiteSpace(match.ProcessPath))
        {
            score += 400;
        }

        if (!string.IsNullOrWhiteSpace(match.PackageFamilyName))
        {
            score += 300;
        }

        if (!string.IsNullOrWhiteSpace(match.WindowClass))
        {
            score += 200;
        }

        if (!string.IsNullOrWhiteSpace(match.ProcessName))
        {
            score += 100;
        }

        return score;
    }


    private static bool MatchesExecutableNameFallback(
        WindowContext window,
        string configuredProcessPath)
    {
        if (string.IsNullOrWhiteSpace(window.ProcessName))
        {
            return false;
        }

        var configuredProcessName = Path.GetFileNameWithoutExtension(configuredProcessPath);
        return !string.IsNullOrWhiteSpace(configuredProcessName) &&
               EqualsIgnoreCase(window.ProcessName, configuredProcessName);
    }

    private static bool EqualsIgnoreCase(string? left, string right) =>
        left is not null && left.Equals(right, StringComparison.OrdinalIgnoreCase);

    private sealed record Candidate(ApplicationRule Rule, int Specificity);
}
