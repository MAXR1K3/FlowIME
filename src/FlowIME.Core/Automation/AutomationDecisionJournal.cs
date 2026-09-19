using FlowIME.Core.Context;
using FlowIME.Core.Decisions;
using FlowIME.Core.Models;

namespace FlowIME.Core.Automation;

public enum AutomationDecisionOutcome
{
    NoAction,
    SuppressedByManualOverride,
    Applied,
    ApplyFailed
}

/// <summary>
/// Privacy-bounded explanation record. It intentionally excludes window titles,
/// executable paths, typed text, URLs and clipboard content.
/// </summary>
public sealed record AutomationDecisionRecord(
    DateTimeOffset Timestamp,
    long Generation,
    InputContextTrigger Trigger,
    nint Hwnd,
    string ApplicationKey,
    string ProcessName,
    IReadOnlyList<InputContextSignalKind> Signals,
    InputDecisionSource DecisionSource,
    InputDecisionReasonCode ReasonCode,
    string Reason,
    string? ProviderId,
    InputAction Action,
    AutomationDecisionOutcome Outcome,
    Guid? RuleId,
    string? ContextPolicyId,
    string? ErrorCode,
    uint TargetThreadId = 0,
    string? Backend = null,
    InputState? BeforeState = null,
    InputState? AfterState = null,
    TimeSpan? Duration = null);

public sealed class AutomationDecisionJournal
{
    public const int DefaultCapacity = 128;

    private readonly object _sync = new();
    private readonly Queue<AutomationDecisionRecord> _records;
    private readonly int _capacity;

    public AutomationDecisionJournal(int capacity = DefaultCapacity)
    {
        if (capacity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        _capacity = capacity;
        _records = new Queue<AutomationDecisionRecord>(capacity);
    }

    public int Count
    {
        get
        {
            lock (_sync)
            {
                return _records.Count;
            }
        }
    }

    public void Add(AutomationDecisionRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        lock (_sync)
        {
            while (_records.Count >= _capacity)
            {
                _records.Dequeue();
            }

            _records.Enqueue(record);
        }
    }

    public IReadOnlyList<AutomationDecisionRecord> GetRecent(int maxCount = 20)
    {
        if (maxCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCount));
        }

        lock (_sync)
        {
            return _records
                .Reverse()
                .Take(maxCount)
                .ToArray();
        }
    }
}
