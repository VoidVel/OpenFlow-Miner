using OpenFlowMiner.Core.Domain;
using OpenFlowMiner.Core.Mining;
using OpenFlowMiner.Ingestion;

// OpenFlow Miner engine demo. With no argument it mines the built-in Section-5 example
// (proving Core stands alone). Given a CSV path it ingests that file (Milestone 2) and either
// prints the mined results or the row-numbered ingestion errors.

if (args.Length > 0)
{
    return RunIngestion(args[0]);
}

MineAndPrint(BuiltInExampleLog(), "built-in worked example");
return 0;

static int RunIngestion(string path)
{
    if (!File.Exists(path))
    {
        Console.Error.WriteLine($"File not found: {path}");
        return 2;
    }

    Console.WriteLine($"Ingesting '{Path.GetFileName(path)}' ...\n");

    using var stream = File.OpenRead(path);
    var result = new CsvEventLogReader().Read(stream);

    if (!result.IsSuccess)
    {
        Console.WriteLine($"REJECTED — {result.Errors.Count} problem(s), nothing was ingested:\n");
        foreach (var e in result.Errors)
        {
            var where = e.Row > 0 ? $"row {e.Row}" : "file";
            var column = e.Column is { } c ? $" [column '{c}']" : "";
            Console.WriteLine($"  • {where,-6} {e.Code,-22}{column}  {e.Message}");
        }

        return 1;
    }

    var log = EventLog.FromEvents(Path.GetFileNameWithoutExtension(path), result.Events, new LogProvenance(Path.GetFileName(path)));
    MineAndPrint(log, log.Provenance.Source);
    return 0;
}

static void MineAndPrint(EventLog log, string source)
{
    Console.WriteLine("OpenFlow Miner — engine demo");
    Console.WriteLine($"Source '{source}': {log.CaseCount} cases, {log.EventCount} events");
    Console.WriteLine(new string('-', 60));

    Console.WriteLine("\nDirectly-Follows Graph (edges by frequency):");
    var dfg = DirectlyFollowsMiner.Mine(log);
    foreach (var edge in dfg.Edges)
    {
        Console.WriteLine($"  {edge.From,-18} -> {edge.To,-18} x{edge.Frequency}");
    }

    Console.WriteLine($"  start: {string.Join(", ", dfg.StartActivities)}   end: {string.Join(", ", dfg.EndActivities)}");

    Console.WriteLine("\nProcess variants (most common paths):");
    foreach (var v in VariantAnalyzer.Analyze(log))
    {
        Console.WriteLine($"  #{v.Rank} ({v.CaseCount} case(s), {v.Percentage:0.#}%): {string.Join(" -> ", v.Sequence)}");
    }

    Console.WriteLine("\nStatistics:");
    var stats = StatisticsCalculator.Calculate(log);
    Console.WriteLine($"  cases={stats.CaseCount}  events={stats.EventCount}");
    Console.WriteLine($"  avg case duration={stats.AverageCaseDuration}  median={stats.MedianCaseDuration}");

    Console.WriteLine("\nBottlenecks (slowest step-to-next first):");
    foreach (var b in BottleneckDetector.Detect(log))
    {
        Console.WriteLine($"  {b.Activity,-18} avg wait {b.AverageTimeToNext}  ({b.Observations} obs)");
    }
}

static EventLog BuiltInExampleLog()
{
    DateTimeOffset Utc(int day, int hour, int minute) => new(2026, 1, day, hour, minute, 0, TimeSpan.Zero);

    Event[] events =
    [
        new("1", "Order Placed",     Utc(1, 9, 0)),
        new("1", "Payment Received", Utc(1, 9, 5)),
        new("1", "Shipped",          Utc(2, 14, 0)),
        new("2", "Order Placed",     Utc(1, 10, 0)),
        new("2", "Payment Failed",   Utc(1, 10, 2)),
        new("2", "Payment Received", Utc(1, 10, 15)),
        new("2", "Shipped",          Utc(3, 9, 0)),
    ];

    return EventLog.FromEvents("demo", events, new LogProvenance("built-in worked example"));
}
