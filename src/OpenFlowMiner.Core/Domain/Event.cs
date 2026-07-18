namespace OpenFlowMiner.Core.Domain;

/// <summary>
/// The atom of a process log: one recorded occurrence within a case.
/// </summary>
/// <remarks>
/// This type is deliberately domain-agnostic. It carries only the three things every
/// process has — which case, what happened, and when — plus two generic, optional
/// extension points (<see cref="Resource"/> and <see cref="Attributes"/>).
/// There is intentionally no way to express an industry concept such as an order status
/// or a claim type here; those belong in a downstream consumer, never in this model.
/// A guard test asserts this property set never grows industry-specific fields.
/// </remarks>
public sealed record Event(
    string CaseId,
    string Activity,
    DateTimeOffset Timestamp,
    string? Resource = null,
    IReadOnlyDictionary<string, string>? Attributes = null);
