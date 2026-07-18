using OpenFlowMiner.Core.Domain;

namespace OpenFlowMiner.Ingestion;

/// <summary>
/// The outcome of parsing a CSV. Ingestion is <b>atomic</b>: either every row is valid and
/// <see cref="Events"/> holds the whole log, or <see cref="Errors"/> lists every row-level
/// problem found and <see cref="Events"/> is empty. There is no partial, silently-trimmed load.
/// </summary>
/// <remarks>
/// Deliberately returns a flat <see cref="Event"/> list rather than an <see cref="EventLog"/>:
/// the log identity is assigned by the storage layer at save time, keeping ingestion free of
/// any storage concern.
/// </remarks>
public sealed class IngestionResult
{
    private IngestionResult(IReadOnlyList<Event> events, IReadOnlyList<IngestionError> errors)
    {
        Events = events;
        Errors = errors;
    }

    public IReadOnlyList<Event> Events { get; }

    public IReadOnlyList<IngestionError> Errors { get; }

    public bool IsSuccess => Errors.Count == 0;

    public static IngestionResult Success(IReadOnlyList<Event> events) => new(events, []);

    public static IngestionResult Failure(IReadOnlyList<IngestionError> errors) => new([], errors);
}
