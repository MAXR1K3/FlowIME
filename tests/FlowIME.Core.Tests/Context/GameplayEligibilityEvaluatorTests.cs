using FlowIME.Core.Context;

namespace FlowIME.Core.Tests.Context;

public sealed class GameplayEligibilityEvaluatorTests
{
    private readonly GameplayEligibilityEvaluator _evaluator = new();

    [Fact]
    public void Fullscreen_without_game_evidence_is_not_gameplay()
    {
        var result = _evaluator.Evaluate(true, []);

        Assert.False(result.IsEligible);
        Assert.Equal("insufficient-game-evidence", result.Reason);
    }

    [Fact]
    public void Strong_game_evidence_does_not_override_missing_fullscreen()
    {
        var result = _evaluator.Evaluate(
            false,
            [Evidence(GameplayEvidenceKind.WindowsGameMetadata)]);

        Assert.False(result.IsEligible);
        Assert.Equal("not-fullscreen", result.Reason);
    }

    [Theory]
    [InlineData(GameplayEvidenceKind.WindowsGameMetadata, "windows-game-metadata")]
    [InlineData(GameplayEvidenceKind.Direct3DExclusive, "direct3d-exclusive")]
    public void One_strong_system_fact_classifies_gameplay(
        GameplayEvidenceKind kind,
        string reason)
    {
        var result = _evaluator.Evaluate(true, [Evidence(kind)]);

        Assert.True(result.IsEligible);
        Assert.Equal(ContextSignalConfidence.High, result.Confidence);
        Assert.Equal(reason, result.Reason);
    }

    [Fact]
    public void Independent_system_facts_raise_confidence_to_certain()
    {
        var result = _evaluator.Evaluate(
            true,
            [
                Evidence(GameplayEvidenceKind.WindowsGameMetadata),
                Evidence(GameplayEvidenceKind.Direct3DExclusive)
            ]);

        Assert.True(result.IsEligible);
        Assert.Equal(ContextSignalConfidence.Certain, result.Confidence);
        Assert.Equal("windows-game-metadata+direct3d-exclusive", result.Reason);
    }

    [Fact]
    public void Future_user_game_preference_is_authoritative()
    {
        var result = _evaluator.Evaluate(
            true,
            [Evidence(GameplayEvidenceKind.UserDeclaredGame)]);

        Assert.True(result.IsEligible);
        Assert.Equal(ContextSignalConfidence.Certain, result.Confidence);
        Assert.Equal("user-declared-game", result.Reason);
    }

    [Fact]
    public void Future_user_not_game_preference_is_a_hard_veto()
    {
        var result = _evaluator.Evaluate(
            true,
            [
                Evidence(GameplayEvidenceKind.UserDeclaredGame),
                Evidence(GameplayEvidenceKind.WindowsGameMetadata),
                Evidence(GameplayEvidenceKind.UserDeclaredNotGame)
            ]);

        Assert.False(result.IsEligible);
        Assert.Equal(ContextSignalConfidence.Certain, result.Confidence);
        Assert.Equal("user-declared-not-game", result.Reason);
    }

    private static GameplayEvidence Evidence(GameplayEvidenceKind kind) =>
        new(kind, $"test.{kind}");
}
