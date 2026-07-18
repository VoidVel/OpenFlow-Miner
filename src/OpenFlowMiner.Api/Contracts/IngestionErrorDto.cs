namespace OpenFlowMiner.Api.Contracts;

/// <summary>Serialized form of a per-row ingestion problem, carried in a 422 problem+json "errors" array.</summary>
public sealed record IngestionErrorDto(string Code, string Message, int Row, string? Column);
