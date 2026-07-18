using OpenFlowMiner.Core.Mining;

namespace OpenFlowMiner.Core.Tests;

public class BottleneckDetectorTests
{
    [Fact]
    public void Detect_WorkedExample_RanksPaymentReceivedAsWorstBottleneck()
    {
        var bottlenecks = BottleneckDetector.Detect(SampleLogs.WorkedExample());

        // "Payment Received → Shipped" spans ~29h and ~47h across the two cases: by far the slowest step.
        var worst = bottlenecks[0];
        Assert.Equal("Payment Received", worst.Activity);
        Assert.Equal(2, worst.Observations);

        // Sanity: it is strictly slower than every other measured activity.
        Assert.All(bottlenecks.Skip(1), b => Assert.True(b.AverageTimeToNext < worst.AverageTimeToNext));
    }

    [Fact]
    public void Detect_ComputesAverageWaitPerActivity()
    {
        var bottlenecks = BottleneckDetector.Detect(SampleLogs.WorkedExample());

        // Order Placed → next: 5 min (case 1) and 2 min (case 2) → average 3.5 min.
        var orderPlaced = bottlenecks.Single(b => b.Activity == "Order Placed");
        Assert.Equal(TimeSpan.FromMinutes(3.5), orderPlaced.AverageTimeToNext);
        Assert.Equal(2, orderPlaced.Observations);
    }

    [Fact]
    public void Detect_TerminalActivityIsNotABottleneckSource()
    {
        var bottlenecks = BottleneckDetector.Detect(SampleLogs.WorkedExample());
        // "Shipped" is always last — it never has a "time to next", so it must not appear.
        Assert.DoesNotContain(bottlenecks, b => b.Activity == "Shipped");
    }

    [Fact]
    public void Detect_RespectsTopN()
    {
        var bottlenecks = BottleneckDetector.Detect(SampleLogs.WorkedExample(), top: 1);
        Assert.Single(bottlenecks);
        Assert.Equal("Payment Received", bottlenecks[0].Activity);
    }

    [Fact]
    public void Detect_EmptyLog_ReturnsEmpty()
        => Assert.Empty(BottleneckDetector.Detect(SampleLogs.Empty()));
}
