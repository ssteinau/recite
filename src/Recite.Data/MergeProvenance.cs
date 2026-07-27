using System.Text.Json;
using Recite.Core.Model;

namespace Recite.Data;

/// <summary>Serializable record of exactly what a merge changed, so it can be reversed.</summary>
public sealed class MergeProvenance
{
    public PersonSnap? AbsorbedPerson { get; set; }
    public JournalSnap? AbsorbedJournal { get; set; }
    public SeriesSnap? AbsorbedSeries { get; set; }
    public ItemSnap? AbsorbedItem { get; set; }

    // Person merge: contributions repointed to the survivor / dropped as duplicates.
    public List<int> MovedContributionIds { get; set; } = new();
    public List<ContributionSnap> DroppedContributions { get; set; } = new();

    // Journal/series merge: items repointed to the survivor.
    public List<int> MovedItemIds { get; set; } = new();

    // Item merge: rows moved onto / added to the survivor, and links repointed.
    public List<int> MovedIdentifierIds { get; set; } = new();
    public List<int> MovedAttachmentIds { get; set; } = new();
    public List<int> AddedCategoryIds { get; set; } = new();
    public List<int> AddedTagIds { get; set; } = new();
    public List<int> RepointedChildItemIds { get; set; } = new();
    public List<int> RepointedProjectIds { get; set; } = new();

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };
    public string ToJson() => JsonSerializer.Serialize(this, Options);
    public static MergeProvenance FromJson(string json) =>
        JsonSerializer.Deserialize<MergeProvenance>(json, Options) ?? new MergeProvenance();
}

public sealed class PersonSnap
{
    public Guid Uuid { get; set; }
    public string? Family { get; set; }
    public string? Given { get; set; }
    public string? DroppingParticle { get; set; }
    public string? NonDroppingParticle { get; set; }
    public string? Suffix { get; set; }
    public string? Literal { get; set; }
    public string UniqueKey { get; set; } = "";

    public static PersonSnap From(Person p) => new()
    {
        Uuid = p.Uuid, Family = p.Family, Given = p.Given, DroppingParticle = p.DroppingParticle,
        NonDroppingParticle = p.NonDroppingParticle, Suffix = p.Suffix, Literal = p.Literal, UniqueKey = p.UniqueKey,
    };

    public Person ToPerson() => new()
    {
        Uuid = Uuid, Family = Family, Given = Given, DroppingParticle = DroppingParticle,
        NonDroppingParticle = NonDroppingParticle, Suffix = Suffix, Literal = Literal, UniqueKey = UniqueKey,
    };
}

public sealed class JournalSnap
{
    public Guid Uuid { get; set; }
    public string Name { get; set; } = "";
    public string? Abbreviation { get; set; }
    public string? Issn { get; set; }
    public string? Eissn { get; set; }
    public string UniqueKey { get; set; } = "";

    public static JournalSnap From(Journal j) => new()
    { Uuid = j.Uuid, Name = j.Name, Abbreviation = j.Abbreviation, Issn = j.Issn, Eissn = j.Eissn, UniqueKey = j.UniqueKey };
    public Journal ToJournal() => new()
    { Uuid = Uuid, Name = Name, Abbreviation = Abbreviation, Issn = Issn, Eissn = Eissn, UniqueKey = UniqueKey };
}

public sealed class SeriesSnap
{
    public Guid Uuid { get; set; }
    public string Name { get; set; } = "";
    public string? Abbreviation { get; set; }
    public string? Issn { get; set; }
    public string UniqueKey { get; set; } = "";

    public static SeriesSnap From(Series s) => new()
    { Uuid = s.Uuid, Name = s.Name, Abbreviation = s.Abbreviation, Issn = s.Issn, UniqueKey = s.UniqueKey };
    public Series ToSeries() => new()
    { Uuid = Uuid, Name = Name, Abbreviation = Abbreviation, Issn = Issn, UniqueKey = UniqueKey };
}

public sealed class ContributionSnap
{
    public int ItemId { get; set; }
    public int Role { get; set; }
    public int Order { get; set; }
}

public sealed class ItemSnap
{
    public Guid Uuid { get; set; }
    public string Type { get; set; } = "";
    public string FieldsJson { get; set; } = "{}";
    public string? Title { get; set; }
    public int? Year { get; set; }
    public string? CitationKey { get; set; }
    public int? JournalId { get; set; }
    public int? SeriesId { get; set; }
    public int? ContainerParentId { get; set; }
    public DateTimeOffset DateAdded { get; set; }
    public List<ContributionSnap2> Contributions { get; set; } = new();
    public List<(string, string)> Identifiers { get; set; } = new();

    public static ItemSnap From(Item i) => new()
    {
        Uuid = i.Uuid, Type = i.Type, FieldsJson = i.FieldsJson, Title = i.Title, Year = i.Year,
        CitationKey = i.CitationKey, JournalId = i.JournalId, SeriesId = i.SeriesId,
        ContainerParentId = i.ContainerParentId, DateAdded = i.DateAdded,
        Contributions = i.Contributions.Select(c => new ContributionSnap2
        { PersonId = c.PersonId, Role = (int)c.Role, Order = c.Order }).ToList(),
        Identifiers = i.Identifiers.Select(x => (x.Scheme, x.Value)).ToList(),
    };
}

public sealed class ContributionSnap2
{
    public int PersonId { get; set; }
    public int Role { get; set; }
    public int Order { get; set; }
}
