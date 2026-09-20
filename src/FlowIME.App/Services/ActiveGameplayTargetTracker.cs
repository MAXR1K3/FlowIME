namespace FlowIME.App.Services;

internal readonly record struct ActiveGameplayTargetTransition(
    bool Changed,
    bool Entered,
    RecentGameplayTarget? ActiveTarget);

internal sealed class ActiveGameplayTargetTracker
{
    private readonly object _sync = new();
    private RecentGameplayTarget? _current;

    internal RecentGameplayTarget? Current
    {
        get
        {
            lock (_sync)
            {
                return _current;
            }
        }
    }

    internal ActiveGameplayTargetTransition Update(RecentGameplayTarget? next)
    {
        lock (_sync)
        {
            if (StringComparer.Ordinal.Equals(
                    _current?.ApplicationIdentityKey,
                    next?.ApplicationIdentityKey))
            {
                _current = next;
                return new ActiveGameplayTargetTransition(false, false, _current);
            }

            _current = next;
            return new ActiveGameplayTargetTransition(true, next is not null, next);
        }
    }
}
