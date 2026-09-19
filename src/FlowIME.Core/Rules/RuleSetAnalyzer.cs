using FlowIME.Core.Models;

namespace FlowIME.Core.Rules;

public enum RuleRelationshipKind
{
    ExactMatch,
    ExistingShadowsCandidate,
    CandidateShadowsExisting,
    EqualPriorityCompetition,
    PotentialOverlap
}

public sealed record RuleRelationship(
    ApplicationRule OtherRule,
    RuleRelationshipKind Kind);

public static class RuleSetAnalyzer
{
    public static RuleSetDiagnostics Analyze(IReadOnlyCollection<ApplicationRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        var enabled = rules.Where(static rule => rule.Enabled).ToArray();
        var overlaps = enabled
            .Select(static rule => new MatchCandidate(rule, MatchKey.Create(rule.Match)))
            .Where(static candidate => !candidate.Key.IsEmpty)
            .GroupBy(static candidate => candidate.Key)
            .Where(static group => group.Count() > 1)
            .ToArray();

        var conflicting = 0;
        var redundant = 0;
        var affectedIds = new HashSet<Guid>();
        foreach (var group in overlaps)
        {
            foreach (var candidate in group)
            {
                affectedIds.Add(candidate.Rule.Id);
            }

            var distinctTargets = group
                .Select(static candidate => TargetKey.Create(candidate.Rule))
                .Distinct()
                .Count();
            if (distinctTargets > 1)
            {
                conflicting++;
            }
            else
            {
                redundant++;
            }
        }

        var shadowed = 0;
        var equalPriorityCompetition = 0;
        for (var leftIndex = 0; leftIndex < enabled.Length; leftIndex++)
        {
            for (var rightIndex = leftIndex + 1; rightIndex < enabled.Length; rightIndex++)
            {
                var left = enabled[leftIndex];
                var right = enabled[rightIndex];
                if (MatchKey.Create(left.Match) == MatchKey.Create(right.Match) ||
                    !CanOverlap(left.Match, right.Match))
                {
                    continue;
                }

                if (left.Priority == right.Priority)
                {
                    equalPriorityCompetition++;
                    affectedIds.Add(left.Id);
                    affectedIds.Add(right.Id);
                    continue;
                }

                var winner = left.Priority > right.Priority ? left : right;
                var loser = ReferenceEquals(winner, left) ? right : left;
                if (Covers(winner.Match, loser.Match))
                {
                    shadowed++;
                    affectedIds.Add(winner.Id);
                    affectedIds.Add(loser.Id);
                }
            }
        }

        return new RuleSetDiagnostics(
            enabled.Length,
            overlaps.Length,
            conflicting,
            redundant,
            affectedIds.Count)
        {
            ShadowedRulePairCount = shadowed,
            EqualPriorityCompetitionPairCount = equalPriorityCompetition
        };
    }

    public static IReadOnlyList<RuleRelationship> AnalyzeCandidate(
        ApplicationRule candidate,
        IReadOnlyCollection<ApplicationRule> existingRules)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(existingRules);

        var candidateKey = MatchKey.Create(candidate.Match);
        var relationships = new List<RuleRelationship>();
        foreach (var existing in existingRules.Where(static rule => rule.Enabled))
        {
            if (existing.Id == candidate.Id)
            {
                continue;
            }

            if (candidateKey == MatchKey.Create(existing.Match))
            {
                relationships.Add(new RuleRelationship(existing, RuleRelationshipKind.ExactMatch));
                continue;
            }

            if (!CanOverlap(candidate.Match, existing.Match))
            {
                continue;
            }

            if (existing.Priority > candidate.Priority && Covers(existing.Match, candidate.Match))
            {
                relationships.Add(new RuleRelationship(existing, RuleRelationshipKind.ExistingShadowsCandidate));
            }
            else if (candidate.Priority > existing.Priority && Covers(candidate.Match, existing.Match))
            {
                relationships.Add(new RuleRelationship(existing, RuleRelationshipKind.CandidateShadowsExisting));
            }
            else if (candidate.Priority == existing.Priority)
            {
                relationships.Add(new RuleRelationship(existing, RuleRelationshipKind.EqualPriorityCompetition));
            }
            else
            {
                relationships.Add(new RuleRelationship(existing, RuleRelationshipKind.PotentialOverlap));
            }
        }

