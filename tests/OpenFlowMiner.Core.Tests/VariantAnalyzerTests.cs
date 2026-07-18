using OpenFlowMiner.Core.Domain;
using OpenFlowMiner.Core.Mining;

namespace OpenFlowMiner.Core.Tests;

public class VariantAnalyzerTests
{
    [Fact]
    public void Analyze_WorkedExample_FindsTwoVariantsEachHalf()
    {
        var variants = VariantAnalyzer.Analyze(SampleLogs.WorkedExample());

        Assert.Equal(2, variants.Count);
        Assert.All(variants, v => Assert.Equal(1, v.CaseCount));
        Assert.All(variants, v => Assert.Equal(50.0, v.Percentage));
        Assert.Equal([1, 2], variants.Select(v => v.Rank));
    }

    [Fact]
    public void Analyze_GroupsIdenticalSequencesAndRanksByFrequency()
    {
        // Three cases take path P, one case takes path Q.
        var t = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var events = new List<Event>();
        foreach (var id in new[] { "1", "2", "3" })
        {
            events.Add(new Event(id, "A", t));
            events.Add(new Event(id, "B", t.AddMinutes(1)));
        }
        events.Add(new Event("4", "A", t));
        events.Add(new Event("4", "C", t.AddMinutes(1)));

        var variants = VariantAnalyzer.Analyze(EventLog.FromEvents("x", events, LogProvenance.Unknown));

        Assert.Equal(2, variants.Count);

        var top = variants[0];
        Assert.Equal(1, top.Rank);
        Assert.Equal(["A", "B"], top.Sequence);
        Assert.Equal(3, top.CaseCount);
        Assert.Equal(75.0, top.Percentage);

        Assert.Equal(["A", "C"], variants[1].Sequence);
        Assert.Equal(25.0, variants[1].Percentage);
    }

    [Fact]
    public void Analyze_RespectsTopN()
    {
        var variants = VariantAnalyzer.Analyze(SampleLogs.WorkedExample(), top: 1);
        Assert.Single(variants);
        Assert.Equal(1, variants[0].Rank);
    }

    [Fact]
    public void Analyze_EmptyLog_ReturnsEmpty()
        => Assert.Empty(VariantAnalyzer.Analyze(SampleLogs.Empty()));

    [Fact]
    public void Analyze_NegativeTop_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() => VariantAnalyzer.Analyze(SampleLogs.WorkedExample(), top: -1));
}
