namespace OpenFlowMiner.Ingestion;

/// <summary>
/// A single, actionable ingestion problem. Deliberately structured (a machine-readable
/// <see cref="Code"/> plus a human message and location) so the API layer can render it as
/// RFC 9457 <c>problem+json</c> without loss — the FR-2 / NFR-3 promise of loud, specific errors.
/// </summary>
/// <param name="Code">Stable, machine-readable slug — see <see cref="IngestionErrorCode"/>.</param>
/// <param name="Message">A human-readable explanation of exactly what is wrong.</param>
/// <param name="Row">
/// 1-based event row the problem occurred on, counting data rows only (the header is not row 1).
/// <c>0</c> denotes a file-level problem (e.g. a missing column or an empty file).
/// </param>
/// <param name="Column">The offending column name, when the problem is tied to one.</param>
public sealed record IngestionError(string Code, string Message, int Row = 0, string? Column = null);

/// <summary>Stable slugs used for <see cref="IngestionError.Code"/>; these become problem+json error types.</summary>
public static class IngestionErrorCode
{
    public const string EmptyFile = "empty-file";
    public const string MissingColumn = "missing-column";
    public const string MissingCaseId = "missing-case-id";
    public const string MissingActivity = "missing-activity";
    public const string MissingTimestamp = "missing-timestamp";
    public const string UnparseableTimestamp = "unparseable-timestamp";
    public const string MalformedRow = "malformed-row";
}
