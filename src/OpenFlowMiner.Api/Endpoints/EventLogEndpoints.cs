using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using OpenFlowMiner.Api.Configuration;
using OpenFlowMiner.Api.Contracts;
using OpenFlowMiner.Core.Domain;
using OpenFlowMiner.Core.Export;
using OpenFlowMiner.Core.Mining;
using OpenFlowMiner.Core.Storage;
using OpenFlowMiner.Ingestion;

namespace OpenFlowMiner.Api.Endpoints;

/// <summary>The versioned public API surface (FR-9). This IS the product; the reference client is secondary.</summary>
public static class EventLogEndpoints
{
    private const string ErrorBase = "https://openflowminer.dev/errors/";

    public static RouteGroupBuilder MapEventLogEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/event-logs").WithTags("Event Logs");

        group.MapPost("/", IngestAsync)
            .WithSummary("Ingest a CSV event log")
            .WithDescription("Upload a CSV (multipart form field 'file'). Optional form fields caseId/activity/timestamp/resource map columns. Returns the stored log summary.")
            .Accepts<IFormFile>("multipart/form-data")
            .Produces<EventLogSummaryDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .DisableAntiforgery();

        group.MapGet("/{id}", GetSummaryAsync)
            .WithSummary("Get a stored log's summary")
            .Produces<EventLogSummaryDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{id}/dfg", GetDfgAsync)
            .WithSummary("Get the directly-follows graph")
            .WithDescription("format=json (default) | mermaid | dot")
            .Produces<DfgResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{id}/variants", GetVariantsAsync)
            .WithSummary("Get ranked process variants")
            .Produces<IReadOnlyList<VariantDto>>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{id}/statistics", GetStatisticsAsync)
            .WithSummary("Get summary statistics")
            .Produces<StatisticsResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{id}/bottlenecks", GetBottlenecksAsync)
            .WithSummary("Get ranked bottleneck activities")
            .Produces<IReadOnlyList<BottleneckDto>>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return group;
    }

    private static async Task<IResult> IngestAsync(
        HttpRequest request,
        IEventLogStore store,
        CsvEventLogReader reader,
        IOptions<IngestionOptions> ingestion,
        IOptions<StorageOptions> storage,
        CancellationToken cancellationToken)
    {
        if (!request.HasFormContentType)
        {
            return Problem("unsupported-media-type", "Expected multipart/form-data", StatusCodes.Status415UnsupportedMediaType,
                "Upload the CSV as multipart/form-data with a file field named 'file'.");
        }

        var form = await request.ReadFormAsync(cancellationToken);
        var file = form.Files["file"] ?? form.Files.FirstOrDefault();
        if (file is null || file.Length == 0)
        {
            return Problem("no-file", "No file uploaded", StatusCodes.Status400BadRequest,
                "Include a non-empty CSV in the 'file' form field.");
        }

        var mapping = MappingFromForm(form);

        IngestionResult result;
        await using (var stream = file.OpenReadStream())
        {
            result = reader.Read(stream, mapping);
        }

        if (!result.IsSuccess)
        {
            return ValidationProblem(result.Errors);
        }

        if (result.Events.Count > ingestion.Value.MaxEvents)
        {
            return Problem("event-log-too-large", "Event log too large", StatusCodes.Status413PayloadTooLarge,
                $"Log has {result.Events.Count:N0} events; the configured limit is {ingestion.Value.MaxEvents:N0}. See docs/benchmarks.md.");
        }

        var id = Guid.NewGuid().ToString("N")[..12];
        var log = EventLog.FromEvents(id, result.Events, new LogProvenance(file.FileName));
        await store.SaveAsync(log, TimeSpan.FromHours(storage.Value.UploadTtlHours), cancellationToken);

        return Results.Created($"/api/v1/event-logs/{id}", ResponseMapper.Summarize(log));
    }

    private static async Task<IResult> GetSummaryAsync(string id, IEventLogStore store, CancellationToken cancellationToken)
    {
        var log = await store.GetAsync(id, cancellationToken);
        return log is null ? NotFound(id) : Results.Ok(ResponseMapper.Summarize(log));
    }

    private static async Task<IResult> GetDfgAsync(string id, string? format, IEventLogStore store, CancellationToken cancellationToken)
    {
        var log = await store.GetAsync(id, cancellationToken);
        if (log is null)
        {
            return NotFound(id);
        }

        var dfg = DirectlyFollowsMiner.Mine(log);
        return (format?.ToLowerInvariant()) switch
        {
            null or "" or "json" => Results.Ok(ResponseMapper.ToDto(dfg)),
            "mermaid" => Results.Text(GraphExport.ToMermaid(dfg), "text/plain"),
            "dot" => Results.Text(GraphExport.ToDot(dfg), "text/vnd.graphviz"),
            _ => Problem("unsupported-format", "Unsupported format", StatusCodes.Status400BadRequest,
                $"Unknown format '{format}'. Use json, mermaid, or dot."),
        };
    }

    private static async Task<IResult> GetVariantsAsync(string id, int? top, IEventLogStore store, CancellationToken cancellationToken)
    {
        if (top is <= 0)
        {
            return InvalidTop();
        }

        var log = await store.GetAsync(id, cancellationToken);
        return log is null ? NotFound(id) : Results.Ok(ResponseMapper.ToDto(VariantAnalyzer.Analyze(log, top)));
    }

    private static async Task<IResult> GetStatisticsAsync(string id, IEventLogStore store, CancellationToken cancellationToken)
    {
        var log = await store.GetAsync(id, cancellationToken);
        return log is null ? NotFound(id) : Results.Ok(ResponseMapper.ToDto(StatisticsCalculator.Calculate(log)));
    }

    private static async Task<IResult> GetBottlenecksAsync(string id, int? top, IEventLogStore store, CancellationToken cancellationToken)
    {
        if (top is <= 0)
        {
            return InvalidTop();
        }

        var log = await store.GetAsync(id, cancellationToken);
        return log is null ? NotFound(id) : Results.Ok(ResponseMapper.ToDto(BottleneckDetector.Detect(log, top)));
    }

    private static CsvColumnMapping MappingFromForm(IFormCollection form)
    {
        string Field(string key, string fallback)
            => form.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v.ToString().Trim() : fallback;

        string? Optional(string key)
            => form.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v.ToString().Trim() : null;

        return new CsvColumnMapping(
            Field("caseId", "case_id"),
            Field("activity", "activity"),
            Field("timestamp", "timestamp"),
            Optional("resource"));
    }

    private static IResult NotFound(string id)
        => Problem("event-log-not-found", "Event log not found", StatusCodes.Status404NotFound, $"No event log with id '{id}'.");

    private static IResult InvalidTop()
        => Problem("invalid-top", "Invalid 'top' parameter", StatusCodes.Status400BadRequest, "'top' must be a positive integer.");

    private static IResult ValidationProblem(IReadOnlyList<IngestionError> errors)
    {
        var problem = new ProblemDetails
        {
            Type = ErrorBase + "event-log-validation",
            Title = "Event log validation failed",
            Status = StatusCodes.Status422UnprocessableEntity,
            Detail = $"{errors.Count} row(s) could not be ingested. Nothing was stored.",
        };

        problem.Extensions["errors"] = errors
            .Select(e => new IngestionErrorDto(e.Code, e.Message, e.Row, e.Column))
            .ToArray();

        return Results.Problem(problem);
    }

    private static IResult Problem(string typeSlug, string title, int status, string detail)
        => Results.Problem(type: ErrorBase + typeSlug, title: title, statusCode: status, detail: detail);
}
