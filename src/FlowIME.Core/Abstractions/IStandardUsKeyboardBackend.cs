using FlowIME.Core.Models;

namespace FlowIME.Core.Abstractions;

public interface IStandardUsKeyboardBackend
{
    ValueTask<InputOperationResult> ApplyAsync(
        WindowContext window,
        CancellationToken cancellationToken = default);
}
