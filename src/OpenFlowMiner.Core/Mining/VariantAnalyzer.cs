using OpenFlowMiner.Core.Domain;

namespace OpenFlowMiner.Core.Mining;

/// <summary>
/// Ranks process variants (FR-6): the distinct end-to-end activity sequences cases actually
/// followed, grouped by identical sequence and ordered by how common each is.
/// </summary>
public static class VariantAnalyzer
{
    /// <param name="top">If given, return only the <paramref name="top"/> most frequent variants.</param>
    public static IReadOnlyList<Variant> Analyze(EventLog log, int? top = null)
    {
        ArgumentNullException.ThrowIfNull(log);
        if (top is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(top), "top must be non-negative.");
        }

        var totalCases = log.Traces.Count;
        if (totalCases == 0)
        {
            return [];
        }

        IEnumerable<(IReadOnlyList<string> Sequence, int Count)> ranked = log.Traces
            .GroupBy(t => t.ActivitySequence, SequenceComparer.Instance)
            .Select(g => (Sequence: g.Key, Count: g.Count()))
            .OrderByDescending(x => x.Count)
            // Deterministic lexicographic tie-break so equal-frequency variants keep a stable order.
            .ThenBy(x => x.Sequence, SequenceOrder.Instance);

        if (top is { } n)
        {
            ranked = ranked.Take(n);
        }

        return ranked
            .Select((x, i) => new Variant(
                Rank: i + 1,
                Sequence: x.Sequence,
                CaseCount: x.Count,
                Percentage: 100.0 * x.Count / totalCases))
            .ToArray();
    }

    /// <summary>Structural (element-by-element, ordinal) equality for activity sequences.</summary>
    private sealed class SequenceComparer : IEqualityComparer<IReadOnlyList<string>>
    {
        public static readonly SequenceComparer Instance = new();

        public bool Equals(IReadOnlyList<string>? x, IReadOnlyList<string>? y)
        {
            if (ReferenceEquals(x, y))
            {
                return true;
            }

            if (x is null || y is null || x.Count != y.Count)
            {
                return false;
            }

            for (var i = 0; i < x.Count; i++)
            {
                if (!string.Equals(x[i], y[i], StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        public int GetHashCode(IReadOnlyList<string> obj)
        {
            var hash = new HashCode();
            foreach (var activity in obj)
            {
                hash.Add(activity, StringComparer.Ordinal);
            }

            return hash.ToHashCode();
        }
    }

    /// <summary>Lexicographic ordinal ordering over activity sequences, for deterministic tie-breaks.</summary>
    private sealed class SequenceOrder : IComparer<IReadOnlyList<string>>
    {
        public static readonly SequenceOrder Instance = new();

        public int Compare(IReadOnlyList<string>? x, IReadOnlyList<string>? y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            if (x is null)
            {
                return -1;
            }

            if (y is null)
            {
                return 1;
            }

            var shared = Math.Min(x.Count, y.Count);
            for (var i = 0; i < shared; i++)
            {
                var c = string.CompareOrdinal(x[i], y[i]);
                if (c != 0)
                {
                    return c;
                }
            }

            return x.Count.CompareTo(y.Count);
        }
    }
}
