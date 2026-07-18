using OpenFlowMiner.Core.Domain;
using OpenFlowMiner.Core.Mining;

namespace OpenFlowMiner.Core.Tests;

public class StatisticsCalculatorTests
{
    [Fact]
    public void Calculate_WorkedExample_ReportsCountsAndDurations()
    {
        var stats = StatisticsCalculator.Calculate(SampleLogs.WorkedExample());

        Assert.Equal(2, stats.CaseCount);
        Assert.Equal(7, stats.EventCount);

        // Case #1: Jan 1 09:00 → Jan 2 14:00 = 29h. Case #2: Jan 1 10:00 → Jan 3 09:00 = 47h.
        Assert.Equal(TimeSpan.FromHours(38), stats.AverageCaseDuration);
        Assert.Equal(TimeSpan.FromHours(38), stats.MedianCaseDuration); // mean of the two values
    }

    [Fact]
    public void Calculate_WorkedExample_ReportsStartAndEndActivities()
    {
        var stats = StatisticsCalculator.Calculate(SampleLogs.WorkedExample());

        var start = Assert.Single(stats.TopStartActivities);
        Assert.Equal("Order Placed", start.Activity);
        Assert.Equal(2, start.Frequency);

        var end = Assert.Single(stats.TopEndActivities);
        Assert.Equal("Shipped", end.Activity);
        Assert.Equal(2, end.Frequency);
    }

    [Fact]
    public void Calculate_MedianOfOddCount_IsMiddleValue()
    {
        // Three cases with durations 1h, 2h, 3h → median 2h, average 2h.
        var baseTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var events = new List<Event>();
        var hours = new[] { 1, 2, 3 };
        for (var i = 0; i < hours.Length; i++)
        {
            var id = (i + 1).ToString();
            events.Add(new Event(id, "Start", baseTime));
            events.Add(new Event(id, "End", baseTime.AddHours(hours[i])));
        }

        var stats = StatisticsCalculator.Calculate(EventLog.FromEvents("x", events, LogProvenance.Unknown));

        Assert.Equal(TimeSpan.FromHours(2), stats.MedianCaseDuration);
        Assert.Equal(TimeSpan.FromHours(2), stats.AverageCaseDuration);
    }

    [Fact]
    public void Calculate_EmptyLog_YieldsZeros()
    {
        var stats = StatisticsCalculator.Calculate(SampleLogs.Empty());

        Assert.Equal(0, stats.CaseCount);
        Assert.Equal(0, stats.EventCount);
        Assert.Equal(TimeSpan.Zero, stats.AverageCaseDuration);
        Assert.Equal(TimeSpan.Zero, stats.MedianCaseDuration);
        Assert.Empty(stats.TopStartActivities);
        Assert.Empty(stats.TopEndActivities);
    }
}
