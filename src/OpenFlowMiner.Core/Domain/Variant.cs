namespace OpenFlowMiner.Core.Domain;

/// <summary>
/// A distinct end-to-end path that at least one case actually followed. Cases with an
/// identical activity sequence are grouped into the same variant and ranked by frequency.
/// </summary>
/// <param name="Rank">1-based rank, most frequent first.</param>
/// <param name="Sequence">The ordered activity sequence that defines this variant.</param>
/// <param name="CaseCount">How many cases followed this exact sequence.</param>
/// <param name="Percentage">Share of all cases that followed it, 0–100.</param>
public sealed record Variant(int Rank, IReadOnlyList<string> Sequence, int CaseCount, double Percentage);
