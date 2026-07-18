using System.Collections.Concurrent;
using OpenFlowMiner.Core.Domain;

namespace OpenFlowMiner.Core.Storage;

/// <summary>
/// The default zero-configuration store: logs live in memory, optionally with a TTL. Suitable
/// for self-hosting and for protecting a public demo (uploads expire; a pinned demo log does not).
/// Expiry is enforced lazily on access and can also be swept proactively via <see cref="RemoveExpired"/>.
/// </summary>
public sealed class InMemoryEventLogStore : IEventLogStore
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly TimeProvider _clock;

    public InMemoryEventLogStore(TimeProvider? clock = null) => _clock = clock ?? TimeProvider.System;

    public Task<string> SaveAsync(EventLog log, TimeSpan? ttl = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(log);

        var expiresAt = ttl is { } span ? _clock.GetUtcNow() + span : (DateTimeOffset?)null;
        _entries[log.Id] = new Entry(log, expiresAt);
        return Task.FromResult(log.Id);
    }

    public Task<EventLog?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        if (_entries.TryGetValue(id, out var entry))
        {
            if (!IsExpired(entry))
            {
                return Task.FromResult<EventLog?>(entry.Log);
            }

            // Expired: drop it so memory is reclaimed even without a sweep.
            _entries.TryRemove(new KeyValuePair<string, Entry>(id, entry));
        }

        return Task.FromResult<EventLog?>(null);
    }

    public Task<bool> ExistsAsync(string id, CancellationToken cancellationToken = default)
        => Task.FromResult(_entries.TryGetValue(id, out var entry) && !IsExpired(entry));

    /// <summary>Removes all expired entries. Returns how many were removed. Intended for a periodic sweeper.</summary>
    public int RemoveExpired()
    {
        var removed = 0;
        foreach (var pair in _entries)
        {
            if (IsExpired(pair.Value) && _entries.TryRemove(pair))
            {
                removed++;
            }
        }

        return removed;
    }

    /// <summary>Current number of live (non-expired) entries.</summary>
    public int Count => _entries.Values.Count(e => !IsExpired(e));

    private bool IsExpired(Entry entry) => entry.ExpiresAt is { } expiry && _clock.GetUtcNow() >= expiry;

    private readonly record struct Entry(EventLog Log, DateTimeOffset? ExpiresAt);
}
