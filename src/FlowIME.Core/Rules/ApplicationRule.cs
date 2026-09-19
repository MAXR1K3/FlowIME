using FlowIME.Core.Models;

namespace FlowIME.Core.Rules;

public sealed record ApplicationRule(
    Guid Id,
    bool Enabled,
    int Priority,
    ApplicationMatch Match,
    InputAction Action,
    string? DisplayName = null,
    string? ProviderId = InputMethodProviderIds.MicrosoftPinyin);
