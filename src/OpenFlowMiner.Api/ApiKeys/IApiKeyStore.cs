namespace OpenFlowMiner.Api.ApiKeys;

/// <summary>
/// Storage abstraction for API keys, mirroring the shape of the event-log store: an in-memory,
/// TTL-able default with no vendor lock-in. Intentionally minimal — it can create a key and check
/// whether one is currently valid. There is deliberately no "list", "get by owner", or "delete"
/// operation: keys are unmanageable and unenumerable by design.
/// </summary>
public interface IApiKeyStore
{
    /// <summary>Issues a new opaque key. A <paramref name="ttl"/> of <c>null</c> means it never expires.</summary>
    Task<ApiKey> CreateAsync(TimeSpan? ttl = null, CancellationToken cancellationToken = default);

    /// <summary>Whether a non-expired key with this value exists.</summary>
    Task<bool> IsValidAsync(string key, CancellationToken cancellationToken = default);
}
