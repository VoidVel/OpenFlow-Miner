using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace OpenFlowMiner.Api.ApiKeys;

/// <summary>
/// Default in-memory API-key store. Keys are cryptographically random opaque tokens prefixed
/// <c>ofm_</c>. Expiry is enforced lazily on access. A per-key usage counter is tracked (handy for a
/// future dashboard) but is never exposed through any endpoint — there is no way to inspect another
/// key. Mirrors <see cref="OpenFlowMiner.Core.Storage.InMemoryEventLogStore"/> in spirit.
/// </summary>
public sealed class InMemoryApiKeyStore : IApiKeyStore
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly TimeProvider _clock;

    public InMemoryApiKeyStore(TimeProvider? clock = null) => _clock = clock ?? TimeProvider.System;

    public Task<ApiKey> CreateAsync(TimeSpan? ttl = null, CancellationToken cancellationToken = default)
    {
        var now = _clock.GetUtcNow();
        var apiKey = new ApiKey(GenerateKey(), now, ttl is { } span ? now + span : null);
        _entries[apiKey.Key] = new Entry(apiKey);
        return Task.FromResult(apiKey);
    }

    public Task<bool> IsValidAsync(string key, CancellationToken cancellationToken = default)
        => Task.FromResult(!string.IsNullOrEmpty(key) && _entries.TryGetValue(key, out var e) && !IsExpired(e.Key));

    /// <summary>
    /// Synchronous validate-and-count path for the rate limiter (whose partitioner is synchronous).
    /// Returns whether the key is valid and, if so, records one use. Expired keys are evicted.
    /// </summary>
    public bool TryUse(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return false;
        }

        if (_entries.TryGetValue(key, out var entry))
        {
            if (!IsExpired(entry.Key))
            {
                entry.IncrementUsage();
                return true;
            }

            _entries.TryRemove(new KeyValuePair<string, Entry>(key, entry));
        }

        return false;
    }

    /// <summary>Number of live (non-expired) keys.</summary>
    public int Count => _entries.Values.Count(e => !IsExpired(e.Key));

    private bool IsExpired(ApiKey key) => key.ExpiresAt is { } expiry && _clock.GetUtcNow() >= expiry;

    private static string GenerateKey()
    {
        // 32 bytes of CSPRNG entropy, base64url-encoded, with a recognizable product prefix.
        var bytes = RandomNumberGenerator.GetBytes(32);
        var token = Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
        return "ofm_" + token;
    }

    private sealed class Entry(ApiKey key)
    {
        private long _usage;

        public ApiKey Key { get; } = key;

        public long Usage => Interlocked.Read(ref _usage);

        public void IncrementUsage() => Interlocked.Increment(ref _usage);
    }
}
