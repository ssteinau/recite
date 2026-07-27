namespace Recite.Core.Model;

/// <summary>
/// An authoring workspace for one of your own papers (PLAN §3). First-class and
/// independent of categories: membership is an explicit curated set. Carries a
/// citation-key scheme and a stack of projections applied at export only.
/// </summary>
public class Project
{
    public int Id { get; set; }
    public Guid Uuid { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "";

    /// <summary>Citation-key scheme template (see CitationKeyScheme).</summary>
    public string KeyScheme { get; set; } = "[auth][year][shorttitle]";

    /// <summary>Serialized <c>ProjectionSet</c> (abbreviation tables, substitutions, overrides).</summary>
    public string ProjectionsJson { get; set; } = "{}";

    public DateTimeOffset DateCreated { get; set; }

    public List<ProjectMember> Members { get; set; } = new();
}

/// <summary>Ordered membership of an item in a project.</summary>
public class ProjectMember
{
    public int ProjectId { get; set; }
    public Project? Project { get; set; }

    public int ItemId { get; set; }
    public Item? Item { get; set; }

    public int Order { get; set; }

    /// <summary>Optional per-project citation-key override for this item.</summary>
    public string? KeyOverride { get; set; }
}
