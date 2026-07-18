using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using OpenFlowMiner.Api;
using OpenFlowMiner.Api.ApiKeys;
using OpenFlowMiner.Api.Configuration;
using OpenFlowMiner.Api.Endpoints;
using OpenFlowMiner.Core.Storage;
using OpenFlowMiner.Ingestion;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Trust reverse proxy headers (for platforms like Render, Azure, etc.)
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// Options
builder.Services.Configure<IngestionOptions>(builder.Configuration.GetSection("Ingestion"));
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection("Storage"));
builder.Services.Configure<RateLimitingOptions>(builder.Configuration.GetSection("RateLimiting"));
builder.Services.Configure<ApiKeyGenerationOptions>(builder.Configuration.GetSection("ApiKeys"));

// Core services. The store is one instance exposed both concretely (for the sweeper) and via the
// interface (for endpoints) — no vendor lock-in in the public contract (NFR-4).
builder.Services.AddSingleton<InMemoryEventLogStore>();
builder.Services.AddSingleton<IEventLogStore>(sp => sp.GetRequiredService<InMemoryEventLogStore>());
builder.Services.AddSingleton<CsvEventLogReader>();
builder.Services.AddHostedService<EventLogStoreSweeper>();

// API-key store: same in-memory + interface pattern as the event-log store. Keys are for rate-limit
// tiering ONLY — no account, login, or profile is created anywhere.
builder.Services.AddSingleton<InMemoryApiKeyStore>();
builder.Services.AddSingleton<IApiKeyStore>(sp => sp.GetRequiredService<InMemoryApiKeyStore>());

// Consistent RFC 9457 problem+json for framework-generated errors too (NFR-3).
builder.Services.AddProblemDetails();

// OpenAPI is a first-class deliverable (NFR-1).
builder.Services.AddOpenApi();

// Tiered rate limiting (Open Q3). A valid X-Api-Key grants the higher keyed tier; everything else —
// no key, an unknown key, an expired key — falls through to the anonymous per-IP tier exactly as
// before. This is purely additive: keyless access is never gated.
var rateLimits = builder.Configuration.GetSection("RateLimiting").Get<RateLimitingOptions>() ?? new RateLimitingOptions();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var window = TimeSpan.FromSeconds(rateLimits.WindowSeconds);

        if (context.Request.Headers.TryGetValue("X-Api-Key", out var provided))
        {
            var key = provided.ToString();
            // Validate (and count usage) via the concrete store's synchronous path.
            if (context.RequestServices.GetRequiredService<InMemoryApiKeyStore>().TryUse(key))
            {
                return RateLimitPartition.GetFixedWindowLimiter(
                    "key:" + key,
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = rateLimits.KeyedPermitLimit, Window = window });
            }
        }

        return RateLimitPartition.GetFixedWindowLimiter(
            "ip:" + (context.Connection.RemoteIpAddress?.ToString() ?? "unknown"),
            _ => new FixedWindowRateLimiterOptions { PermitLimit = rateLimits.AnonymousPermitLimit, Window = window });
    });
});

var app = builder.Build();

// Process forwarded headers from reverse proxy
app.UseForwardedHeaders();

app.UseExceptionHandler();
app.UseStatusCodePages();

// Serve the static frontend (developer portal + reference client) BEFORE the rate limiter, so asset
// loads don't consume a client's request budget — only API calls are metered.
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseRateLimiter();

// OpenAPI document + Scalar interactive docs UI at /scalar.
app.MapOpenApi();
app.MapScalarApiReference(options => options.WithTitle("OpenFlow Miner API"));

app.MapEventLogEndpoints();
app.MapApiKeyEndpoints();

// Seed pinned demo logs (ttl: null → never expire) so the API is demonstrable immediately.
// The hero is a real published municipal process; a tiny sample sits alongside it.
var store = app.Services.GetRequiredService<IEventLogStore>();
var seedLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("DemoData");
var reader = app.Services.GetRequiredService<CsvEventLogReader>();

await store.SaveAsync(DemoData.BuildSampleLog(), ttl: null);

var receiptLog = DemoData.TryLoadReceiptLog(Path.Combine(AppContext.BaseDirectory, "demo-data"), reader, seedLogger);
if (receiptLog is not null)
{
    await store.SaveAsync(receiptLog, ttl: null);
    seedLogger.LogInformation("Seeded real demo log '{Id}': {Cases} cases, {Events} events.",
        receiptLog.Id, receiptLog.CaseCount, receiptLog.EventCount);
}

app.Run();

// Exposed so the integration-test project (WebApplicationFactory) can reference the entry point.
public partial class Program;
