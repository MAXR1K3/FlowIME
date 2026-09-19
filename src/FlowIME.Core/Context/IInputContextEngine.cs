namespace FlowIME.Core.Context;

public interface IInputContextEngine
{
    ValueTask<InputContextSnapshot> ResolveAsync(
        ContextDetectionRequest request,
        CancellationToken cancellationToken = default);
}
