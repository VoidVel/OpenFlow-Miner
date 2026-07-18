using OpenFlowMiner.Core.Domain;
using OpenFlowMiner.Core.Mining;

namespace OpenFlowMiner.Api.Contracts;

// Explicit API response shapes. Kept separate from the domain model so the public contract is
// deliberate and stable rather than an accident of internal types — durations, in particular, are
// exposed both as machine-friendly seconds and a human-readable string (NFR-1/NFR-3).

public sealed record ProvenanceDto(string Source, string? Attribution);

public sealed record EventLogSummaryDto(
    string Id,
    int CaseCount,
    int EventCount,
    int ActivityCount,
    int VariantCount,
    ProvenanceDto Provenance);

public sealed record ActivityNodeDto(string Activity, int Frequency);

public sealed record DfEdgeDto(string From, string To, int Frequency);

public sealed record DfgResponse(
    IReadOnlyList<ActivityNodeDto> Nodes,
    IReadOnlyList<DfEdgeDto> Edges,
    IReadOnlyList<string> StartActivities,
    IReadOnlyList<string> EndActivities);

public sealed record VariantDto(int Rank, IReadOnlyList<string> Sequence, int CaseCount, double Percentage);

public sealed record StatisticsResponse(
    int CaseCount,
    int EventCount,
    double AverageCaseDurationSeconds,
    string AverageCaseDuration,
    double MedianCaseDurationSeconds,
    string MedianCaseDuration,
    IReadOnlyList<ActivityNodeDto> TopStartActivities,
    IReadOnlyList<ActivityNodeDto> TopEndActivities);

public sealed record BottleneckDto(
    string Activity,
    double AverageSecondsToNext,
    string AverageTimeToNext,
    int Observations);

/// <summary>Maps mined domain artifacts to their API response shapes.</summary>
public static class ResponseMapper
{
    public static EventLogSummaryDto Summarize(EventLog log)
    {
        var dfg = DirectlyFollowsMiner.Mine(log);
        var variants = VariantAnalyzer.Analyze(log);
        return new EventLogSummaryDto(
            log.Id,
            log.CaseCount,
            log.EventCount,
            dfg.Nodes.Count,
            variants.Count,
            new ProvenanceDto(log.Provenance.Source, log.Provenance.Attribution));
    }

    public static DfgResponse ToDto(DirectlyFollowsGraph dfg) => new(
        dfg.Nodes.Select(n => new ActivityNodeDto(n.Activity, n.Frequency)).ToArray(),
        dfg.Edges.Select(e => new DfEdgeDto(e.From, e.To, e.Frequency)).ToArray(),
        dfg.StartActivities,
        dfg.EndActivities);

    public static IReadOnlyList<VariantDto> ToDto(IReadOnlyList<Variant> variants) => variants
        .Select(v => new VariantDto(v.Rank, v.Sequence, v.CaseCount, Math.Round(v.Percentage, 2)))
        .ToArray();

    public static StatisticsResponse ToDto(LogStatistics stats) => new(
        stats.CaseCount,
        stats.EventCount,
        stats.AverageCaseDuration.TotalSeconds,
        stats.AverageCaseDuration.ToString(),
        stats.MedianCaseDuration.TotalSeconds,
        stats.MedianCaseDuration.ToString(),
        stats.TopStartActivities.Select(a => new ActivityNodeDto(a.Activity, a.Frequency)).ToArray(),
        stats.TopEndActivities.Select(a => new ActivityNodeDto(a.Activity, a.Frequency)).ToArray());

    public static IReadOnlyList<BottleneckDto> ToDto(IReadOnlyList<Bottleneck> bottlenecks) => bottlenecks
        .Select(b => new BottleneckDto(b.Activity, b.AverageTimeToNext.TotalSeconds, b.AverageTimeToNext.ToString(), b.Observations))
        .ToArray();
}
