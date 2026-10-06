using FlowIME.Core.Abstractions;
using FlowIME.Core.Models;

namespace FlowIME.Core.InputMethods;

/// <summary>
/// Compatibility adapter between the automation coordinator and provider-specific
/// implementations. Rules may now target a stable provider ID while callers that
/// do not specify one retain the historical default-provider behavior.
/// </summary>
public sealed class ProviderInputMethodBackend : IInputMethodBackend
{
    private readonly InputMethodProviderRegistry _registry;
    private readonly IStandardUsKeyboardBackend? _standardUsKeyboard;

    public ProviderInputMethodBackend(
        InputMethodProviderRegistry registry,
        IStandardUsKeyboardBackend? standardUsKeyboard = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _standardUsKeyboard = standardUsKeyboard;
    }

    public InputMethodProviderDescriptor DefaultProvider =>
        _registry.DefaultProvider.Descriptor;

    public IReadOnlyList<InputMethodProviderDescriptor> Providers =>
        _registry.Descriptors;

    /// <summary>
    /// Reads the provider that is actually active. If TSF detection is unavailable
    /// for the sole P5A provider, fall back to the historical default provider. Once
    /// multiple providers are registered, ambiguous detection fails closed so another
    /// IME is never interpreted using Microsoft-Pinyin-specific semantics. If
    /// detection succeeds but no registered provider is active, report an unsupported
    /// input method.
    /// </summary>
    public async ValueTask<InputState> GetStateAsync(
        WindowContext window,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(window);

        var anySuccessfulDetection = false;
        foreach (var provider in _registry.Providers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var detection = await provider
                .DetectAsync(window, cancellationToken)
                .ConfigureAwait(false);
            if (!detection.Success)
            {
                continue;
            }

            anySuccessfulDetection = true;
            if (detection.IsActive)
            {
                return await provider
                    .GetStateAsync(window, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        if (!anySuccessfulDetection && _registry.Providers.Count == 1)
        {
            return await _registry.DefaultProvider
                .GetStateAsync(window, cancellationToken)
                .ConfigureAwait(false);
        }

        return new InputState(
            anySuccessfulDetection ? "Other input method" : "Input method unavailable",
            InputMode.Unknown,
            0);
    }

    public ValueTask<InputOperationResult> ApplyAsync(
        WindowContext window,
        InputAction action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(window);
        return action == InputAction.StandardUsKeyboard
            ? ApplyStandardUsKeyboardAsync(window, cancellationToken)
            : _registry.DefaultProvider.ApplyAsync(window, action, cancellationToken);
    }

    public ValueTask<InputOperationResult> ApplyAsync(
        WindowContext window,
        string? providerId,
        InputAction action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (action == InputAction.StandardUsKeyboard)
        {
            return ApplyStandardUsKeyboardAsync(window, cancellationToken);
        }

        var normalized = InputMethodProviderIds.Normalize(providerId);
        if (_registry.TryGetProvider(normalized, out var provider) && provider is not null)
        {
            return provider.ApplyAsync(window, action, cancellationToken);
        }

        var unknown = new InputState(null, InputMode.Unknown, 0);
        return ValueTask.FromResult(
            new InputOperationResult(
                Success: false,
                Before: unknown,
                After: unknown,
                Backend: "Input method provider registry",
                ErrorCode: "provider-not-found",
                Duration: TimeSpan.Zero));
    }

    private ValueTask<InputOperationResult> ApplyStandardUsKeyboardAsync(
        WindowContext window,
        CancellationToken cancellationToken)
    {
        if (_standardUsKeyboard is not null)
        {
            return _standardUsKeyboard.ApplyAsync(window, cancellationToken);
        }

        var unknown = new InputState(null, InputMode.Unknown, 0);
        return ValueTask.FromResult(
            new InputOperationResult(
                Success: false,
                Before: unknown,
                After: unknown,
                Backend: "Standard US keyboard",
                ErrorCode: "standard-us-keyboard-backend-unavailable",
                Duration: TimeSpan.Zero));
    }
}
