using FlowIME.Core.Context;
using FlowIME.Core.Decisions;
using FlowIME.Core.Models;
using FlowIME.Core.Rules;

namespace FlowIME.Core.Tests.Decisions;

public sealed class InputDecisionEngineTests
{
    [Fact]
    public void Context_policy_overrides_application_rule()
    {
        var context = Context();
        var rule = Rule(InputAction.Chinese);
        var engine = new InputDecisionEngine(
            new RuleEngine(),
            [new FixedPolicy("gameplay", 1000, InputAction.English, 10)]);

        var decision = engine.Resolve(
            context,
            new RuleConfigurationSnapshot([rule], null));

        Assert.Equal(InputDecisionSource.ContextPolicy, decision.Source);
        Assert.Equal(InputAction.English, decision.Action);
        Assert.Equal("gameplay", decision.ContextPolicyId);
    }

    [Fact]
    public void Context_keep_is_an_explicit_veto_and_does_not_fall_through()
    {
        var engine = new InputDecisionEngine(
            new RuleEngine(),
            [new KeepPolicy("text-safe", 1000, 10)]);

        var decision = engine.Resolve(
            Context(),
            new RuleConfigurationSnapshot(
                [Rule(InputAction.English)],
                new GlobalDefaultTarget(
                    InputMethodProviderIds.WeChat,
                    InputAction.Chinese)));

        Assert.Equal(InputDecisionSource.ContextPolicy, decision.Source);
        Assert.Equal(InputAction.Keep, decision.Action);
    }

    [Fact]
    public void Higher_priority_policy_wins_before_specificity()
    {
        var engine = new InputDecisionEngine(
            new RuleEngine(),
            [
                new FixedPolicy("specific-low", 10, InputAction.Chinese, 100),
                new FixedPolicy("priority-high", 20, InputAction.English, 1)
            ]);

        var decision = engine.Resolve(
            Context(),
            new RuleConfigurationSnapshot([], null));

        Assert.Equal("priority-high", decision.ContextPolicyId);
        Assert.Equal(InputAction.English, decision.Action);
    }

    [Fact]
    public void Existing_application_then_global_default_semantics_are_preserved()
    {
        var engine = new InputDecisionEngine(new RuleEngine());
        var global = new GlobalDefaultTarget(
            InputMethodProviderIds.WeChat,
            InputAction.Chinese);

        var appDecision = engine.Resolve(
            Context(),
            new RuleConfigurationSnapshot([Rule(InputAction.English)], global));
        var globalDecision = engine.Resolve(
            Context(processPath: @"C:\Apps\Other.exe"),
            new RuleConfigurationSnapshot([Rule(InputAction.English)], global));

        Assert.Equal(InputDecisionSource.ApplicationRule, appDecision.Source);
        Assert.Equal(InputAction.English, appDecision.Action);
        Assert.Equal(InputDecisionSource.GlobalDefault, globalDecision.Source);
        Assert.Equal(InputAction.Chinese, globalDecision.Action);
    }



    [Fact]
    public void Mutating_policy_without_provider_is_ignored_and_falls_back()
    {
        var engine = new InputDecisionEngine(
            new RuleEngine(),
            [new MissingProviderPolicy()]);

        var decision = engine.Resolve(
            Context(),
            new RuleConfigurationSnapshot([Rule(InputAction.Chinese)], null));

        Assert.Equal(InputDecisionSource.ApplicationRule, decision.Source);
        Assert.Equal(InputAction.Chinese, decision.Action);
    }

    [Fact]
    public void Duplicate_policy_ids_are_rejected_at_registration_time()
    {
        Assert.Throws<ArgumentException>(() =>
            new InputDecisionEngine(
                new RuleEngine(),
                [
                    new FixedPolicy("duplicate", 10, InputAction.English, 1),
                    new FixedPolicy("duplicate", 20, InputAction.Chinese, 2)
                ]));
    }

    private static InputContextSnapshot Context(
        string processPath = @"C:\Apps\Code.exe")
    {
        var window = new WindowContext(
            (nint)0x10,
            10,
            11,
            Path.GetFileNameWithoutExtension(processPath),
            processPath,
            "Window",
            "Class",
            null);
        return new InputContextSnapshot(
            window,
            ApplicationIdentity.FromWindow(window),
            InputContextTrigger.ForegroundChanged,
            window.Hwnd,
            DateTimeOffset.UtcNow,
            [new(InputContextSignalKind.Application, "test")]);
    }

    private static ApplicationRule Rule(InputAction action) =>
        new(
            Guid.NewGuid(),
            true,
            100,
            new ApplicationMatch(ProcessPath: @"C:\Apps\Code.exe"),
            action,
            ProviderId: InputMethodProviderIds.MicrosoftPinyin);


    private sealed class KeepPolicy(
        string id,
        int priority,
        int specificity) : IInputContextPolicy
    {
        public string Id => id;
        public int Priority => priority;

        public ContextPolicyMatch? Evaluate(InputContextSnapshot context) =>
            new(InputAction.Keep, Specificity: specificity, Reason: $"test:{id}");
    }

    private sealed class MissingProviderPolicy : IInputContextPolicy
    {
        public string Id => "missing-provider";
        public int Priority => 1000;

        public ContextPolicyMatch? Evaluate(InputContextSnapshot context) =>
            new(InputAction.English, ProviderId: null, Specificity: 100);
    }

    private sealed class FixedPolicy(
        string id,
        int priority,
        InputAction action,
        int specificity) : IInputContextPolicy
    {
        public string Id => id;
        public int Priority => priority;

        public ContextPolicyMatch? Evaluate(InputContextSnapshot context) =>
            new(
                action,
                InputMethodProviderIds.MicrosoftPinyin,
                specificity,
                $"test:{id}");
    }
}
