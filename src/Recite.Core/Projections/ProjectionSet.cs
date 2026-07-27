using System.Text.Json;
using System.Text.Json.Serialization;

namespace Recite.Core.Projections;

/// <summary>Maps a canonical name to its output abbreviation (e.g. journal → "IEEE Trans.").</summary>
public sealed class AbbreviationRule
{
    public string Match { get; set; } = "";
    public string Abbreviation { get; set; } = "";
}

/// <summary>
/// A find/replace applied to output fields. <see cref="Field"/> = "*" applies to all
/// string fields; otherwise to a single CSL field (e.g. "title", "container-title").
/// </summary>
public sealed class StringSubstitution
{
    public string Field { get; set; } = "*";
    public string Pattern { get; set; } = "";
    public string Replacement { get; set; } = "";
    public bool IsRegex { get; set; }
    public bool CaseSensitive { get; set; } = true;
}

/// <summary>
/// A project's stack of output transforms (PLAN §3). Applied in a fixed, documented order
/// by <see cref="ProjectionEngine"/> so <c>render(data, projections)</c> is deterministic:
/// abbreviations → substitutions → per-item overrides (overrides always win).
/// </summary>
public sealed class ProjectionSet
{
    public List<AbbreviationRule> JournalAbbreviations { get; set; } = new();
    public List<AbbreviationRule> SeriesAbbreviations { get; set; } = new();
    public List<StringSubstitution> Substitutions { get; set; } = new();

    /// <summary>Per-item field overrides, keyed by item UUID → (field → value).</summary>
    public Dictionary<string, Dictionary<string, string>> ItemOverrides { get; set; } = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static ProjectionSet FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new ProjectionSet();
        try { return JsonSerializer.Deserialize<ProjectionSet>(json, JsonOptions) ?? new ProjectionSet(); }
        catch { return new ProjectionSet(); }
    }
}
