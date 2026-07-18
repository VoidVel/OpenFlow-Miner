namespace OpenFlowMiner.Api.Contracts;

/// <summary>Response for a freshly generated API key. The key itself is shown only once.</summary>
public sealed record ApiKeyResponse(
    string Key,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    string Header,
    RateLimitTierDto RateLimit,
    string Notice);

/// <summary>The rate-limit tier a key grants.</summary>
public sealed record RateLimitTierDto(int PermitLimit, int WindowSeconds);
