using FlowIME.Core.Models;

namespace FlowIME.Core.Automation;

/// <summary>
/// Remembers the last definite Chinese/English mode of each application, keyed
/// by application identity. Memory lives for the session only; disabling it
/// forgets everything so re-enabling starts from the global default again.
/// </summary>
public sealed class ApplicationModeMemory
{
    public const int MaxApplications = 256;

    private readonly object _sync = new();
    private readonly Dictionary<string, Entry> _modes = new(StringComparer.OrdinalIgnoreCase);
    private long _sequence;
    private bool _enabled;

    public bool Enabled
    {
        get
        {
            lock (_sync)
            {
                return _enabled;
            }
        }
    }

    public int Count
    {
        get
        {
            lock (_sync)
            {
                return _modes.Count;
            }
        }
    }

    public void SetEnabled(bool enabled)
    {
        lock (_sync)
        {
            _enabled = enabled;
            if (!enabled)
            {
                _modes.Clear();
            }
        }
    }

    /// <summary>
    /// Records an observed mode. Anything other than a definite Chinese/English
    /// mode is ignored so an unreadable state never erases a good memory.
    /// </summary>
    public bool Record(string applicationKey, InputMode mode)
    {
        if (string.IsNullOrWhiteSpace(applicationKey) ||
            mode is not (InputMode.Chinese or InputMode.English))
        {
            return false;
        }

        lock (_sync)
        {
            if (!_enabled)
            {
                return false;
            }

            if (!_modes.ContainsKey(applicationKey) && _modes.Count >= MaxApplications)
            {
                var oldest = _modes.MinBy(pair => pair.Value.Sequence).Key;
                _modes.Remove(oldest);
            }

            _modes[applicationKey] = new Entry(mode, ++_sequence);
            return true;
        }
    }

    public InputMode? TryGet(string applicationKey)
    {
        if (string.IsNullOrWhiteSpace(applicationKey))
        {
            return null;
        }

        lock (_sync)
        {
            return _enabled && _modes.TryGetValue(applicationKey, out var entry)
                ? entry.Mode
                : null;
        }
    }

    private readonly record struct Entry(InputMode Mode, long Sequence);
}
