using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace OpenFlowMiner.Api.Tests;

public class EventLogApiTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static MultipartFormDataContent CsvUpload(string csv, string fileName = "log.csv")
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/csv");
        content.Add(file, "file", fileName);
        return content;
    }

    // ---- Read path against the seeded demo log --------------------------------------------

    [Fact]
    public async Task Get_DemoSummary_ReturnsExpectedShape()
    {
        var summary = await _client.GetFromJsonAsync<JsonElement>("/api/v1/event-logs/sample");

        Assert.Equal("sample", summary.GetProperty("id").GetString());
        Assert.Equal(3, summary.GetProperty("caseCount").GetInt32());
        Assert.Equal(13, summary.GetProperty("eventCount").GetInt32());
        Assert.Equal(2, summary.GetProperty("variantCount").GetInt32());
    }

    [Fact]
    public async Task Get_DemoDfg_Json_HasNodesAndEdges()
    {
        var dfg = await _client.GetFromJsonAsync<JsonElement>("/api/v1/event-logs/sample/dfg");

        Assert.Equal(5, dfg.GetProperty("nodes").GetArrayLength());
        Assert.Contains("Order Placed", dfg.GetProperty("startActivities").EnumerateArray().Select(e => e.GetString()));
        Assert.Contains("Delivered", dfg.GetProperty("endActivities").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task Get_DemoDfg_Mermaid_RendersFlowchart()
    {
        var response = await _client.GetAsync("/api/v1/event-logs/sample/dfg?format=mermaid");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("graph LR", body);
        Assert.Contains("-->|3|", body); // Payment Received -> Shipped, 3 times
    }

    [Fact]
    public async Task Get_DemoStatistics_MatchesGroundTruth()
    {
        var stats = await _client.GetFromJsonAsync<JsonElement>("/api/v1/event-logs/sample/statistics");

        // avg 58h20m = 210000s, median 55h = 198000s (expected_results.md §3)
        Assert.Equal(210000, stats.GetProperty("averageCaseDurationSeconds").GetDouble());
        Assert.Equal(198000, stats.GetProperty("medianCaseDurationSeconds").GetDouble());
    }

    [Fact]
    public async Task Get_DemoBottlenecks_RanksPaymentReceivedFirst()
    {
        var bottlenecks = await _client.GetFromJsonAsync<JsonElement>("/api/v1/event-logs/sample/bottlenecks");
        Assert.Equal("Payment Received", bottlenecks[0].GetProperty("activity").GetString());
    }

    [Fact]
    public async Task Get_UnknownLog_Returns404ProblemJson()
    {
        var response = await _client.GetAsync("/api/v1/event-logs/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problem = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
        Assert.Contains("event-log-not-found", problem.GetProperty("type").GetString());
    }

    [Fact]
    public async Task Get_Variants_TopZero_Returns400()
    {
        var response = await _client.GetAsync("/api/v1/event-logs/sample/variants?top=0");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---- Write path: upload, then round-trip ----------------------------------------------

    [Fact]
    public async Task Post_ValidCsv_Creates_AndIsRetrievable()
    {
        using var upload = CsvUpload("""
            case_id,activity,timestamp
            c1,Start,2026-01-01T09:00:00Z
            c1,End,2026-01-01T10:00:00Z
            """);

        var post = await _client.PostAsync("/api/v1/event-logs", upload);
        Assert.Equal(HttpStatusCode.Created, post.StatusCode);
        Assert.NotNull(post.Headers.Location);

        var created = JsonSerializer.Deserialize<JsonElement>(await post.Content.ReadAsStringAsync());
        var id = created.GetProperty("id").GetString()!;

        var summary = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/event-logs/{id}");
        Assert.Equal(1, summary.GetProperty("caseCount").GetInt32());
        Assert.Equal(2, summary.GetProperty("eventCount").GetInt32());
    }

    [Fact]
    public async Task Post_MalformedCsv_Returns422_WithRowNumberedErrors()
    {
        using var upload = CsvUpload("""
            case_id,activity,timestamp
            c1,Ok,2026-01-01T09:00:00Z
            c1,BadDate,2026-13-99T09:00:00Z
            ,NoCase,2026-01-01T10:00:00Z
            """);

        var response = await _client.PostAsync("/api/v1/event-logs", upload);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problem = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
        var errors = problem.GetProperty("errors").EnumerateArray().ToArray();

        Assert.Equal(2, errors.Length);
        Assert.Contains(errors, e => e.GetProperty("code").GetString() == "unparseable-timestamp" && e.GetProperty("row").GetInt32() == 2);
        Assert.Contains(errors, e => e.GetProperty("code").GetString() == "missing-case-id" && e.GetProperty("row").GetInt32() == 3);
    }

    [Fact]
    public async Task Post_NoFile_Returns400()
    {
        using var empty = new MultipartFormDataContent { { new StringContent("x"), "notafile" } };
        var response = await _client.PostAsync("/api/v1/event-logs", empty);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task OpenApiDocument_IsServed()
    {
        var response = await _client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Get_RealReceiptDemoLog_IsSeededAndMineable()
    {
        // The hero dataset (real, published municipal process) must back the live demo (Success 10.2).
        var summary = await _client.GetFromJsonAsync<JsonElement>("/api/v1/event-logs/receipt");

        Assert.Equal(1434, summary.GetProperty("caseCount").GetInt32());
        Assert.Equal(8577, summary.GetProperty("eventCount").GetInt32());
        Assert.Contains("CoSeLoG", summary.GetProperty("provenance").GetProperty("source").GetString());

        // And it actually mines: the DFG has a real start activity.
        var dfg = await _client.GetFromJsonAsync<JsonElement>("/api/v1/event-logs/receipt/dfg");
        Assert.True(dfg.GetProperty("nodes").GetArrayLength() > 1);
    }
}
