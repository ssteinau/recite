using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Recite.Core.Csl;

namespace Recite.Core.Projections;

/// <summary>
/// The output transform: <c>render(canonicalData, projections)</c>. A pure function that
/// never mutates its input — it clones, transforms the clone, and returns it. This is the
/// integrity/repeatability guarantee (PLAN §3) and is directly unit-testable for determinism.
///
/// Fixed application order: (1) journal/series abbreviations, (2) string substitutions,
/// (3) per-item field overrides — overrides always have the final say.
/// </summary>
public static class ProjectionEngine
{
    public static CslDocument Project(CslDocument input, ProjectionSet set, Guid? itemUuid = null)
    {
        var doc = input.Clone();

        // 1. Abbreviations on container-title (journal) and collection-title (series).
        if (CslMapper.IsJournalType(doc.Type) && doc.ContainerTitle is { } ct)
            doc.ContainerTitle = ApplyAbbrev(ct, set.JournalAbbreviations);
        if (doc.CollectionTitle is { } cs)
            doc.CollectionTitle = ApplyAbbrev(cs, set.SeriesAbbreviations);

        // 2. String substitutions.
        foreach (var sub in set.Substitutions)
            ApplySubstitution(doc, sub);

        // 3. Per-item field overrides (highest precedence).
        if (itemUuid is Guid g &&
            set.ItemOverrides.TryGetValue(g.ToString(), out var overrides))
        {
            foreach (var (field, value) in overrides)
            {
                if (string.IsNullOrEmpty(value)) doc.Root.Remove(field);
                else doc.SetString(field, value);
            }
        }

        return doc;
    }

    private static string ApplyAbbrev(string value, IEnumerable<AbbreviationRule> rules)
    {
        var folded = TextNormalizer.Fold(value);
        foreach (var rule in rules)
            if (TextNormalizer.Fold(rule.Match) == folded && !string.IsNullOrEmpty(rule.Abbreviation))
                return rule.Abbreviation;
        return value;
    }

    private static void ApplySubstitution(CslDocument doc, StringSubstitution sub)
    {
        if (string.IsNullOrEmpty(sub.Pattern)) return;

        string Transform(string input)
        {
            if (sub.IsRegex)
            {
                var opts = RegexOptions.CultureInvariant |
                           (sub.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase);
                try { return Regex.Replace(input, sub.Pattern, sub.Replacement ?? "", opts); }
                catch (RegexParseException) { return input; }
            }
            var comparison = sub.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            return input.Replace(sub.Pattern, sub.Replacement ?? "", comparison);
        }

        if (sub.Field == "*")
        {
            foreach (var key in doc.Root.Select(kv => kv.Key).ToList())
                if (doc.Root[key] is JsonValue v && v.TryGetValue<string>(out var s))
                    doc.Root[key] = Transform(s);
        }
        else if (doc.GetString(sub.Field) is { } current)
        {
            doc.SetString(sub.Field, Transform(current));
        }
    }
}
