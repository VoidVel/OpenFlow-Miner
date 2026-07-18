using OpenFlowMiner.Core.Domain;

namespace OpenFlowMiner.Core.Mining;

/// <summary>
/// Identifies candidate bottleneck activities (FR-8): for each activity, the average time
/// elapsed until the next event in the same case, ranked so the longest delays surface first.
/// </summary>
public static class BottleneckDetector
{
    /// <param name="top">If given, return only the <paramref name="top"/> slowest activities.</param>
    public static IReadOnlyList<Bottleneck> Detect(EventLog log, int? top = null)
    {
        ArgumentNullException.ThrowIfNull(log);
        if (top is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(top), "top must be non-negative.");
        }

        // Per source activity: accumulated wait ticks and the number of transitions observed.
        var totals = new Dictionary<string, (long Ticks, int Count)>(StringComparer.Ordinal);

        foreach (var trace in log.Traces)
        {
            var events = trace.Events;
            for (var i = 0; i + 1 < events.Count; i++)
            {
                var activity = events[i].Activity;
                // Events are chronologically ordered, so this delta is non-negative.
                var wait = (events[i + 1].Timestamp - events[i].Timestamp).Ticks;

                var current = totals.GetValueOrDefault(activity);
                totals[activity] = (current.Ticks + wait, current.Count + 1);
            }
        }

        IEnumerable<Bottleneck> ranked = totals
            .Select(kv => new Bottleneck(
                Activity: kv.Key,
                AverageTimeToNext: new TimeSpan(kv.Value.Ticks / kv.Value.Count),
                Observations: kv.Value.Count))
            .OrderByDescending(b => b.AverageTimeToNext)
            .ThenBy(b => b.Activity, StringComparer.Ordinal);

        if (top is { } n)
        {
            ranked = ranked.Take(n);
        }

        return ranked.ToArray();
    }
}
