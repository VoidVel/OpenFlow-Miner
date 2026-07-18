using OpenFlowMiner.Core.Domain;

namespace OpenFlowMiner.Core.Tests;

/// <summary>
/// Shared fixtures. <see cref="WorkedExample"/> is the exact two-case log from Section 5 of the
/// Product Analysis document, so the documentation example is also a passing test.
/// </summary>
internal static class SampleLogs
{
    private static DateTimeOffset At(int day, int hour, int minute)
        => new(2026, 1, day, hour, minute, 0, TimeSpan.Zero);

    /// <summary>
    /// Case #1: Order Placed → Payment Received → Shipped
    /// Case #2: Order Placed → Payment Failed → Payment Received → Shipped
    /// </summary>
    public static EventLog WorkedExample()
    {
        Event[] events =
        [
            new("1", "Order Placed",     At(1, 9, 0)),
            new("1", "Payment Received", At(1, 9, 5)),
            new("1", "Shipped",          At(2, 14, 0)),
            new("2", "Order Placed",     At(1, 10, 0)),
            new("2", "Payment Failed",   At(1, 10, 2)),
            new("2", "Payment Received", At(1, 10, 15)),
            new("2", "Shipped",          At(3, 9, 0)),
        ];

        return EventLog.FromEvents("worked-example", events, new LogProvenance("Section 5 example"));
    }

    public static EventLog Empty()
        => EventLog.FromEvents("empty", [], LogProvenance.Unknown);
}
