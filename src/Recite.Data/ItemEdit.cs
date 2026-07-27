using Recite.Core.Csl;

namespace Recite.Data;

/// <summary>
/// A flat, UI-friendly edit request for an item. The scalar CSL fields and contributors
/// live in <see cref="Document"/>; the relational extras are listed alongside. Passed to
/// <see cref="LibraryService.Upsert"/>.
/// </summary>
public sealed class ItemEdit
{
    /// <summary>Null to create a new item; otherwise the item to update (identity preserved).</summary>
    public Guid? Uuid { get; set; }

    /// <summary>CSL scalar fields + author/editor/… names.</summary>
    public CslDocument Document { get; set; } = new();

    public List<Guid> CategoryUuids { get; set; } = new();
    public List<string> Tags { get; set; } = new();
    public List<AttachmentEdit> Attachments { get; set; } = new();

    /// <summary>crossref container (proceedings volume / edited book).</summary>
    public Guid? ContainerParentUuid { get; set; }

    /// <summary>Explicit library-default citation key; null regenerates from the scheme.</summary>
    public string? CitationKeyOverride { get; set; }

    public string KeyScheme { get; set; } = Core.Citations.CitationKeyScheme.Default;
}

public sealed class AttachmentEdit
{
    public string RelativePath { get; set; } = "";
    public string? Title { get; set; }
}
