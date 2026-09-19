using FlowIME.Core.Models;
using FlowIME.Core.Rules;

namespace FlowIME.Core.Tests.Rules;

public sealed class RuleSetAnalyzerTests
{
    [Fact]
    public void Exact_same_match_with_different_targets_is_reported_as_conflict()
    {
        var match = new ApplicationMatch(ProcessPath: @"C:\Apps\Code.exe");
        ApplicationRule[] rules =
        [
            Rule(match, InputAction.Chinese, InputMethodProviderIds.WeChat),
            Rule(match, InputAction.English, InputMethodProviderIds.MicrosoftPinyin)
        ];

        var result = RuleSetAnalyzer.Analyze(rules);

        Assert.True(result.HasIssues);
        Assert.Equal(1, result.ExactOverlapGroupCount);
        Assert.Equal(1, result.ConflictingTargetGroupCount);
        Assert.Equal(0, result.RedundantTargetGroupCount);
        Assert.Equal(2, result.AffectedRuleCount);
    }

    [Fact]
    public void Exact_same_match_and_target_is_reported_as_redundant()
    {
        ApplicationRule[] rules =
        [
            Rule(new ApplicationMatch(ProcessName: "Code"), InputAction.Chinese, InputMethodProviderIds.WeChat),
            Rule(new ApplicationMatch(ProcessName: "code"), InputAction.Chinese, InputMethodProviderIds.WeChat)
        ];

        var result = RuleSetAnalyzer.Analyze(rules);

        Assert.Equal(1, result.ExactOverlapGroupCount);
        Assert.Equal(0, result.ConflictingTargetGroupCount);
        Assert.Equal(1, result.RedundantTargetGroupCount);
    }

    [Fact]
    public void Disabled_rule_does_not_participate_in_overlap_diagnostics()
    {
        var match = new ApplicationMatch(ProcessPath: @"C:\Apps\Code.exe");
        ApplicationRule[] rules =
        [
            Rule(match, InputAction.Chinese, InputMethodProviderIds.WeChat),
            Rule(match, InputAction.English, InputMethodProviderIds.MicrosoftPinyin, enabled: false)
        ];

        var result = RuleSetAnalyzer.Analyze(rules);

        Assert.False(result.HasIssues);
        Assert.Equal(1, result.EnabledRuleCount);
    }

    [Fact]
    public void Empty_non_matching_rules_do_not_create_false_positive_overlap()
    {
        ApplicationRule[] rules =
        [
            Rule(new ApplicationMatch(), InputAction.Chinese, InputMethodProviderIds.WeChat),
            Rule(new ApplicationMatch(), InputAction.English, InputMethodProviderIds.MicrosoftPinyin)
        ];

        var result = RuleSetAnalyzer.Analyze(rules);

        Assert.False(result.HasIssues);
        Assert.Equal(0, result.ExactOverlapGroupCount);
    }

    [Fact]
    public void Same_process_with_different_title_constraint_is_reported_as_equal_priority_competition()
    {
        ApplicationRule[] rules =
        [
            Rule(new ApplicationMatch(ProcessName: "chrome"), InputAction.Chinese, InputMethodProviderIds.WeChat),
            Rule(
                new ApplicationMatch(ProcessName: "chrome", WindowTitleContains: "Docs"),
                InputAction.English,
                InputMethodProviderIds.MicrosoftPinyin)
        ];

        var result = RuleSetAnalyzer.Analyze(rules);

        Assert.True(result.HasIssues);
        Assert.Equal(0, result.ExactOverlapGroupCount);
        Assert.Equal(1, result.EqualPriorityCompetitionPairCount);
    }

    [Fact]
    public void Same_aumid_rules_compete_even_when_persisted_paths_have_different_names()
    {
        ApplicationRule[] rules =
        [
            Rule(
                new ApplicationMatch(
                    ProcessPath: @"C:\Old\HostA.exe",
                    ApplicationUserModelId: "Vendor.App!Main"),
                InputAction.Chinese,
                InputMethodProviderIds.WeChat),
            Rule(
                new ApplicationMatch(
                    ProcessPath: @"C:\New\HostB.exe",
                    ApplicationUserModelId: "Vendor.App!Main"),
                InputAction.English,
                InputMethodProviderIds.MicrosoftPinyin)
        ];

        var result = RuleSetAnalyzer.Analyze(rules);

        Assert.Equal(1, result.EqualPriorityCompetitionPairCount);
    }

    [Fact]
    public void Higher_priority_broad_rule_that_covers_specific_rule_is_reported_as_shadowing()
    {
        ApplicationRule[] rules =
        [
            Rule(new ApplicationMatch(ProcessName: "chrome"), InputAction.Chinese, InputMethodProviderIds.WeChat, priority: 300),
            Rule(
                new ApplicationMatch(ProcessName: "chrome", WindowTitleContains: "Docs"),
                InputAction.English,
                InputMethodProviderIds.MicrosoftPinyin,
                priority: 100)
        ];

        var result = RuleSetAnalyzer.Analyze(rules);

        Assert.True(result.HasIssues);
        Assert.Equal(1, result.ShadowedRulePairCount);
        Assert.Equal(0, result.EqualPriorityCompetitionPairCount);
    }

    [Fact]
    public void Candidate_analysis_explains_duplicate_and_shadow_relationships()
    {
        var candidate = Rule(
            new ApplicationMatch(ProcessPath: @"C:\Apps\Chrome.exe", WindowTitleContains: "Docs"),
            InputAction.English,
            InputMethodProviderIds.MicrosoftPinyin,
            priority: 100);
        ApplicationRule[] existing =
        [
            Rule(new ApplicationMatch(ProcessPath: @"c:\apps\CHROME.exe", WindowTitleContains: "docs"), InputAction.Chinese, InputMethodProviderIds.WeChat, priority: 100),
            Rule(new ApplicationMatch(ProcessPath: @"C:\Apps\Chrome.exe"), InputAction.Chinese, InputMethodProviderIds.WeChat, priority: 300)
        ];

        var relationships = RuleSetAnalyzer.AnalyzeCandidate(candidate, existing);

        Assert.Contains(relationships, item => item.Kind == RuleRelationshipKind.ExactMatch);
        Assert.Contains(relationships, item => item.Kind == RuleRelationshipKind.ExistingShadowsCandidate);
    }

    private static ApplicationRule Rule(
        ApplicationMatch match,
        InputAction action,
        string providerId,
        bool enabled = true,
        int priority = 100) =>
        new(Guid.NewGuid(), enabled, priority, match, action, null, providerId);
}
