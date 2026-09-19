namespace FlowIME.Core.Context;

public enum GameTextEntryActivationSource
{
    None,
    StandardTextControl,
    HotkeyProfile,
    Manual,
    Adapter
}

public sealed record GameTextEntryRuntimeSnapshot(
    bool Active,
    long Generation,
    string ApplicationKey,
    uint ProcessId,
    nint TopLevelHwnd,
    string ProcessName,
    GameTextEntryActivationSource Source,
    string ProfileId,
    DateTimeOffset? ActivatedAt,
    DateTimeOffset? LastChangedAt,
    string LastReason);

/// <summary>
/// Thread-safe runtime latch for an in-game text-entry session. It stores only
/// application/window identity and activation metadata; it never stores typed text,
/// chat contents, URLs, clipboard data or accessibility values.
///
/// P8C.2 hotkey profiles and high-confidence standard-control focus detection drive
/// this latch. It stores only state metadata, never text content.
/// </summary>
public sealed class GameTextEntryRuntimeState
{
    private readonly object _sync = new();
    private RuntimeValue _value = RuntimeValue.Inactive;
    private long _generation;

    public GameTextEntryRuntimeSnapshot GetSnapshot()
    {
        lock (_sync)
        {
            return ToSnapshot(_value, _generation);
        }
    }

    public bool Activate(
        ApplicationIdentity application,
        uint processId,
        nint topLevelHwnd,
        string? processName,
        GameTextEntryActivationSource source,
        string? profileId = null,
        string reason = "activated")
    {
        ArgumentNullException.ThrowIfNull(application);
        if (source == GameTextEntryActivationSource.None)
        {
            throw new ArgumentOutOfRangeException(nameof(source));
        }

        var normalizedProfileId = NormalizeToken(profileId, "none");
        var normalizedProcessName = NormalizeToken(processName, "unknown");
        var normalizedReason = NormalizeToken(reason, "activated");
        var now = DateTimeOffset.UtcNow;

        lock (_sync)
        {
            var alreadyEquivalent = _value.Active &&
                StringComparer.Ordinal.Equals(_value.ApplicationKey, application.Key) &&
                _value.ProcessId == processId &&
                _value.TopLevelHwnd == topLevelHwnd &&
                _value.Source == source &&
                StringComparer.Ordinal.Equals(_value.ProfileId, normalizedProfileId);

            if (alreadyEquivalent)
            {
                _value = _value with
                {
                    ProcessName = normalizedProcessName,
                    LastChangedAt = now,
                    LastReason = normalizedReason
                };
                return false;
            }

            _generation++;
            _value = new RuntimeValue(
                Active: true,
                ApplicationKey: application.Key,
                ProcessId: processId,
                TopLevelHwnd: topLevelHwnd,
                ProcessName: normalizedProcessName,
                Source: source,
                ProfileId: normalizedProfileId,
                ActivatedAt: now,
                LastChangedAt: now,
                LastReason: normalizedReason);
            return true;
        }
    }

    public bool Deactivate(string reason = "deactivated")
    {
        var normalizedReason = NormalizeToken(reason, "deactivated");
        var now = DateTimeOffset.UtcNow;

        lock (_sync)
        {
            if (!_value.Active)
            {
                return false;
            }

            _generation++;
            _value = RuntimeValue.Inactive with
            {
                LastChangedAt = now,
                LastReason = normalizedReason
            };
            return true;
        }
    }

    public bool IsActiveFor(
        ApplicationIdentity application,
        uint processId,
        nint topLevelHwnd = 0)
    {
        ArgumentNullException.ThrowIfNull(application);

        lock (_sync)
        {
            return _value.Active &&
                StringComparer.Ordinal.Equals(_value.ApplicationKey, application.Key) &&
                (_value.ProcessId == 0 || processId == 0 || _value.ProcessId == processId) &&
                (_value.TopLevelHwnd == 0 || topLevelHwnd == 0 || _value.TopLevelHwnd == topLevelHwnd);
        }
    }

    private static GameTextEntryRuntimeSnapshot ToSnapshot(
        RuntimeValue value,
        long generation) =>
        new(
            value.Active,
            generation,
            value.ApplicationKey,
            value.ProcessId,
            value.TopLevelHwnd,
            value.ProcessName,
            value.Source,
            value.ProfileId,
            value.ActivatedAt,
            value.LastChangedAt,
            value.LastReason);

    private static string NormalizeToken(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return value.Trim()
            .Replace('|', '_')
            .Replace('\r', ' ')
            .Replace('\n', ' ');
    }

    private sealed record RuntimeValue(
        bool Active,
        string ApplicationKey,
        uint ProcessId,
        nint TopLevelHwnd,
        string ProcessName,
        GameTextEntryActivationSource Source,
        string ProfileId,
        DateTimeOffset? ActivatedAt,
        DateTimeOffset? LastChangedAt,
        string LastReason)
    {
        public static RuntimeValue Inactive { get; } = new(
            Active: false,
            ApplicationKey: "none",
            ProcessId: 0,
            TopLevelHwnd: 0,
            ProcessName: "none",
            Source: GameTextEntryActivationSource.None,
            ProfileId: "none",
            ActivatedAt: null,
            LastChangedAt: null,
            LastReason: "none");
    }
}
