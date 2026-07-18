namespace OpenFlowMiner.Core.Domain;

/// <summary>
/// One case = one instance of a process running end to end, with its events in time order.
/// </summary>
public sealed record Trace(string CaseId, IReadOnlyList<Event> Events)
{
    /// <summary>The ordered sequence of activity names this case went through.</summary>
    public IReadOnlyList<string> ActivitySequence => Events.Select(e => e.Activity).ToArray();

    /// <summary>Timestamp of the first event, or <c>null</c> for an empty trace.</summary>
    public DateTimeOffset? Start => Events.Count == 0 ? null : Events[0].Timestamp;

    /// <summary>Timestamp of the last event, or <c>null</c> for an empty trace.</summary>
    public DateTimeOffset? End => Events.Count == 0 ? null : Events[^1].Timestamp;

    /// <summary>Elapsed time from first to last event, or <see cref="TimeSpan.Zero"/> if fewer than two events.</summary>
    public TimeSpan Duration => Start is { } s && End is { } e ? e - s : TimeSpan.Zero;
}
