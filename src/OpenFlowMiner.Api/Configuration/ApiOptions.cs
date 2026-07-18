namespace OpenFlowMiner.Api.Configuration;

/// <summary>Ingestion limits (bound from the "Ingestion" configuration section).</summary>
public sealed class IngestionOptions
{
    /// <summary>
    /// The maximum number of events a single upload may contain. Above this the API returns
    /// 413. The default (1,000,000) is the measured, responsive ceiling — see docs/benchmarks.md.
    /// </summary>
    public int MaxEvents { get; set; } = 1_000_000;
}

/// <summary>Storage/retention settings (bound from the "Storage" configuration section).</summary>
public sealed class StorageOptions
{
    /// <summary>How long an uploaded log is retained before it expires. The pinned demo log never expires.</summary>
    public int UploadTtlHours { get; set; } = 24;
}

/// <summary>
/// Rate-limit tiers (bound from the "RateLimiting" section). Anonymous, keyless clients get the
/// anonymous tier per IP; clients presenting a valid X-Api-Key get the higher keyed tier.
/// </summary>
public sealed class RateLimitingOptions
{
    public int AnonymousPermitLimit { get; set; } = 100;
    public int KeyedPermitLimit { get; set; } = 1000;
    public int WindowSeconds { get; set; } = 60;
}

/// <summary>API-key settings (bound from the "ApiKeys" section).</summary>
public sealed class ApiKeyGenerationOptions
{
    /// <summary>Lifetime of a generated key in days; <c>0</c> or less means the key never expires.</summary>
    public int LifetimeDays { get; set; } = 30;
}
