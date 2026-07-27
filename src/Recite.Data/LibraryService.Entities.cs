using Microsoft.EntityFrameworkCore;
using Recite.Core.Dedup;
using Recite.Core.Model;

namespace Recite.Data;

public sealed partial class LibraryService
{
    // ---- facets & categories ------------------------------------------------

    public IReadOnlyList<Facet> GetFacets()
    {
        using var ctx = CreateContext();
        return ctx.Facets
            .Include(f => f.Categories)
            .OrderBy(f => f.Order).ThenBy(f => f.Name)
            .AsNoTracking()
            .ToList();
    }

    public Guid AddFacet(string name)
    {
        using var ctx = CreateContext();
        var order = (ctx.Facets.Max(f => (int?)f.Order) ?? -1) + 1;
        var facet = new Facet { Name = name.Trim(), Order = order };
        ctx.Facets.Add(facet);
        ctx.SaveChanges();
        return facet.Uuid;
    }

    public Guid AddCategory(Guid facetUuid, Guid? parentUuid, string name)
    {
        using var ctx = CreateContext();
        var facet = ctx.Facets.First(f => f.Uuid == facetUuid);
        int? parentId = parentUuid is Guid pu ? ctx.Categories.First(c => c.Uuid == pu).Id : null;
        var siblings = ctx.Categories.Where(c => c.FacetId == facet.Id && c.ParentId == parentId);
        var order = (siblings.Max(c => (int?)c.Order) ?? -1) + 1;
        var cat = new Category { FacetId = facet.Id, ParentId = parentId, Name = name.Trim(), Order = order };
        ctx.Categories.Add(cat);
        ctx.SaveChanges();
        return cat.Uuid;
    }

    public void RenameCategory(Guid uuid, string name)
    {
        using var ctx = CreateContext();
        var cat = ctx.Categories.First(c => c.Uuid == uuid);
        cat.Name = name.Trim();
        ctx.SaveChanges();
    }

    public void DeleteCategory(Guid uuid)
    {
        using var ctx = CreateContext();
        var cat = ctx.Categories.Include(c => c.Children).First(c => c.Uuid == uuid);
        // Re-parent children to this node's parent, then delete.
        foreach (var child in cat.Children) child.ParentId = cat.ParentId;
        ctx.Categories.Remove(cat);
        ctx.SaveChanges();
    }

    public void RenameFacet(Guid uuid, string name)
    {
        using var ctx = CreateContext();
        var facet = ctx.Facets.First(f => f.Uuid == uuid);
        facet.Name = name.Trim();
        ctx.SaveChanges();
    }

    public void DeleteFacet(Guid uuid)
    {
        using var ctx = CreateContext();
        var facet = ctx.Facets.First(f => f.Uuid == uuid);
        ctx.Facets.Remove(facet);
        ctx.SaveChanges();
    }

    /// <summary>Item ids classified under a category (used for browsing by facet).</summary>
    public IReadOnlyList<int> ItemIdsInCategory(Guid categoryUuid)
    {
        using var ctx = CreateContext();
        var cat = ctx.Categories.First(c => c.Uuid == categoryUuid);
        return ctx.ItemCategories.Where(ic => ic.CategoryId == cat.Id).Select(ic => ic.ItemId).ToList();
    }

    // ---- persons / journals / series / tags ---------------------------------

    public IReadOnlyList<(Person Person, int Count)> GetPersons()
    {
        using var ctx = CreateContext();
        return ctx.Persons
            .Select(p => new { p, count = p.Contributions.Count })
            .AsNoTracking()
            .ToList()
            .Select(x => (x.p, x.count))
            .OrderByDescending(x => x.count)
            .ToList();
    }

    public IReadOnlyList<(Journal Journal, int Count)> GetJournals()
    {
        using var ctx = CreateContext();
        return ctx.Journals
            .Select(j => new { j, count = j.Items.Count })
            .AsNoTracking().ToList()
            .Select(x => (x.j, x.count))
            .OrderBy(x => x.j.Name).ToList();
    }

