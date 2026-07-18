namespace OpenFlowMiner.Core.Domain;

/// <summary>
/// An ingested event log: its cases (traces), an identity, and provenance.
/// </summary>
public sealed record EventLog(string Id, IReadOnlyList<Trace> Traces, LogProvenance Provenance)
{
    /// <summary>Total number of cases in the log.</summary>
    public int CaseCount => Traces.Count;

    /// <summary>Total number of events across all cases.</summary>
    public int EventCount => Traces.Sum(t => t.Events.Count);

    /// <summary>
    /// Builds a log from a flat sequence of events: groups them by case and orders each
    /// case chronologically. Events with identical timestamps within a case keep their
    /// original input order (a stable sort), so coarse timestamps degrade predictably.
    /// </summary>
    public static EventLog FromEvents(string id, IEnumerable<Event> events, LogProvenance provenance)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(provenance);

        var traces = events
            .GroupBy(e => e.CaseId, StringComparer.Ordinal)
            .Select(g => new Trace(
                g.Key,
                // OrderBy is a stable sort in .NET, so timestamp ties preserve input order.
                g.OrderBy(e => e.Timestamp).ToArray()))
            .ToArray();

        return new EventLog(id, traces, provenance);
    }
}
