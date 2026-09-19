using FlowIME.Core.Context;

namespace FlowIME.Core.Abstractions;

public interface IGameTextEntryProfileRepository
{
    ValueTask<IReadOnlyList<GameTextEntryProfile>> GetProfilesAsync(
        CancellationToken cancellationToken = default);

    ValueTask ReplaceProfilesAsync(
        IEnumerable<GameTextEntryProfile> profiles,
        CancellationToken cancellationToken = default);
}
