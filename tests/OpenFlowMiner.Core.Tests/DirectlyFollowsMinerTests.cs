using OpenFlowMiner.Core.Domain;
using OpenFlowMiner.Core.Mining;

namespace OpenFlowMiner.Core.Tests;

public class DirectlyFollowsMinerTests
{
    [Fact]
    public void Mine_CountsNodeFrequenciesFromWorkedExample()
    {
        var dfg = DirectlyFollowsMiner.Mine(SampleLogs.WorkedExample());

        Assert.Equal(2, FrequencyOf(dfg, "Order Placed"));
        Assert.Equal(2, FrequencyOf(dfg, "Payment Received"));
        Assert.Equal(2, FrequencyOf(dfg, "Shipped"));
        Assert.Equal(1, FrequencyOf(dfg, "Payment Failed"));
    }

    [Fact]
    public void Mine_CountsDirectlyFollowsEdges()
    {
        var dfg = DirectlyFollowsMiner.Mine(SampleLogs.WorkedExample());

        // Payment Received → Shipped happens in BOTH cases.
        Assert.Equal(2, EdgeFrequency(dfg, "Payment Received", "Shipped"));
        Assert.Equal(1, EdgeFrequency(dfg, "Order Placed", "Payment Received"));
        Assert.Equal(1, EdgeFrequency(dfg, "Order Placed", "Payment Failed"));
        Assert.Equal(1, EdgeFrequency(dfg, "Payment Failed", "Payment Received"));
        // No case ever goes Shipped → anything.
        Assert.DoesNotContain(dfg.Edges, e => e.From == "Shipped");
    }

    [Fact]
    public void Mine_IdentifiesStartAndEndActivities()
    {
        var dfg = DirectlyFollowsMiner.Mine(SampleLogs.WorkedExample());

        Assert.Equal(["Order Placed"], dfg.StartActivities);
        Assert.Equal(["Shipped"], dfg.EndActivities);
    }

    [Fact]
    public void Mine_EmptyLog_ProducesEmptyGraph()
    {
        var dfg = DirectlyFollowsMiner.Mine(SampleLogs.Empty());

        Assert.Empty(dfg.Nodes);
        Assert.Empty(dfg.Edges);
        Assert.Empty(dfg.StartActivities);
        Assert.Empty(dfg.EndActivities);
    }

    [Fact]
    public void Mine_SingleEventCase_HasNodeButNoEdge()
    {
        var log = EventLog.FromEvents(
            "single",
            [new Event("A", "Lonely", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero))],
            LogProvenance.Unknown);

        var dfg = DirectlyFollowsMiner.Mine(log);

        Assert.Single(dfg.Nodes);
        Assert.Empty(dfg.Edges);
        Assert.Equal(["Lonely"], dfg.StartActivities);
        Assert.Equal(["Lonely"], dfg.EndActivities);
    }

    private static int FrequencyOf(DirectlyFollowsGraph dfg, string activity)
        => dfg.Nodes.Single(n => n.Activity == activity).Frequency;

    private static int EdgeFrequency(DirectlyFollowsGraph dfg, string from, string to)
        => dfg.Edges.SingleOrDefault(e => e.From == from && e.To == to)?.Frequency ?? 0;
}
