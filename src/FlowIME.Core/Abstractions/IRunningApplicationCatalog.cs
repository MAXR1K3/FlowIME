using FlowIME.Core.Models;

namespace FlowIME.Core.Abstractions;

public interface IRunningApplicationCatalog
{
    ValueTask<IReadOnlyList<RunningApplication>> GetRunningApplicationsAsync(
        CancellationToken cancellationToken = default);
}
