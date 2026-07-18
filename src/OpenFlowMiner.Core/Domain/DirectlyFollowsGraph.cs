namespace OpenFlowMiner.Core.Domain;

/// <summary>An activity node in a mined graph, with how often it occurred.</summary>
public sealed record ActivityNode(string Activity, int Frequency);

/// <summary>A directed, frequency-weighted "directly-follows" edge: <paramref name="From"/> → <paramref name="To"/>.</summary>
public sealed record DfEdge(string From, string To, int Frequency);

/// <summary>
/// The Directly-Follows Graph (DFG): the core mined artifact. Nodes are activities;
/// an edge A→B (weighted by frequency) exists if B was ever observed immediately after A
/// within a case.
/// </summary>
public sealed record DirectlyFollowsGraph(
    IReadOnlyList<ActivityNode> Nodes,
    IReadOnlyList<DfEdge> Edges,
    IReadOnlyList<string> StartActivities,
    IReadOnlyList<string> EndActivities);
