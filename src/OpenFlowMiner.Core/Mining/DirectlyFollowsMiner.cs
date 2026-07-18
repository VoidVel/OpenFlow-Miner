using OpenFlowMiner.Core.Domain;

namespace OpenFlowMiner.Core.Mining;

/// <summary>
/// Computes the Directly-Follows Graph (FR-4, FR-5): for every case, which activity
/// immediately follows which, aggregated across all cases into one frequency-weighted graph.
/// Pure and deterministic — no I/O, no shared state.
/// </summary>
public static class DirectlyFollowsMiner
{
    public static DirectlyFollowsGraph Mine(EventLog log)
    {
        ArgumentNullException.ThrowIfNull(log);

        var nodeFrequency = new Dictionary<string, int>(StringComparer.Ordinal);
        var edgeFrequency = new Dictionary<(string From, string To), int>();
        var startFrequency = new Dictionary<string, int>(StringComparer.Ordinal);
        var endFrequency = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var trace in log.Traces)
        {
            var events = trace.Events;
            if (events.Count == 0)
            {
                continue;
            }

            Increment(startFrequency, events[0].Activity);
            Increment(endFrequency, events[^1].Activity);

            for (var i = 0; i < events.Count; i++)
            {
                Increment(nodeFrequency, events[i].Activity);

                if (i + 1 < events.Count)
                {
                    var key = (events[i].Activity, events[i + 1].Activity);
                    edgeFrequency[key] = edgeFrequency.GetValueOrDefault(key) + 1;
                }
            }
        }

        var nodes = nodeFrequency
            .Select(kv => new ActivityNode(kv.Key, kv.Value))
            .OrderByDescending(n => n.Frequency)
            .ThenBy(n => n.Activity, StringComparer.Ordinal)
            .ToArray();

        var edges = edgeFrequency
            .Select(kv => new DfEdge(kv.Key.From, kv.Key.To, kv.Value))
            .OrderByDescending(e => e.Frequency)
            .ThenBy(e => e.From, StringComparer.Ordinal)
            .ThenBy(e => e.To, StringComparer.Ordinal)
            .ToArray();

        return new DirectlyFollowsGraph(nodes, edges, RankKeys(startFrequency), RankKeys(endFrequency));
    }

    private static void Increment(Dictionary<string, int> counter, string key)
        => counter[key] = counter.GetValueOrDefault(key) + 1;

    private static string[] RankKeys(Dictionary<string, int> counter)
        => counter
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => kv.Key)
            .ToArray();
}
