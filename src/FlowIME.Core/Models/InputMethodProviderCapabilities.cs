namespace FlowIME.Core.Models;

/// <summary>
/// Describes which operations an input-method provider can perform reliably.
/// Providers must not advertise a capability merely because the platform has a
/// superficially similar API; each flag represents behavior that the provider
/// implementation is prepared to own and verify.
/// </summary>
public sealed record InputMethodProviderCapabilities(
    bool CanDetectActiveProfile,
    bool CanActivateProfile,
    bool CanReadMode,
    bool CanSetChinese,
    bool CanSetEnglish,
    bool RequiresPostActivationSettling);
