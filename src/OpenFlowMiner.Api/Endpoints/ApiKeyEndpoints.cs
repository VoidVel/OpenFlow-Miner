using Microsoft.Extensions.Options;
using OpenFlowMiner.Api.ApiKeys;
using OpenFlowMiner.Api.Configuration;
using OpenFlowMiner.Api.Contracts;

namespace OpenFlowMiner.Api.Endpoints;

/// <summary>
/// Self-serve API-key generation. This is <b>not</b> an account system: there is no signup, login,
/// email, or profile — a POST returns an opaque key immediately, used only to raise the caller's
/// rate-limit tier. Anonymous, keyless access to every other endpoint keeps working unchanged.
/// </summary>
public static class ApiKeyEndpoints
{
    public static RouteGroupBuilder MapApiKeyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/api-keys").WithTags("API Keys");

        group.MapPost("/", CreateAsync)
            .WithSummary("Generate an API key")
            .WithDescription(
                "Returns a new opaque API key immediately — no signup, email, or account. Send it in the " +
                "'X-Api-Key' header to get a higher rate limit. The key is shown only once and cannot be " +
                "retrieved, listed, or managed afterwards. Keyless access continues to work at the anonymous tier.")
            .Produces<ApiKeyResponse>(StatusCodes.Status201Created);

        return group;
    }

    private static async Task<IResult> CreateAsync(
        IApiKeyStore store,
        IOptions<ApiKeyGenerationOptions> keyOptions,
        IOptions<RateLimitingOptions> rateLimiting,
        CancellationToken cancellationToken)
    {
        var lifetimeDays = keyOptions.Value.LifetimeDays;
        var ttl = lifetimeDays > 0 ? TimeSpan.FromDays(lifetimeDays) : (TimeSpan?)null;

        var apiKey = await store.CreateAsync(ttl, cancellationToken);

        var response = new ApiKeyResponse(
            Key: apiKey.Key,
            CreatedAt: apiKey.CreatedAt,
            ExpiresAt: apiKey.ExpiresAt,
            Header: "X-Api-Key",
            RateLimit: new RateLimitTierDto(rateLimiting.Value.KeyedPermitLimit, rateLimiting.Value.WindowSeconds),
            Notice: "Store this key now — it is shown only once and cannot be retrieved, listed, or managed later.");

        // 201 without a Location header: a key is created but is intentionally not addressable/retrievable.
        return Results.Json(response, statusCode: StatusCodes.Status201Created);
    }
}
