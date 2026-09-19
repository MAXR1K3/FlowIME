using FlowIME.Core.Automation;
using FlowIME.Core.Context;
using FlowIME.Core.Decisions;
using FlowIME.Core.Models;

namespace FlowIME.Core.Tests.Automation;

public sealed class AutomationDecisionJournalTests
{
    [Fact]
    public void Journal_is_bounded_and_returns_newest_first()
    {
        var journal = new AutomationDecisionJournal(capacity: 2);
        journal.Add(Record(1));
        journal.Add(Record(2));
        journal.Add(Record(3));

        var recent = journal.GetRecent(10);

        Assert.Equal(2, journal.Count);
        Assert.Equal([3L, 2L], recent.Select(record => record.Generation));
    }

    private static AutomationDecisionRecord Record(long generation) =>
        new(
            DateTimeOffset.UtcNow,
            generation,
            InputContextTrigger.ForegroundChanged,
            (nint)0x10,
            "path:c:\\apps\\code.exe",
            "Code",
            [InputContextSignalKind.Application],
            InputDecisionSource.ApplicationRule,
            InputDecisionReasonCode.ApplicationRule,
            "application-rule",
            InputMethodProviderIds.MicrosoftPinyin,
            InputAction.English,
            AutomationDecisionOutcome.Applied,
            Guid.NewGuid(),
            null,
            null);
}
