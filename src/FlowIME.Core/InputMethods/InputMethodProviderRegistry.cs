using FlowIME.Core.Abstractions;
using FlowIME.Core.Models;

namespace FlowIME.Core.InputMethods;

/// <summary>
/// Immutable provider catalog keyed by persistence-stable provider IDs. Microsoft
/// Pinyin remains the legacy/default provider while rules may explicitly target
/// additional validated providers such as WeChat Input Method.
/// </summary>
public sealed class InputMethodProviderRegistry
{
    private readonly IReadOnlyDictionary<string, IInputMethodProvider> _providers;
    private readonly IReadOnlyList<IInputMethodProvider> _orderedProviders;
    private readonly IReadOnlyList<InputMethodProviderDescriptor> _descriptors;

    public InputMethodProviderRegistry(
        IEnumerable<IInputMethodProvider> providers,
        string defaultProviderId)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultProviderId);

        var providerArray = providers.ToArray();
        if (providerArray.Length == 0)
        {
            throw new ArgumentException("At least one input-method provider is required.", nameof(providers));
        }

        var map = new Dictionary<string, IInputMethodProvider>(StringComparer.OrdinalIgnoreCase);
        foreach (var provider in providerArray)
        {
            ArgumentNullException.ThrowIfNull(provider);
            var id = provider.Descriptor.Id;
            if (!map.TryAdd(id, provider))
            {
                throw new ArgumentException(
                    $"Duplicate input-method provider id '{id}'.",
                    nameof(providers));
            }
        }

        if (!map.TryGetValue(defaultProviderId, out var defaultProvider))
        {
            throw new ArgumentException(
                $"Default input-method provider '{defaultProviderId}' is not registered.",
                nameof(defaultProviderId));
        }

        _providers = map;
        _orderedProviders = Array.AsReadOnly(providerArray);
        _descriptors = Array.AsReadOnly(
            providerArray.Select(provider => provider.Descriptor).ToArray());
        DefaultProvider = defaultProvider;
    }

    public IInputMethodProvider DefaultProvider { get; }

    public IReadOnlyList<IInputMethodProvider> Providers => _orderedProviders;

    public IReadOnlyList<InputMethodProviderDescriptor> Descriptors => _descriptors;

    public bool TryGetProvider(string providerId, out IInputMethodProvider? provider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        return _providers.TryGetValue(providerId, out provider);
    }

    public IInputMethodProvider GetRequiredProvider(string providerId)
    {
        if (TryGetProvider(providerId, out var provider) && provider is not null)
        {
            return provider;
        }

        throw new KeyNotFoundException(
            $"Input-method provider '{providerId}' is not registered.");
    }
}
