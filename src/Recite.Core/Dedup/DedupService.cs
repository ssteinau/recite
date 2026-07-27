namespace Recite.Core.Dedup;

public enum DupConfidence { Exact, Strong, Possible }

/// <summary>A detected candidate-duplicate pair, for the human review queue (PLAN §3).</summary>
public sealed record DuplicatePair(
    Guid LeftUuid,
    Guid RightUuid,
    double Score,
    DupConfidence Confidence,
    string Reason);

/// <summary>
/// Duplicate detection. Exact matches key on shared external identifiers; fuzzy matches
/// key on normalized title + year + type + author overlap, blocked to avoid O(n²).
/// Deliberately conservative on title-only matches: identical titles across different
/// years are NOT duplicates (PLAN §3 — "Business Process Management" ×4).
/// </summary>
public sealed class DedupService
{
    public double StrongThreshold { get; init; } = 0.90;
    public double PossibleThreshold { get; init; } = 0.80;

    /// <summary>Find all candidate pairs among the records, most confident first.</summary>
    public IReadOnlyList<DuplicatePair> FindDuplicates(IReadOnlyList<DedupRecord> records)
    {
        var pairs = new Dictionary<(Guid, Guid), DuplicatePair>();

        // 1. Exact: shared (scheme, value) identifier.
        var byId = new Dictionary<(string, string), List<DedupRecord>>();
        foreach (var r in records)
            foreach (var id in r.Identifiers)
            {
                if (string.IsNullOrWhiteSpace(id.Value)) continue;
                var key = (id.Scheme, id.Value);
                (byId.TryGetValue(key, out var list) ? list : byId[key] = new()).Add(r);
            }
        foreach (var (key, list) in byId)
            for (int a = 0; a < list.Count; a++)
                for (int b = a + 1; b < list.Count; b++)
                    Add(pairs, new DuplicatePair(list[a].Uuid, list[b].Uuid, 1.0,
                        DupConfidence.Exact, $"shared {key.Item1} {key.Item2}"));

        // 2. Fuzzy: block by first-significant-word + year + type, compare within blocks.
        var blocks = new Dictionary<string, List<DedupRecord>>();
        foreach (var r in records)
            (blocks.TryGetValue(r.BlockKey, out var l) ? l : blocks[r.BlockKey] = new()).Add(r);

        foreach (var block in blocks.Values)
        {
            for (int a = 0; a < block.Count; a++)
                for (int b = a + 1; b < block.Count; b++)
                {
                    var (score, reason) = ScorePair(block[a], block[b]);
                    if (score >= PossibleThreshold)
                    {
                        var conf = score >= StrongThreshold ? DupConfidence.Strong : DupConfidence.Possible;
                        Add(pairs, new DuplicatePair(block[a].Uuid, block[b].Uuid, score, conf, reason));
                    }
                }
        }

        return pairs.Values
            .OrderByDescending(p => (int) Rank(p.Confidence))
            .ThenByDescending(p => p.Score)
            .ToList();
    }

    private static int Rank(DupConfidence c) => c switch
    {
        DupConfidence.Exact => 3, DupConfidence.Strong => 2, _ => 1
    };

    private static void Add(Dictionary<(Guid, Guid), DuplicatePair> pairs, DuplicatePair p)
    {
        var key = p.LeftUuid.CompareTo(p.RightUuid) <= 0 ? (p.LeftUuid, p.RightUuid) : (p.RightUuid, p.LeftUuid);
        // Keep the strongest signal for a pair.
        if (!pairs.TryGetValue(key, out var existing) || Rank(p.Confidence) > Rank(existing.Confidence)
            || (Rank(p.Confidence) == Rank(existing.Confidence) && p.Score > existing.Score))
            pairs[key] = p with { LeftUuid = key.Item1, RightUuid = key.Item2 };
    }

    private (double score, string reason) ScorePair(DedupRecord x, DedupRecord y)
    {
        // Type must match; different types are never fuzzy dups.
        if (!string.Equals(x.Type, y.Type, StringComparison.OrdinalIgnoreCase))
            return (0, "");

        // Year must match when both present (guards the "same title, different year" trap).
        if (x.Year is not null && y.Year is not null && x.Year != y.Year)
            return (0, "");

        double titleSim = TextNormalizer.Similarity(x.NormalizedTitle, y.NormalizedTitle);
        if (titleSim < 0.6) return (0, "");

        double authorOverlap = Overlap(x.AuthorKeys, y.AuthorKeys);
        double yearScore = (x.Year is not null && y.Year == x.Year) ? 1.0
            : (x.Year is null || y.Year is null) ? 0.5 : 0.0;

        double score = 0.60 * titleSim + 0.25 * authorOverlap + 0.15 * yearScore;
        var reason = $"title {titleSim:0.00}, authors {authorOverlap:0.00}";
        return (score, reason);
    }

    private static double Overlap(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        if (a.Count == 0 && b.Count == 0) return 0.5;
        if (a.Count == 0 || b.Count == 0) return 0.0;
        var sa = a.ToHashSet();
        var sb = b.ToHashSet();
        int inter = sa.Count(sb.Contains);
        return (double)inter / Math.Max(sa.Count, sb.Count);
    }
}
