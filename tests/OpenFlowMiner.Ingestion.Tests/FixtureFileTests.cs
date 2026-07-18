using OpenFlowMiner.Core.Domain;
using OpenFlowMiner.Core.Mining;
using OpenFlowMiner.Ingestion;

namespace OpenFlowMiner.Ingestion.Tests;

/// <summary>
/// Tests driven by the exact fixture files shipped with the project. These lock the
/// implementation to the hand-authored ground truth in <c>expected_results.md</c>.
/// </summary>
public class FixtureFileTests
{
    private static IngestionResult ReadFixture(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", name);
        using var stream = File.OpenRead(path);
        return new CsvEventLogReader().Read(stream);
    }

    // ---- malformed_event_log.csv : the FR-2 error-path fixture ----------------------------

    [Fact]
    public void Malformed_ProducesDistinctRowNumberedErrors()
    {
        var result = ReadFixture("malformed_event_log.csv");

        Assert.False(result.IsSuccess);
        Assert.Empty(result.Events);

        // Exactly the three self-contained broken rows are reported, each specific and by row.
        // (Row 4, "Shipped" with a valid timestamp, is well-formed on its own and is not an error.)
        Assert.Equal(3, result.Errors.Count);

        AssertError(result, row: 2, IngestionErrorCode.UnparseableTimestamp);
        AssertError(result, row: 3, IngestionErrorCode.MissingTimestamp);
        AssertError(result, row: 5, IngestionErrorCode.MissingCaseId);
    }

    [Fact]
    public void Malformed_ErrorsAreNotOneVagueMessage()
    {
        var result = ReadFixture("malformed_event_log.csv");

        // Distinct codes AND distinct rows — the FR-2 promise of specificity.
        Assert.Equal(3, result.Errors.Select(e => e.Code).Distinct().Count());
        Assert.Equal(3, result.Errors.Select(e => e.Row).Distinct().Count());
        Assert.All(result.Errors, e => Assert.False(string.IsNullOrWhiteSpace(e.Message)));
    }

    private static void AssertError(IngestionResult result, int row, string code)
    {
        var error = result.Errors.SingleOrDefault(e => e.Row == row);
        Assert.NotNull(error);
        Assert.Equal(code, error!.Code);
    }

    // ---- sample_event_log.csv : ground truth for the mining engine ------------------------

    [Fact]
    public void Sample_IngestsThirteenEventsAcrossThreeCases()
    {
        var result = ReadFixture("sample_event_log.csv");

        Assert.True(result.IsSuccess);
        Assert.Equal(13, result.Events.Count);
        Assert.Equal(3, result.Events.Select(e => e.CaseId).Distinct().Count());
    }

    [Fact]
    public void Sample_MinedResultsMatchExpectedGroundTruth()
    {
        var result = ReadFixture("sample_event_log.csv");
        var log = EventLog.FromEvents("sample", result.Events, LogProvenance.Unknown);

        // DFG (expected_results.md §1)
        var dfg = DirectlyFollowsMiner.Mine(log);
        Assert.Equal(3, dfg.Edges.Single(e => e is { From: "Payment Received", To: "Shipped" }).Frequency);
        Assert.Equal(3, dfg.Edges.Single(e => e is { From: "Shipped", To: "Delivered" }).Frequency);
        Assert.Equal(["Order Placed"], dfg.StartActivities);
        Assert.Equal(["Delivered"], dfg.EndActivities);

        // Variants (§2): 2 variants, top is the clean path taken by 2 of 3 cases.
        var variants = VariantAnalyzer.Analyze(log);
        Assert.Equal(2, variants.Count);
        Assert.Equal(2, variants[0].CaseCount);
        Assert.Equal(66.7, Math.Round(variants[0].Percentage, 1));

        // Statistics (§3): avg 58h20m, median 55h.
        var stats = StatisticsCalculator.Calculate(log);
        Assert.Equal(3, stats.CaseCount);
        Assert.Equal(13, stats.EventCount);
        Assert.Equal(TimeSpan.FromMinutes(58 * 60 + 20), stats.AverageCaseDuration);
        Assert.Equal(TimeSpan.FromHours(55), stats.MedianCaseDuration);

        // Bottlenecks (§4): Payment Received worst at 34h30m, Delivered excluded.
        var bottlenecks = BottleneckDetector.Detect(log);
        Assert.Equal("Payment Received", bottlenecks[0].Activity);
        Assert.Equal(TimeSpan.FromMinutes(34 * 60 + 30), bottlenecks[0].AverageTimeToNext);
        Assert.DoesNotContain(bottlenecks, b => b.Activity == "Delivered");
    }
}
