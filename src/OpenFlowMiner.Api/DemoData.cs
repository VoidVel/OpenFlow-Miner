using OpenFlowMiner.Core.Domain;
using OpenFlowMiner.Ingestion;

namespace OpenFlowMiner.Api;

/// <summary>
/// Seeds pinned demo logs so a first-time visitor sees a real graph immediately with no upload and
/// no signup (Product Principle 4.3, Success 10.2). The hero dataset is a genuine, publicly
/// published municipal process; a tiny built-in sample sits alongside it for a quick illustration.
/// </summary>
public static class DemoData
{
    /// <summary>Small illustrative sample (fabricated, clearly labelled) — an order-fulfilment flow.</summary>
    public const string SampleLogId = "sample";

    /// <summary>The real hero dataset: receipt phase of a Dutch environmental-permit process.</summary>
    public const string ReceiptLogId = "receipt";

    /// <summary>Column mapping for <c>receipt.csv</c> (see samples/data/ATTRIBUTION.md).</summary>
    private static readonly CsvColumnMapping ReceiptMapping = new(
        CaseId: "case:concept:name",
        Activity: "concept:name",
        Timestamp: "time:timestamp",
        Resource: "org:resource");

    public static EventLog BuildSampleLog()
    {
        static DateTimeOffset Ts(int day, int hour, int minute) => new(2026, 1, day, hour, minute, 0, TimeSpan.Zero);

        Event[] events =
        [
            new("case-001", "Order Placed",     Ts(1, 9, 0)),
            new("case-001", "Payment Received", Ts(1, 9, 5)),
            new("case-001", "Shipped",          Ts(2, 14, 0)),
            new("case-001", "Delivered",        Ts(3, 10, 0)),
            new("case-002", "Order Placed",     Ts(1, 10, 0)),
            new("case-002", "Payment Failed",   Ts(1, 10, 2)),
            new("case-002", "Payment Received", Ts(1, 10, 15)),
            new("case-002", "Shipped",          Ts(3, 9, 0)),
            new("case-002", "Delivered",        Ts(4, 9, 0)),
            new("case-003", "Order Placed",     Ts(2, 8, 0)),
            new("case-003", "Payment Received", Ts(2, 8, 10)),
            new("case-003", "Shipped",          Ts(3, 12, 0)),
            new("case-003", "Delivered",        Ts(4, 15, 0)),
        ];

        return EventLog.FromEvents(
            SampleLogId,
            events,
            new LogProvenance("Built-in order-fulfilment sample", "Illustrative sample data (not real)"));
    }

    /// <summary>
    /// Loads the real receipt-phase log from <paramref name="dataDirectory"/>. Returns <c>null</c>
    /// (and logs a warning) if the file is missing or fails to parse, so a self-host without the
    /// dataset still starts — it just won't have the real demo log.
    /// </summary>
    public static EventLog? TryLoadReceiptLog(string dataDirectory, CsvEventLogReader reader, ILogger logger)
    {
        var path = Path.Combine(dataDirectory, "receipt.csv");
        if (!File.Exists(path))
        {
            logger.LogWarning("Real demo dataset not found at {Path}; skipping the '{Id}' log.", path, ReceiptLogId);
            return null;
        }

        using var stream = File.OpenRead(path);
        var result = reader.Read(stream, ReceiptMapping);
        if (!result.IsSuccess)
        {
            logger.LogWarning("Real demo dataset at {Path} failed to parse ({Count} error(s)); skipping.", path, result.Errors.Count);
            return null;
        }

        return EventLog.FromEvents(
            ReceiptLogId,
            result.Events,
            new LogProvenance(
                "Receipt phase of an environmental permit process (WABO), CoSeLoG project",
                "Buijs (2014), 4TU.ResearchData, CC BY — see samples/data/ATTRIBUTION.md"));
    }
}
