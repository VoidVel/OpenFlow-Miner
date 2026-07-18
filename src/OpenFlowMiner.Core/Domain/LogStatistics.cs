namespace OpenFlowMiner.Core.Domain;

/// <summary>
/// Summary statistics for an event log: volumes, case durations, and the most common
/// entry and exit activities.
/// </summary>
public sealed record LogStatistics(
    int CaseCount,
    int EventCount,
    TimeSpan AverageCaseDuration,
    TimeSpan MedianCaseDuration,
    IReadOnlyList<ActivityNode> TopStartActivities,
    IReadOnlyList<ActivityNode> TopEndActivities);
