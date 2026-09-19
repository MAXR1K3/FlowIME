using FlowIME.Core.Context;
using FlowIME.Core.Decisions;
using FlowIME.Core.Models;
using FlowIME.Core.Rules;

namespace FlowIME.Core.Tests.Decisions;

public sealed class GameTextEntryPolicyTests
{
    [Fact]
    public void Non_text_entry_context_does_not_match()
    {
        var policy = new GameTextEntryPolicy(new GameTextEntryProfileRegistry());

        Assert.Null(policy.Evaluate(Context(gameTextEntry: false)));
    }

    [Fact]
    public void Unconfigured_text_entry_fails_closed_with_keep()
    {
        var policy = new GameTextEntryPolicy(new GameTextEntryProfileRegistry());

        var match = policy.Evaluate(Context(gameTextEntry: true));

        Assert.NotNull(match);
        Assert.Equal(InputAction.Keep, match!.Action);
        Assert.Equal("game-text-entry-no-profile", match.Reason);
    }

    [Fact]
    public void Configured_profile_returns_provider_target()
    {
        var context = Context(gameTextEntry: true);
        var registry = new GameTextEntryProfileRegistry();
        registry.ReplaceProfiles([
            new GameTextEntryProfile
            {
                Id = "chat",
                ApplicationIdentityKey = context.Application.Key,
                DetectionMode = GameTextEntryDetectionMode.HotkeyProfile,
                ProviderId = InputMethodProviderIds.WeChat,
                Action = InputAction.Chinese,
                EnterGestures = [new(0x54)],
                ExitGestures = [new(0x0D)]
            }
        ]);
        var policy = new GameTextEntryPolicy(registry);

        var match = policy.Evaluate(context);

        Assert.NotNull(match);
        Assert.Equal(InputAction.Chinese, match!.Action);
        Assert.Equal(InputMethodProviderIds.WeChat, match.ProviderId);
        Assert.Equal("game-text-entry-profile:chat", match.Reason);
        Assert.True(policy.Priority > new GameplayKeyboardBaselinePolicy(
            new GameplayKeyboardBaselineState()).Priority);
    }

    [Fact]
    public void Text_entry_target_outranks_ready_gameplay_baseline()
    {
        var context = Context(gameTextEntry: true);
        var registry = new GameTextEntryProfileRegistry();
        registry.ReplaceProfiles([
            new GameTextEntryProfile
            {
                Id = "chat",
                ApplicationIdentityKey = context.Application.Key,
                DetectionMode = GameTextEntryDetectionMode.HotkeyProfile,
                ProviderId = InputMethodProviderIds.WeChat,
                Action = InputAction.Chinese,
                EnterGestures = [new(0x54)],
                ExitGestures = [new(0x0D)]
            }
        ]);
        var baselineState = new GameplayKeyboardBaselineState();
        baselineState.SetReady(true);
        var engine = new InputDecisionEngine(
            new RuleEngine(),
            [
                new GameTextEntryPolicy(registry),
                new GameplayKeyboardBaselinePolicy(baselineState)
            ]);

        var decision = engine.Resolve(
            context,
            new RuleConfigurationSnapshot([], null));

        Assert.Equal(InputDecisionSource.ContextPolicy, decision.Source);
        Assert.Equal(GameTextEntryPolicy.PolicyId, decision.ContextPolicyId);
        Assert.Equal(InputAction.Chinese, decision.Action);
        Assert.Equal(InputMethodProviderIds.WeChat, decision.ProviderId);
    }

    private static InputContextSnapshot Context(bool gameTextEntry)
    {
        var window = new WindowContext(
            (nint)0x10,
            10,
            11,
            "game",
            @"C:\Games\game.exe",
            "Game",
            "GameWindow",
            null);
        var signals = new List<InputContextSignal>
        {
            new(InputContextSignalKind.Application, "test"),
            new(InputContextSignalKind.Game, "test")
        };
        if (gameTextEntry)
        {
            signals.Add(new InputContextSignal(InputContextSignalKind.GameTextEntry, "test"));
        }

        return new InputContextSnapshot(
            window,
            ApplicationIdentity.FromWindow(window),
            InputContextTrigger.GameTextEntryChanged,
            0,
            DateTimeOffset.UtcNow,
            signals);
    }
}
