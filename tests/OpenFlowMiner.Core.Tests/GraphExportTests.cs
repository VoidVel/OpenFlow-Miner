using OpenFlowMiner.Core.Export;
using OpenFlowMiner.Core.Mining;

namespace OpenFlowMiner.Core.Tests;

public class GraphExportTests
{
    [Fact]
    public void ToMermaid_ProducesValidFlowchartWithFrequencyLabels()
    {
        var dfg = DirectlyFollowsMiner.Mine(SampleLogs.WorkedExample());
        var mermaid = GraphExport.ToMermaid(dfg);

        Assert.StartsWith("graph LR", mermaid);
        // Every node is declared and every edge carries its frequency as a Mermaid edge label.
        Assert.Contains("[\"Payment Received\"]", mermaid);
        Assert.Contains("-->|2|", mermaid); // Payment Received -> Shipped occurs twice
    }

    [Fact]
    public void ToDot_ProducesDigraphWithFrequencyLabels()
    {
        var dfg = DirectlyFollowsMiner.Mine(SampleLogs.WorkedExample());
        var dot = GraphExport.ToDot(dfg);

        Assert.StartsWith("digraph process {", dot);
        Assert.Contains("rankdir=LR;", dot);
        Assert.Contains("\"Payment Received\" -> \"Shipped\" [label=\"2\"];", dot);
        Assert.EndsWith("}", dot);
    }

    [Fact]
    public void ToMermaid_EscapesQuotesInActivityNames()
    {
        var log = OpenFlowMiner.Core.Domain.EventLog.FromEvents(
            "x",
            [
                new OpenFlowMiner.Core.Domain.Event("c", "Say \"hi\"", DateTimeOffset.UnixEpoch),
                new OpenFlowMiner.Core.Domain.Event("c", "Done", DateTimeOffset.UnixEpoch.AddMinutes(1)),
            ],
            OpenFlowMiner.Core.Domain.LogProvenance.Unknown);

        var mermaid = GraphExport.ToMermaid(DirectlyFollowsMiner.Mine(log));

        Assert.DoesNotContain("\"hi\"", mermaid); // raw quotes would break Mermaid parsing
        Assert.Contains("&quot;", mermaid);
    }
}
