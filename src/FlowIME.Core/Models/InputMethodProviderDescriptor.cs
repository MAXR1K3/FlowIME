namespace FlowIME.Core.Models;

/// <summary>
/// Stable identity and declared capabilities for one concrete input method.
/// Id is persistence-facing and must remain stable once rules begin referring to it.
/// </summary>
public sealed record InputMethodProviderDescriptor
{
    public InputMethodProviderDescriptor(
        string id,
        string displayName,
        InputMethodProviderCapabilities capabilities)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(capabilities);

        Id = id;
        DisplayName = displayName;
        Capabilities = capabilities;
    }

    public string Id { get; }

    public string DisplayName { get; }

    public InputMethodProviderCapabilities Capabilities { get; }
}
