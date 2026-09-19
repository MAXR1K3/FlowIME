namespace FlowIME.Core.Models;

/// <summary>
/// Result of asking a provider whether its profile is currently active.
/// Detection failure is distinct from a successful inactive result so provider
/// selection never guesses when native state is unavailable.
/// </summary>
public sealed record InputMethodProviderDetectionResult(
    bool Success,
    bool IsActive,
    string? ErrorCode = null);
