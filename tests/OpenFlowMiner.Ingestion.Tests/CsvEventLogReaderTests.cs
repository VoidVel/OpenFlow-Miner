using OpenFlowMiner.Ingestion;

namespace OpenFlowMiner.Ingestion.Tests;

public class CsvEventLogReaderTests
{
    private static IngestionResult ReadCsv(string content)
    {
        using var reader = new StringReader(content);
        return new CsvEventLogReader().Read(reader);
    }

    private const string ValidHeader = "case_id,activity,timestamp";

    [Fact]
    public void Read_ValidCsv_ProducesEvents()
    {
        var result = ReadCsv($"""
            {ValidHeader}
            c1,Start,2026-01-01T09:00:00Z
            c1,End,2026-01-01T10:00:00Z
            """);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Errors);
        Assert.Equal(2, result.Events.Count);
        Assert.Equal("c1", result.Events[0].CaseId);
        Assert.Equal("Start", result.Events[0].Activity);
    }

    [Fact]
    public void Read_MissingCaseId_ReportsRowAndCode()
    {
        var result = ReadCsv($"""
            {ValidHeader}
            ,Start,2026-01-01T09:00:00Z
            """);

        var error = Assert.Single(result.Errors);
        Assert.Equal(IngestionErrorCode.MissingCaseId, error.Code);
        Assert.Equal(1, error.Row);
    }

    [Fact]
    public void Read_MissingTimestamp_ReportsRowAndCode()
    {
        var result = ReadCsv($"""
            {ValidHeader}
            c1,Start,
            """);

        var error = Assert.Single(result.Errors);
        Assert.Equal(IngestionErrorCode.MissingTimestamp, error.Code);
        Assert.Equal(1, error.Row);
    }

    [Fact]
    public void Read_UnparseableTimestamp_ReportsRowAndCode()
    {
        var result = ReadCsv($"""
            {ValidHeader}
            c1,Start,2026-13-99T09:00:00Z
            """);

        var error = Assert.Single(result.Errors);
        Assert.Equal(IngestionErrorCode.UnparseableTimestamp, error.Code);
        Assert.Equal(1, error.Row);
    }

    [Fact]
    public void Read_MissingRequiredColumn_ReportsFileLevelError()
    {
        var result = ReadCsv("""
            id,activity,timestamp
            c1,Start,2026-01-01T09:00:00Z
            """);

        var error = Assert.Single(result.Errors);
        Assert.Equal(IngestionErrorCode.MissingColumn, error.Code);
        Assert.Equal(0, error.Row); // file-level
        Assert.Equal("case_id", error.Column);
    }

    [Fact]
    public void Read_CustomColumnMapping_IsRespected()
    {
        using var reader = new StringReader("""
            ticket,step,when
            t1,Opened,2026-01-01T09:00:00Z
            """);

        var mapping = new CsvColumnMapping(CaseId: "ticket", Activity: "step", Timestamp: "when");
        var result = new CsvEventLogReader().Read(reader, mapping);

        Assert.True(result.IsSuccess);
        Assert.Equal("t1", result.Events[0].CaseId);
        Assert.Equal("Opened", result.Events[0].Activity);
    }

    [Fact]
    public void Read_ColumnOrderIndependent()
    {
        // timestamp first, case_id last — order must not matter, only header names.
        var result = ReadCsv("""
            timestamp,activity,case_id
            2026-01-01T09:00:00Z,Start,c1
            """);

        Assert.True(result.IsSuccess);
        Assert.Equal("c1", result.Events[0].CaseId);
    }

    [Fact]
    public void Read_HeaderOnly_IsEmptyFileError()
    {
        var result = ReadCsv(ValidHeader);

        var error = Assert.Single(result.Errors);
        Assert.Equal(IngestionErrorCode.EmptyFile, error.Code);
    }

    [Fact]
    public void Read_IsAtomic_ValidRowsDiscardedWhenAnyRowFails()
    {
        var result = ReadCsv($"""
            {ValidHeader}
            c1,Start,2026-01-01T09:00:00Z
            c1,End,not-a-date
            """);

        Assert.False(result.IsSuccess);
        Assert.Empty(result.Events); // nothing partially ingested
        Assert.Single(result.Errors);
    }

    [Fact]
    public void Read_OptionalResourceColumn_IsCaptured()
    {
        using var reader = new StringReader("""
            case_id,activity,timestamp,who
            c1,Start,2026-01-01T09:00:00Z,alice
            """);

        var mapping = CsvColumnMapping.Default with { Resource = "who" };
        var result = new CsvEventLogReader().Read(reader, mapping);

        Assert.True(result.IsSuccess);
        Assert.Equal("alice", result.Events[0].Resource);
    }
}
