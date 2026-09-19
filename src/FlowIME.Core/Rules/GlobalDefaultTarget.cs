using FlowIME.Core.Models;

namespace FlowIME.Core.Rules;

/// <summary>
/// Fallback input target used only when no enabled application-specific rule matches.
/// A null target means FlowIME leaves unmatched applications alone.
/// </summary>
public sealed record GlobalDefaultTarget(
    string ProviderId,
    InputAction Action)
{
    public GlobalDefaultTarget Normalize() =>
        this with { ProviderId = InputMethodProviderIds.Normalize(ProviderId) };
}
