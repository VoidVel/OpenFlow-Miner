using System.Globalization;
using OpenFlowMiner.Core.Domain;
using Sylvan.Data.Csv;

namespace OpenFlowMiner.Ingestion;

/// <summary>
/// Parses a CSV event log into <see cref="Event"/>s, validating each row and reporting every
/// problem specifically and by row (FR-1, FR-2, NFR-3). Validation is per-row and independent:
/// a well-formed row is accepted even if a sibling row in the same case is broken — the broken
/// sibling is still reported, so nothing is dropped silently.
/// </summary>
public sealed class CsvEventLogReader
{
    private static readonly DateTimeStyles TimestampStyles =
        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal;

    public IngestionResult Read(Stream stream, CsvColumnMapping? mapping = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var reader = new StreamReader(stream);
        return Read(reader, mapping);
    }

    public IngestionResult Read(TextReader textReader, CsvColumnMapping? mapping = null)
    {
        ArgumentNullException.ThrowIfNull(textReader);
        mapping ??= CsvColumnMapping.Default;

        CsvDataReader csv;
        try
        {
            csv = CsvDataReader.Create(textReader);
        }
        catch (CsvFormatException ex)
        {
            return IngestionResult.Failure([new IngestionError(IngestionErrorCode.EmptyFile, $"The CSV could not be read: {ex.Message}")]);
        }

        using (csv)
        {
            if (csv.FieldCount == 0)
            {
                return IngestionResult.Failure([new IngestionError(IngestionErrorCode.EmptyFile, "The file has no header row.")]);
            }

            if (!TryResolveColumns(csv, mapping, out var columns, out var columnErrors))
            {
                return IngestionResult.Failure(columnErrors);
            }

            return ReadRows(csv, mapping, columns);
        }
    }

    private static IngestionResult ReadRows(CsvDataReader csv, CsvColumnMapping mapping, ResolvedColumns columns)
    {
        var events = new List<Event>();
        var errors = new List<IngestionError>();
        var row = 0;

        while (csv.Read())
        {
            row++;
            try
            {
                var caseId = csv.GetString(columns.CaseId).Trim();
                var activity = csv.GetString(columns.Activity).Trim();
                var timestampRaw = csv.GetString(columns.Timestamp).Trim();

                var rowErrorCountBefore = errors.Count;

                if (caseId.Length == 0)
                {
                    errors.Add(new IngestionError(IngestionErrorCode.MissingCaseId, "Case ID is empty.", row, mapping.CaseId));
                }

                if (activity.Length == 0)
                {
                    errors.Add(new IngestionError(IngestionErrorCode.MissingActivity, "Activity is empty.", row, mapping.Activity));
                }

                DateTimeOffset timestamp = default;
                if (timestampRaw.Length == 0)
                {
                    errors.Add(new IngestionError(IngestionErrorCode.MissingTimestamp, "Timestamp is empty.", row, mapping.Timestamp));
                }
                else if (!TryParseTimestamp(timestampRaw, mapping, out timestamp))
                {
                    errors.Add(new IngestionError(
                        IngestionErrorCode.UnparseableTimestamp,
                        $"Timestamp '{timestampRaw}' is not a valid date/time.",
                        row,
                        mapping.Timestamp));
                }

                if (errors.Count == rowErrorCountBefore)
                {
                    var resource = columns.Resource is { } r ? NullIfEmpty(csv.GetString(r)) : null;
                    events.Add(new Event(caseId, activity, timestamp, resource));
                }
            }
            catch (Exception ex) when (ex is FormatException or InvalidOperationException or IndexOutOfRangeException)
            {
                // A structurally broken row (e.g. too few columns) — report it, never crash.
                errors.Add(new IngestionError(IngestionErrorCode.MalformedRow, $"Row could not be parsed: {ex.Message}", row));
            }
        }

        if (row == 0)
        {
            return IngestionResult.Failure([new IngestionError(IngestionErrorCode.EmptyFile, "The file has a header but no data rows.")]);
        }

        return errors.Count == 0 ? IngestionResult.Success(events) : IngestionResult.Failure(errors);
    }

    private static bool TryResolveColumns(
        CsvDataReader csv,
        CsvColumnMapping mapping,
        out ResolvedColumns columns,
        out IReadOnlyList<IngestionError> errors)
    {
        var header = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < csv.FieldCount; i++)
        {
            header[csv.GetName(i).Trim()] = i;
        }

        var problems = new List<IngestionError>();

        var caseId = Resolve(header, mapping.CaseId, problems);
        var activity = Resolve(header, mapping.Activity, problems);
        var timestamp = Resolve(header, mapping.Timestamp, problems);

        int? resource = null;
        if (mapping.Resource is { } resourceName)
        {
            resource = Resolve(header, resourceName, problems);
        }

        if (problems.Count > 0)
        {
            columns = default;
            errors = problems;
            return false;
        }

        columns = new ResolvedColumns(caseId!.Value, activity!.Value, timestamp!.Value, resource);
        errors = [];
        return true;

        static int? Resolve(Dictionary<string, int> header, string name, List<IngestionError> problems)
        {
            if (header.TryGetValue(name, out var ordinal))
            {
                return ordinal;
            }

            problems.Add(new IngestionError(
                IngestionErrorCode.MissingColumn,
                $"Required column '{name}' was not found in the header.",
                Row: 0,
                Column: name));
            return null;
        }
    }

    private static bool TryParseTimestamp(string value, CsvColumnMapping mapping, out DateTimeOffset timestamp)
    {
        if (mapping.TimestampFormats is { Count: > 0 } formats)
        {
            return DateTimeOffset.TryParseExact(value, [.. formats], CultureInfo.InvariantCulture, TimestampStyles, out timestamp);
        }

        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, TimestampStyles, out timestamp);
    }

    private static string? NullIfEmpty(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private readonly record struct ResolvedColumns(int CaseId, int Activity, int Timestamp, int? Resource);
}