    public IReadOnlyList<(Series Series, int Count)> GetSeries()
    {
        using var ctx = CreateContext();
        return ctx.Series
            .Select(s => new { s, count = s.Items.Count })
            .AsNoTracking().ToList()
            .Select(x => (x.s, x.count))
            .OrderBy(x => x.s.Name).ToList();
    }

    public IReadOnlyList<(Tag Tag, int Count)> GetTags()
    {
        using var ctx = CreateContext();
        return ctx.Tags
            .Select(t => new { t, count = t.Items.Count })
            .AsNoTracking().ToList()
            .Select(x => (x.t, x.count))
            .OrderBy(x => x.t.Name).ToList();
    }

    public void RenamePerson(Guid uuid, Person edited)
    {
        using var ctx = CreateContext();
        var p = ctx.Persons.First(x => x.Uuid == uuid);
        p.Family = edited.Family; p.Given = edited.Given; p.Suffix = edited.Suffix;
        p.DroppingParticle = edited.DroppingParticle; p.NonDroppingParticle = edited.NonDroppingParticle;
        p.Literal = edited.Literal;
        p.UniqueKey = Person.ComputeKey(p);
        ctx.SaveChanges();
        RebuildIndex();
    }

    public void RenameJournal(Guid uuid, string name, string? abbreviation, string? issn)
    {
        using var ctx = CreateContext();
        var j = ctx.Journals.First(x => x.Uuid == uuid);
        j.Name = name.Trim(); j.Abbreviation = abbreviation; j.Issn = issn;
        j.UniqueKey = Journal.ComputeKey(name);
        ctx.SaveChanges();
        RebuildIndex();
    }

    // ---- duplicate detection ------------------------------------------------

    public IReadOnlyList<DuplicatePair> FindItemDuplicates()
    {
        using var ctx = CreateContext();
        var records = ctx.Items
            .Include(i => i.Contributions).ThenInclude(c => c.Person)
            .Include(i => i.Identifiers)
            .AsNoTracking()
            .ToList()
            .Select(DedupRecord.From)
            .ToList();
        return new DedupService().FindDuplicates(records);
    }

    public IReadOnlyList<PersonMatch> FindPersonDuplicates()
    {
        using var ctx = CreateContext();
        var records = ctx.Persons.AsNoTracking().ToList().Select(PersonDedup.ToRecord).ToList();
        return PersonDedup.Find(records);
    }

    public Item? GetItemLite(Guid uuid)
    {
        using var ctx = CreateContext();
        return ctx.Items
            .Include(i => i.Contributions).ThenInclude(c => c.Person)
            .AsNoTracking()
            .FirstOrDefault(i => i.Uuid == uuid);
    }

    public Person? GetPerson(Guid uuid)
    {
        using var ctx = CreateContext();
        return ctx.Persons.AsNoTracking().FirstOrDefault(p => p.Uuid == uuid);
    }

    public IReadOnlyList<MergeLog> GetMergeLog()
    {
        using var ctx = CreateContext();
        // Order by Id (insertion order); SQLite can't ORDER BY DateTimeOffset.
        return ctx.MergeLogs.OrderByDescending(l => l.Id).AsNoTracking().ToList();
    }

    /// <summary>Container references (proceedings volumes, edited books, books) for crossref linking.</summary>
    public IReadOnlyList<(Guid Uuid, string Label)> GetContainers()
    {
        using var ctx = CreateContext();
        return ctx.Items
            .Where(i => i.Type == "proceedings" || i.Type == "collection" || i.Type == "book")
            .OrderBy(i => i.Title)
            .Select(i => new { i.Uuid, i.Title, i.Year })
            .AsNoTracking().ToList()
            .Select(x => (x.Uuid, string.IsNullOrEmpty(x.Title) ? "(untitled)" : $"{x.Title} ({x.Year})"))
            .ToList();
    }
}
