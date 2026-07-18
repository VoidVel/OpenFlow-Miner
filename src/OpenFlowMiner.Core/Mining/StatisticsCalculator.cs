using OpenFlowMiner.Core.Domain;

namespace OpenFlowMiner.Core.Mining;

/// <summary>
/// Computes summary statistics for a log (FR-7): case/event counts, average and median case
/// duration, and the most frequent starting and ending activities.
/// </summary>
public static class StatisticsCalculator
{
    /// <param name="topActivities">How many start/end activities to return, ranked by frequency.</param>
    public static LogStatistics Calculate(EventLog log, int topActivities = 5)
    {
        ArgumentNullException.ThrowIfNull(log);
        if (topActivities < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(topActivities), "topActivities must be non-negative.");
        }

        var nonEmpty = log.Traces.Where(t => t.Events.Count > 0).ToArray();

        var durations = nonEmpty.Select(t => t.Duration).ToArray();
        var startCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var endCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var trace in nonEmpty)
        {
            startCounts[trace.Events[0].Activity] = startCounts.GetValueOrDefault(trace.Events[0].Activity) + 1;
            endCounts[trace.Events[^1].Activity] = endCounts.GetValueOrDefault(trace.Events[^1].Activity) + 1;
        }

        return new LogStatistics(
            CaseCount: log.CaseCount,
            EventCount: log.EventCount,
            AverageCaseDuration: Average(durations),
            MedianCaseDuration: Median(durations),
            TopStartActivities: TopN(startCounts, topActivities),
            TopEndActivities: TopN(endCounts, topActivities));
    }

    private static TimeSpan Average(IReadOnlyCollection<TimeSpan> durations)
    {
        if (durations.Count == 0)
        {
            return TimeSpan.Zero;
        }

        // Sum ticks as long to avoid TimeSpan intermediate-overflow surprises on large logs.
        long totalTicks = 0;
        foreach (var d in durations)
        {
            totalTicks += d.Ticks;
        }

        return new TimeSpan(totalTicks / durations.Count);
    }

    private static TimeSpan Median(IReadOnlyList<TimeSpan> durations)
    {
        if (durations.Count == 0)
        {
            return TimeSpan.Zero;
        }

        var sorted = durations.OrderBy(d => d).ToArray();
        var mid = sorted.Length / 2;

        // Even count → average of the two middle values; odd count → the middle value.
        return sorted.Length % 2 == 0
            ? new TimeSpan((sorted[mid - 1].Ticks + sorted[mid].Ticks) / 2)
            : sorted[mid];
    }

    private static IReadOnlyList<ActivityNode> TopN(Dictionary<string, int> counts, int n)
        => counts
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Take(n)
            .Select(kv => new ActivityNode(kv.Key, kv.Value))
            .ToArray();
}
