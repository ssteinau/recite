using Recite.Core.Model;

namespace Recite.Core.Dedup;

public sealed record PersonMatch(Guid LeftUuid, Guid RightUuid, double Score, string Reason);

/// <summary>
/// Finds candidate duplicate persons for the reconciliation queue (PLAN §3, "merge
/// 'Hull, R.' into 'Hull, Richard'"). Blocks by family name, then flags pairs whose given
/// names are compatible: equal, one an initial-prefix of the other, or highly similar.
/// </summary>
public static class PersonDedup
{
    public sealed record Record(Guid Uuid, string? Family, string? Given, string? Literal)
    {
        public string FamilyKey { get; } = TextNormalizer.Fold((Family ?? "") + " " + (Literal ?? ""));
        public string GivenFold { get; } = TextNormalizer.Fold(Given ?? "");
    }

    public static Record ToRecord(Person p) => new(p.Uuid, p.Family, p.Given, p.Literal);

    public static IReadOnlyList<PersonMatch> Find(IReadOnlyList<Record> people)
    {
        var matches = new List<PersonMatch>();
        var blocks = people
            .Where(p => p.FamilyKey.Length > 0)
            .GroupBy(p => p.FamilyKey);

        foreach (var block in blocks)
        {
            var list = block.ToList();
            for (int a = 0; a < list.Count; a++)
                for (int b = a + 1; b < list.Count; b++)
                {
                    var (score, reason) = Compare(list[a], list[b]);
                    if (score > 0)
                        matches.Add(new PersonMatch(list[a].Uuid, list[b].Uuid, score, reason));
                }
        }

        return matches.OrderByDescending(m => m.Score).ToList();
    }

    private static (double, string) Compare(Record x, Record y)
    {
        var gx = x.GivenFold;
        var gy = y.GivenFold;
        if (gx.Length == 0 || gy.Length == 0)
            return (0.6, "same family, one given name missing");
        if (gx == gy)
            return (0, ""); // identical → already auto-merged by the unique key
        if (IsInitialOf(gx, gy) || IsInitialOf(gy, gx))
            return (0.95, "same family, given name vs initial");
        var sim = TextNormalizer.Similarity(gx, gy);
        if (sim >= 0.85)
            return (0.8, $"same family, similar given ({sim:0.00})");
        return (0, "");
    }

    /// <summary>True if <paramref name="initials"/> is an initial-abbreviation of <paramref name="full"/>.</summary>
    private static bool IsInitialOf(string initials, string full)
    {
        var initTokens = initials.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var fullTokens = full.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (initTokens.Length == 0 || initTokens.Length > fullTokens.Length) return false;
        // Every initial token must be a 1-char prefix of the matching full token, and shorter overall.
        if (!initTokens.All(t => t.Length == 1)) return false;
        for (int i = 0; i < initTokens.Length; i++)
            if (fullTokens[i][0] != initTokens[i][0]) return false;
        return true;
    }
}
