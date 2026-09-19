namespace FlowIME.Core.Context;

public enum GameplayEvidenceKind
{
    UserDeclaredGame,
    UserDeclaredNotGame,
    WindowsGameMetadata,
    Direct3DExclusive
}

/// <summary>
/// One privacy-safe reason that a foreground application may or may not represent
/// gameplay. Evidence never contains window titles, executable paths, URLs or input
/// content. Native probes should translate local system metadata into these stable
/// semantic facts before handing them to the evaluator.
/// </summary>
public sealed record GameplayEvidence(
    GameplayEvidenceKind Kind,
    string Source,
    ContextSignalConfidence Confidence = ContextSignalConfidence.High);

public sealed record GameplayEligibilityAssessment(
    bool IsEligible,
    ContextSignalConfidence Confidence,
    string Reason,
    IReadOnlyList<GameplayEvidence> Evidence)
{
    public static GameplayEligibilityAssessment NotEligible(
        string reason,
        IReadOnlyList<GameplayEvidence>? evidence = null) =>
        new(
            false,
            ContextSignalConfidence.Low,
            reason,
            evidence ?? Array.Empty<GameplayEvidence>());
}

/// <summary>
/// Pure gameplay classification over already-collected evidence.
///
/// Contract:
/// - geometric fullscreen is a prerequisite for automatic gameplay classification;
/// - an explicit future "not a game" preference is a hard veto;
/// - an explicit future "game" preference is authoritative;
/// - Windows game metadata or an exclusive Direct3D fullscreen signal are each
///   strong enough to classify gameplay conservatively;
/// - two independent strong system signals raise confidence to Certain.
///
/// No input-method action belongs here. P8B gameplay policies consume the resulting
/// Game context signal in a later stage.
/// </summary>
public sealed class GameplayEligibilityEvaluator
{
    public GameplayEligibilityAssessment Evaluate(
        bool isFullscreen,
        IReadOnlyList<GameplayEvidence> evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        var sanitized = evidence
            .Where(item => item is not null && !string.IsNullOrWhiteSpace(item.Source))
            .Distinct()
            .ToArray();

        if (!isFullscreen)
        {
            return GameplayEligibilityAssessment.NotEligible(
                "not-fullscreen",
                sanitized);
        }

        if (sanitized.Any(item => item.Kind == GameplayEvidenceKind.UserDeclaredNotGame))
        {
            return new GameplayEligibilityAssessment(
                false,
                ContextSignalConfidence.Certain,
                "user-declared-not-game",
                sanitized);
        }

        if (sanitized.Any(item => item.Kind == GameplayEvidenceKind.UserDeclaredGame))
        {
            return new GameplayEligibilityAssessment(
                true,
                ContextSignalConfidence.Certain,
                "user-declared-game",
                sanitized);
        }

        var windowsMetadata = sanitized.Any(
            item => item.Kind == GameplayEvidenceKind.WindowsGameMetadata);
        var direct3DExclusive = sanitized.Any(
            item => item.Kind == GameplayEvidenceKind.Direct3DExclusive);

        if (windowsMetadata && direct3DExclusive)
        {
            return new GameplayEligibilityAssessment(
                true,
                ContextSignalConfidence.Certain,
                "windows-game-metadata+direct3d-exclusive",
                sanitized);
        }

        if (windowsMetadata)
        {
            return new GameplayEligibilityAssessment(
                true,
                ContextSignalConfidence.High,
                "windows-game-metadata",
                sanitized);
        }

        if (direct3DExclusive)
        {
            return new GameplayEligibilityAssessment(
                true,
                ContextSignalConfidence.High,
                "direct3d-exclusive",
                sanitized);
        }

        return GameplayEligibilityAssessment.NotEligible(
            "insufficient-game-evidence",
            sanitized);
    }
}
