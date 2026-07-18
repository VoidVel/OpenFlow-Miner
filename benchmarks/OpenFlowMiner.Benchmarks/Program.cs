using System.Diagnostics;
using OpenFlowMiner.Core.Domain;
using OpenFlowMiner.Core.Mining;

// A timed harness (not micro-benchmarking) to find the honest scale ceiling for v1 (NFR-2).
// It mines synthetic logs of growing size end-to-end — build the log, then run all four miners —
// and reports wall-clock and peak managed memory. The numbers this prints back the figure
// published in docs/benchmarks.md; nothing about the ceiling is asserted without a run like this.

int[] sizes = args.Length > 0
    ? args.Select(int.Parse).ToArray()
    : [1_000, 10_000, 50_000, 100_000, 250_000, 500_000, 1_000_000];

Console.WriteLine($"Machine: {Environment.ProcessorCount} logical cores, {Environment.OSVersion}");
Console.WriteLine($"Runtime: .NET {Environment.Version}\n");
Console.WriteLine($"{"events",12} {"cases",9} {"build ms",10} {"mine ms",9} {"total ms",9} {"retained MB",12}");
Console.WriteLine(new string('-', 78));

foreach (var size in sizes)
{
    // Warm once (JIT) at the smallest size only, discarded.
    if (size == sizes[0])
    {
        RunOnce(GenerateEvents(1_000));
    }

    // Baseline BEFORE generating, so the retained delta includes the events the log holds onto.
    var baseline = ForceGcAndMeasure();
    var events = GenerateEvents(size);

    var sw = Stopwatch.StartNew();
    var log = EventLog.FromEvents("bench", events, LogProvenance.Unknown);
    sw.Stop();
    var buildMs = sw.Elapsed.TotalMilliseconds;

    // Release the transient List<Event> container; the Event objects stay, held by the log's traces.
    events = null!;

    // Retained footprint of one stored log — this is what limits an in-memory store.
    var retainedMb = (ForceGcAndMeasure() - baseline) / (1024.0 * 1024.0);

    sw.Restart();
    Mine(log);
    sw.Stop();

    Console.WriteLine(
        $"{size,12:N0} {log.CaseCount,9:N0} {buildMs,10:F1} {sw.Elapsed.TotalMilliseconds,9:F1} {buildMs + sw.Elapsed.TotalMilliseconds,9:F1} {retainedMb,12:F1}");

    GC.KeepAlive(log);
}

Console.WriteLine("\n(retained MB = live heap held by one stored log — the ceiling driver for an in-memory store)");
return;

static long ForceGcAndMeasure()
{
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
    return GC.GetTotalMemory(forceFullCollection: true);
}

static void Mine(EventLog log)
{
    _ = DirectlyFollowsMiner.Mine(log);
    _ = VariantAnalyzer.Analyze(log);
    _ = StatisticsCalculator.Calculate(log);
    _ = BottleneckDetector.Detect(log);
}

static (double buildMs, double mineMs, int cases, int activities) RunOnce(IReadOnlyList<Event> events)
{
    var sw = Stopwatch.StartNew();
    var log = EventLog.FromEvents("bench", events, LogProvenance.Unknown);
    sw.Stop();
    var buildMs = sw.Elapsed.TotalMilliseconds;

    sw.Restart();
    var dfg = DirectlyFollowsMiner.Mine(log);
    _ = VariantAnalyzer.Analyze(log);
    _ = StatisticsCalculator.Calculate(log);
    _ = BottleneckDetector.Detect(log);
    sw.Stop();

    return (buildMs, sw.Elapsed.TotalMilliseconds, log.CaseCount, dfg.Nodes.Count);
}

// Synthetic generator: ~12 events per case over a small activity pool, with a couple of
// alternate paths so variants and bottlenecks are non-trivial. Deterministic (seeded).
static List<Event> GenerateEvents(int targetEvents)
{
    string[] activities = ["Received", "Triaged", "Assigned", "In Progress", "Escalated", "Resolved", "Reopened", "Closed"];
    var rng = new Random(12345);
    var events = new List<Event>(targetEvents);
    var baseTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    var caseIndex = 0;
    while (events.Count < targetEvents)
    {
        var caseId = $"case-{caseIndex++}";
        var steps = 8 + rng.Next(0, 9); // 8..16 events per case
        var t = baseTime.AddMinutes(caseIndex);
        for (var s = 0; s < steps && events.Count < targetEvents; s++)
        {
            var activity = s == 0 ? activities[0] : activities[rng.Next(activities.Length)];
            t = t.AddMinutes(rng.Next(1, 240));
            events.Add(new Event(caseId, activity, t));
        }
    }

    return events;
}
