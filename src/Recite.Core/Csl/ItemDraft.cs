using Recite.Core.Model;

namespace Recite.Core.Csl;

/// <summary>
/// The factored-out result of reading a <see cref="CslDocument"/>: everything the
/// library importer needs to reconstruct an <see cref="Item"/> plus its resolved
/// Person/Journal/Series relations. Produced by <see cref="CslMapper.ToDraft"/>.
/// </summary>
public sealed class ItemDraft
{
    public Guid? Uuid { get; set; }
    public string Type { get; set; } = CslType.Document;

    /// <summary>Own (non-factored) CSL fields, serialized.</summary>
    public string FieldsJson { get; set; } = "{}";

    public string? Title { get; set; }
    public int? Year { get; set; }

    public List<(ContributorRole Role, CslName Name)> Contributors { get; } = new();

    public string? JournalName { get; set; }
    public string? JournalIssn { get; set; }
    public string? JournalEissn { get; set; }

    public string? SeriesName { get; set; }
    public string? SeriesIssn { get; set; }

    /// <summary>Container title for non-journal types (kept in FieldsJson too).</summary>
    public string? ContainerTitle { get; set; }

    public List<(string Scheme, string Value)> Identifiers { get; } = new();

    /// <summary>The source's own citation key/id — recorded but not trusted (PLAN §7).</summary>
    public string? SourceKey { get; set; }
}