        return relationships;
    }

    public static bool AreExactMatches(ApplicationMatch left, ApplicationMatch right) =>
        MatchKey.Create(left) == MatchKey.Create(right);

    public static string DescribeMatch(ApplicationMatch match)
    {
        ArgumentNullException.ThrowIfNull(match);
        var parts = new List<string>();
        Add(parts, match.ProcessPath, "可执行文件 = ");
        Add(parts, match.ProcessName, "进程名 = ");
        Add(parts, match.WindowClass, "窗口类 = ");
        Add(parts, match.WindowTitleContains, "标题包含 ");
        Add(parts, match.PackageFamilyName, "包标识 = ");
        Add(parts, match.ApplicationUserModelId, "AUMID = ");
        return parts.Count == 0 ? "未设置有效匹配条件" : string.Join(" 且 ", parts);
    }

    private static void Add(List<string> parts, string? value, string prefix)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            parts.Add(prefix + value.Trim());
        }
    }

    private static bool CanOverlap(ApplicationMatch left, ApplicationMatch right) =>
        EqualityConstraintsCompatible(left.ProcessName, right.ProcessName) &&
        EqualityConstraintsCompatible(left.WindowClass, right.WindowClass) &&
        IdentityConstraintsCompatible(left, right);

    private static bool Covers(ApplicationMatch broad, ApplicationMatch narrow) =>
        EqualityConstraintCovers(broad.ProcessName, narrow.ProcessName) &&
        EqualityConstraintCovers(broad.WindowClass, narrow.WindowClass) &&
        IdentityConstraintCovers(broad, narrow) &&
        TitleConstraintCovers(broad.WindowTitleContains, narrow.WindowTitleContains);

    private static bool IdentityConstraintsCompatible(ApplicationMatch left, ApplicationMatch right)
    {
        if (!HasIdentityConstraint(left) || !HasIdentityConstraint(right))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(left.ApplicationUserModelId) &&
            !string.IsNullOrWhiteSpace(right.ApplicationUserModelId) &&
            Normalize(left.ApplicationUserModelId) == Normalize(right.ApplicationUserModelId))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(left.PackageFamilyName) &&
            !string.IsNullOrWhiteSpace(right.PackageFamilyName) &&
            Normalize(left.PackageFamilyName) == Normalize(right.PackageFamilyName))
        {
            return PathConstraintsCompatible(left.ProcessPath, right.ProcessPath);
        }

        return PathConstraintsCompatible(left.ProcessPath, right.ProcessPath);
    }

    private static bool IdentityConstraintCovers(ApplicationMatch broad, ApplicationMatch narrow)
    {
        if (!HasIdentityConstraint(broad))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(broad.ApplicationUserModelId) &&
            !string.IsNullOrWhiteSpace(narrow.ApplicationUserModelId) &&
            Normalize(broad.ApplicationUserModelId) == Normalize(narrow.ApplicationUserModelId))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(broad.PackageFamilyName) &&
            !string.IsNullOrWhiteSpace(narrow.PackageFamilyName) &&
            Normalize(broad.PackageFamilyName) == Normalize(narrow.PackageFamilyName))
        {
            return PathConstraintCovers(broad.ProcessPath, narrow.ProcessPath);
        }

        return PathConstraintCovers(broad.ProcessPath, narrow.ProcessPath);
    }

    private static bool HasIdentityConstraint(ApplicationMatch match) =>
        !string.IsNullOrWhiteSpace(match.ProcessPath) ||
        !string.IsNullOrWhiteSpace(match.PackageFamilyName) ||
        !string.IsNullOrWhiteSpace(match.ApplicationUserModelId);

    private static bool EqualityConstraintsCompatible(string? left, string? right) =>
        string.IsNullOrWhiteSpace(left) ||
        string.IsNullOrWhiteSpace(right) ||
        Normalize(left) == Normalize(right);

    private static bool EqualityConstraintCovers(string? broad, string? narrow) =>
        string.IsNullOrWhiteSpace(broad) ||
        (!string.IsNullOrWhiteSpace(narrow) && Normalize(broad) == Normalize(narrow));

    private static bool PathConstraintsCompatible(string? left, string? right) =>
        string.IsNullOrWhiteSpace(left) ||
        string.IsNullOrWhiteSpace(right) ||
        PathsEquivalentForEngine(left, right);

    private static bool PathConstraintCovers(string? broad, string? narrow) =>
        string.IsNullOrWhiteSpace(broad) ||
        (!string.IsNullOrWhiteSpace(narrow) && PathsEquivalentForEngine(broad, narrow));

    private static bool PathsEquivalentForEngine(string left, string right) =>
        Normalize(left) == Normalize(right) ||
        Normalize(Path.GetFileNameWithoutExtension(left)) ==
        Normalize(Path.GetFileNameWithoutExtension(right));

    private static bool TitleConstraintCovers(string? broad, string? narrow) =>
        string.IsNullOrWhiteSpace(broad) ||
        (!string.IsNullOrWhiteSpace(narrow) &&
         narrow.Trim().Contains(broad.Trim(), StringComparison.OrdinalIgnoreCase));

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    private sealed record MatchCandidate(ApplicationRule Rule, MatchKey Key);

    private sealed record MatchKey(
        string? ProcessPath,
        string? ProcessName,
        string? WindowTitleContains,
        string? WindowClass,
        string? PackageFamilyName,
        string? ApplicationUserModelId)
    {
        public bool IsEmpty =>
            ProcessPath is null && ProcessName is null && WindowTitleContains is null &&
            WindowClass is null && PackageFamilyName is null && ApplicationUserModelId is null;

        public static MatchKey Create(ApplicationMatch match)
        {
            ArgumentNullException.ThrowIfNull(match);
            return new MatchKey(
                Normalize(match.ProcessPath), Normalize(match.ProcessName),
                Normalize(match.WindowTitleContains), Normalize(match.WindowClass),
                Normalize(match.PackageFamilyName), Normalize(match.ApplicationUserModelId));
        }
    }

    private sealed record TargetKey(string ProviderId, InputAction Action)
    {
        public static TargetKey Create(ApplicationRule rule)
        {
            var providerId = rule.Action == InputAction.Keep
                ? string.Empty
                : InputMethodProviderIds.Normalize(rule.ProviderId);
            return new TargetKey(providerId, rule.Action);
        }
    }
}
