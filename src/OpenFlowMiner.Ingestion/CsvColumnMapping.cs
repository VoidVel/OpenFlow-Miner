namespace OpenFlowMiner.Ingestion;

/// <summary>
/// Maps the columns of an incoming CSV to the three required event fields (plus optional
/// resource). Column names are matched case-insensitively against the header, so callers are
/// never forced into a fixed column order or fixed names (FR-1).
/// </summary>
/// <param name="CaseId">Header name of the case-identifier column.</param>
/// <param name="Activity">Header name of the activity/step column.</param>
/// <param name="Timestamp">Header name of the timestamp column.</param>
/// <param name="Resource">Optional header name of a resource/actor column (FR-14).</param>
/// <param name="TimestampFormats">
/// Optional explicit formats (tried in order via exact parsing). When omitted, timestamps are
/// parsed with the general invariant-culture parser, which handles ISO 8601 (e.g. 2026-01-01T09:00:00Z).
/// </param>
public sealed record CsvColumnMapping(
    string CaseId = "case_id",
    string Activity = "activity",
    string Timestamp = "timestamp",
    string? Resource = null,
    IReadOnlyList<string>? TimestampFormats = null)
{
    /// <summary>The conventional mapping: <c>case_id</c>, <c>activity</c>, <c>timestamp</c>.</summary>
    public static CsvColumnMapping Default { get; } = new();
}
