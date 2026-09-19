using FlowIME.Core.Models;

namespace FlowIME.Core.Abstractions;

public interface IWindowResolver
{
    ValueTask<WindowContext?> ResolveAsync(
        nint hwnd,
        CancellationToken cancellationToken = default);
}
