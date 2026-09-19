using FlowIME.Core.Models;

namespace FlowIME.Core.Abstractions;

public interface IInputMethodBackend
{
    ValueTask<InputState> GetStateAsync(
        WindowContext window,
        CancellationToken cancellationToken = default);

    ValueTask<InputOperationResult> ApplyAsync(
        WindowContext window,
        InputAction action,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies a rule to a specific provider. The default interface implementation
    /// preserves compatibility with pre-P5B backends and test doubles; the provider
    /// registry backend overrides it and performs real provider dispatch.
    /// </summary>
    ValueTask<InputOperationResult> ApplyAsync(
        WindowContext window,
        string? providerId,
        InputAction action,
        CancellationToken cancellationToken = default) =>
        ApplyAsync(window, action, cancellationToken);
}
