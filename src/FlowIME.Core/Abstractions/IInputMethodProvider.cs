using FlowIME.Core.Models;

namespace FlowIME.Core.Abstractions;

/// <summary>
/// Provider-specific input-method behavior. Each provider owns the native rules
/// for identifying, activating, reading and mutating its own IME; conversion-mode
/// constants and settle behavior must never leak across providers.
/// </summary>
public interface IInputMethodProvider
{
    InputMethodProviderDescriptor Descriptor { get; }

    ValueTask<InputMethodProviderDetectionResult> DetectAsync(
        WindowContext window,
        CancellationToken cancellationToken = default);

    ValueTask<InputState> GetStateAsync(
        WindowContext window,
        CancellationToken cancellationToken = default);

    ValueTask<InputOperationResult> ApplyAsync(
        WindowContext window,
        InputAction action,
        CancellationToken cancellationToken = default);
}
