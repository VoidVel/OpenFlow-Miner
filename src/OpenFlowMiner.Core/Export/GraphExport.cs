using System.Text;
using OpenFlowMiner.Core.Domain;

namespace OpenFlowMiner.Core.Export;

/// <summary>
/// Renders a mined <see cref="DirectlyFollowsGraph"/> into shareable text formats. Mermaid output
/// can be pasted straight into GitHub/Notion to *see* the real process with no tooling; DOT feeds
/// Graphviz. JSON remains the canonical machine format — these are the zero-friction human formats.
/// </summary>
public static class GraphExport
{
    /// <summary>Renders the graph as a Mermaid <c>graph LR</c> flowchart.</summary>
    public static string ToMermaid(DirectlyFollowsGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var ids = AssignNodeIds(graph);
        var sb = new StringBuilder();
        sb.AppendLine("graph LR");

        foreach (var node in graph.Nodes)
        {
            sb.AppendLine($"    {ids[node.Activity]}[\"{EscapeMermaid(node.Activity)}\"]");
        }

        foreach (var edge in graph.Edges)
        {
            sb.AppendLine($"    {ids[edge.From]} -->|{edge.Frequency}| {ids[edge.To]}");
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>Renders the graph as Graphviz DOT.</summary>
    public static string ToDot(DirectlyFollowsGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var sb = new StringBuilder();
        sb.AppendLine("digraph process {");
        sb.AppendLine("    rankdir=LR;");
        sb.AppendLine("    node [shape=box];");

        foreach (var node in graph.Nodes)
        {
            sb.AppendLine($"    \"{EscapeDot(node.Activity)}\" [label=\"{EscapeDot(node.Activity)} ({node.Frequency})\"];");
        }

        foreach (var edge in graph.Edges)
        {
            sb.AppendLine($"    \"{EscapeDot(edge.From)}\" -> \"{EscapeDot(edge.To)}\" [label=\"{edge.Frequency}\"];");
        }

        sb.AppendLine("}");
        return sb.ToString().TrimEnd();
    }

    private static Dictionary<string, string> AssignNodeIds(DirectlyFollowsGraph graph)
    {
        var ids = new Dictionary<string, string>(StringComparer.Ordinal);
        var index = 0;
        foreach (var node in graph.Nodes)
        {
            ids[node.Activity] = $"n{index++}";
        }

        return ids;
    }

    private static string EscapeMermaid(string text)
        => text.Replace("\"", "&quot;", StringComparison.Ordinal);

    private static string EscapeDot(string text)
        => text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
}
