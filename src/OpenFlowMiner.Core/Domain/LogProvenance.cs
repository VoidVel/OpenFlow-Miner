namespace OpenFlowMiner.Core.Domain;

/// <summary>
/// Where an event log came from. Kept for honest attribution — e.g. so the public demo
/// can say "this is mining a real hospital billing process" and credit the source dataset.
/// </summary>
/// <param name="Source">A human-readable origin label (e.g. an uploaded file name, or a dataset name).</param>
/// <param name="Attribution">Optional citation/credit line for a public dataset.</param>
public sealed record LogProvenance(string Source, string? Attribution = null)
{
    /// <summary>Provenance for an anonymous, in-memory or ad-hoc log with no meaningful origin.</summary>
    public static LogProvenance Unknown { get; } = new("unknown");
}
