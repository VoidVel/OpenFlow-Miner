using OpenFlowMiner.Core.Domain;

namespace OpenFlowMiner.Core.Tests;

public class EventLogTests
{
    [Fact]
    public void FromEvents_GroupsByCaseAndOrdersChronologically()
    {
        // Deliberately out of order in the input.
        Event[] events =
        [
            new("A", "Second", new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero)),
            new("A", "First",  new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero)),
            new("B", "Only",   new DateTimeOffset(2026, 1, 1, 9, 30, 0, TimeSpan.Zero)),
        ];

        var log = EventLog.FromEvents("t", events, LogProvenance.Unknown);

        Assert.Equal(2, log.CaseCount);
        Assert.Equal(3, log.EventCount);

        var caseA = log.Traces.Single(t => t.CaseId == "A");
        Assert.Equal(["First", "Second"], caseA.ActivitySequence);
    }

    [Fact]
    public void FromEvents_IsStableForEqualTimestamps()
    {
        var ts = new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);
        Event[] events =
        [
            new("A", "Alpha", ts),
            new("A", "Bravo", ts),
            new("A", "Charlie", ts),
        ];

        var log = EventLog.FromEvents("t", events, LogProvenance.Unknown);

        // Identical timestamps must preserve input order (documented tie rule).
        Assert.Equal(["Alpha", "Bravo", "Charlie"], log.Traces.Single().ActivitySequence);
    }

    [Fact]
    public void FromEvents_RejectsEmptyId()
        => Assert.Throws<ArgumentException>(() => EventLog.FromEvents("", [], LogProvenance.Unknown));

    [Fact]
    public void EmptyLog_HasZeroCounts()
    {
        var log = SampleLogs.Empty();
        Assert.Equal(0, log.CaseCount);
        Assert.Equal(0, log.EventCount);
    }
}
