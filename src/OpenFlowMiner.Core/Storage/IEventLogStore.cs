using OpenFlowMiner.Core.Domain;

namespace OpenFlowMiner.Core.Storage;

/// <summary>
/// Persistence abstraction for ingested logs. The public contract assumes no particular
/// database or cloud provider (NFR-4): the default implementation is in-memory, but a durable
/// adapter can be dropped in without touching the API surface.
/// </summary>
public interface IEventLogStore
{
    /// <summary>
    /// Stores a log. A <paramref name="ttl"/> of <c>null</c> pins it permanently (used for the
    /// demo dataset); otherwise it expires after the given duration.
    /// </summary>
    Task<string> SaveAsync(EventLog log, TimeSpan? ttl = null, CancellationToken cancellationToken = default);

    /// <summary>Returns the log, or <c>null</c> if it is unknown or has expired.</summary>
    Task<EventLog?> GetAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Whether a non-expired log with this id is present.</summary>
    Task<bool> ExistsAsync(string id, CancellationToken cancellationToken = default);
}
