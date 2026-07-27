using Microsoft.EntityFrameworkCore;
using Recite.Core.Model;

namespace Recite.Data;

/// <summary>
/// Reconciles duplicates by merging an absorbed entity into a surviving one, recording every
/// change in a <see cref="MergeLog"/> so the merge is auditable and reversible (PLAN §3).
/// Applies to items, persons, journals and series.
/// </summary>
public sealed class MergeService
{
    private readonly LibraryService _lib;
    public MergeService(LibraryService lib) => _lib = lib;

    // ---- persons ------------------------------------------------------------

    public Guid MergePersons(Guid survivingUuid, Guid absorbedUuid)
    {
        using var ctx = _lib.CreateContext();
        var surviving = ctx.Persons.First(p => p.Uuid == survivingUuid);
        var absorbed = ctx.Persons.Include(p => p.Contributions).First(p => p.Uuid == absorbedUuid);

        var prov = new MergeProvenance { AbsorbedPerson = PersonSnap.From(absorbed) };
        var survivingLinks = ctx.Contributions
            .Where(c => c.PersonId == surviving.Id)
            .Select(c => new { c.ItemId, c.Role })
            .ToHashSet();

        foreach (var c in absorbed.Contributions.ToList())
        {
            if (survivingLinks.Contains(new { c.ItemId, c.Role }))
            {
                prov.DroppedContributions.Add(new ContributionSnap { ItemId = c.ItemId, Role = (int)c.Role, Order = c.Order });
                ctx.Contributions.Remove(c);
            }
            else
            {
                c.PersonId = surviving.Id;
                prov.MovedContributionIds.Add(c.Id);
                survivingLinks.Add(new { c.ItemId, c.Role });
            }
        }

        ctx.Persons.Remove(absorbed);
        var log = WriteLog(ctx, MergeEntityKind.Person, survivingUuid, absorbedUuid, prov);
        ctx.SaveChanges();
        _lib.RebuildIndex();
        return log.Uuid;
    }

    // ---- journals / series --------------------------------------------------

    public Guid MergeJournals(Guid survivingUuid, Guid absorbedUuid)
    {
        using var ctx = _lib.CreateContext();
        var surviving = ctx.Journals.First(j => j.Uuid == survivingUuid);
        var absorbed = ctx.Journals.First(j => j.Uuid == absorbedUuid);
        var prov = new MergeProvenance { AbsorbedJournal = JournalSnap.From(absorbed) };

        foreach (var item in ctx.Items.Where(i => i.JournalId == absorbed.Id))
        {
            item.JournalId = surviving.Id;
            prov.MovedItemIds.Add(item.Id);
        }
        surviving.Issn ??= absorbed.Issn;
        surviving.Eissn ??= absorbed.Eissn;
        surviving.Abbreviation ??= absorbed.Abbreviation;

        ctx.Journals.Remove(absorbed);
        var log = WriteLog(ctx, MergeEntityKind.Journal, survivingUuid, absorbedUuid, prov);
        ctx.SaveChanges();
        _lib.RebuildIndex();
        return log.Uuid;
    }

    public Guid MergeSeries(Guid survivingUuid, Guid absorbedUuid)
    {
        using var ctx = _lib.CreateContext();
        var surviving = ctx.Series.First(s => s.Uuid == survivingUuid);
        var absorbed = ctx.Series.First(s => s.Uuid == absorbedUuid);
        var prov = new MergeProvenance { AbsorbedSeries = SeriesSnap.From(absorbed) };

        foreach (var item in ctx.Items.Where(i => i.SeriesId == absorbed.Id))
        {
            item.SeriesId = surviving.Id;
            prov.MovedItemIds.Add(item.Id);
        }
        surviving.Issn ??= absorbed.Issn;
        surviving.Abbreviation ??= absorbed.Abbreviation;

        ctx.Series.Remove(absorbed);
        var log = WriteLog(ctx, MergeEntityKind.Series, survivingUuid, absorbedUuid, prov);
        ctx.SaveChanges();
        return log.Uuid;
    }

    // ---- items --------------------------------------------------------------

    public Guid MergeItems(Guid survivingUuid, Guid absorbedUuid)
    {
        using var ctx = _lib.CreateContext();
        var surviving = ctx.Items
            .Include(i => i.Identifiers).Include(i => i.Categories).Include(i => i.Tags).Include(i => i.Attachments)
            .First(i => i.Uuid == survivingUuid);
        var absorbed = ctx.Items
            .Include(i => i.Identifiers).Include(i => i.Contributions)
            .First(i => i.Uuid == absorbedUuid);

        var prov = new MergeProvenance { AbsorbedItem = ItemSnap.From(absorbed) };

        // Absorb identifiers not already present on the survivor.
        var haveIds = surviving.Identifiers.Select(x => (x.Scheme, x.Value)).ToHashSet();
        foreach (var id in ctx.Identifiers.Where(x => x.ItemId == absorbed.Id).ToList())
        {
            if (haveIds.Contains((id.Scheme, id.Value))) continue;
            id.ItemId = surviving.Id;
            prov.MovedIdentifierIds.Add(id.Id);
            haveIds.Add((id.Scheme, id.Value));
        }

        // Absorb attachments.
        foreach (var a in ctx.Attachments.Where(x => x.ItemId == absorbed.Id).ToList())
        {
            a.ItemId = surviving.Id;
            prov.MovedAttachmentIds.Add(a.Id);
        }

        // Absorb category memberships not already present.
        var haveCats = surviving.Categories.Select(c => c.CategoryId).ToHashSet();
        foreach (var ic in ctx.ItemCategories.Where(x => x.ItemId == absorbed.Id).ToList())
        {
            if (haveCats.Contains(ic.CategoryId)) { ctx.ItemCategories.Remove(ic); continue; }
            ctx.ItemCategories.Remove(ic);
            ctx.ItemCategories.Add(new ItemCategory { ItemId = surviving.Id, CategoryId = ic.CategoryId, Order = ic.Order });
            prov.AddedCategoryIds.Add(ic.CategoryId);
            haveCats.Add(ic.CategoryId);
        }

        // Absorb tags not already present.
        var haveTags = surviving.Tags.Select(t => t.TagId).ToHashSet();
        foreach (var it in ctx.ItemTags.Where(x => x.ItemId == absorbed.Id).ToList())
        {
            if (haveTags.Contains(it.TagId)) { ctx.ItemTags.Remove(it); continue; }
            ctx.ItemTags.Remove(it);
            ctx.ItemTags.Add(new ItemTag { ItemId = surviving.Id, TagId = it.TagId });
            prov.AddedTagIds.Add(it.TagId);
            haveTags.Add(it.TagId);
        }

        // Repoint container children and project memberships.
        foreach (var child in ctx.Items.Where(i => i.ContainerParentId == absorbed.Id))
        {
            child.ContainerParentId = surviving.Id;
            prov.RepointedChildItemIds.Add(child.Id);
        }
        var survivorProjects = ctx.ProjectMembers.Where(m => m.ItemId == surviving.Id).Select(m => m.ProjectId).ToHashSet();
        foreach (var m in ctx.ProjectMembers.Where(x => x.ItemId == absorbed.Id).ToList())
        {
            ctx.ProjectMembers.Remove(m);
            if (!survivorProjects.Contains(m.ProjectId))
            {
                ctx.ProjectMembers.Add(new ProjectMember { ProjectId = m.ProjectId, ItemId = surviving.Id, Order = m.Order, KeyOverride = m.KeyOverride });
                prov.RepointedProjectIds.Add(m.ProjectId);
            }
        }

        int absorbedId = absorbed.Id;
        ctx.Items.Remove(absorbed);
        var log = WriteLog(ctx, MergeEntityKind.Item, survivingUuid, absorbedUuid, prov);
        ctx.SaveChanges();
        _lib.RemoveFromIndex(ctx, absorbedId);
        _lib.IndexItem(ctx, surviving);
        return log.Uuid;
    }

    // ---- revert -------------------------------------------------------------

    public void Revert(Guid mergeLogUuid)
    {
        using var ctx = _lib.CreateContext();
        var log = ctx.MergeLogs.First(l => l.Uuid == mergeLogUuid);
        if (log.Reverted) return;
        var prov = MergeProvenance.FromJson(log.ProvenanceJson);

        switch (log.Kind)
        {
            case MergeEntityKind.Person: RevertPerson(ctx, prov); break;
            case MergeEntityKind.Journal: RevertJournal(ctx, prov); break;
            case MergeEntityKind.Series: RevertSeries(ctx, prov); break;
            case MergeEntityKind.Item: RevertItem(ctx, prov); break;
        }

        log.Reverted = true;
        ctx.SaveChanges();
        _lib.RebuildIndex();
    }

    private static void RevertPerson(ReciteDbContext ctx, MergeProvenance prov)
    {
        var person = prov.AbsorbedPerson!.ToPerson();
        ctx.Persons.Add(person);
        ctx.SaveChanges();
        foreach (var cid in prov.MovedContributionIds)
        {
            var c = ctx.Contributions.FirstOrDefault(x => x.Id == cid);
            if (c is not null) c.PersonId = person.Id;
        }
        foreach (var d in prov.DroppedContributions)
            ctx.Contributions.Add(new Contribution
            { ItemId = d.ItemId, PersonId = person.Id, Role = (ContributorRole)d.Role, Order = d.Order });
    }

    private static void RevertJournal(ReciteDbContext ctx, MergeProvenance prov)
    {
        var journal = prov.AbsorbedJournal!.ToJournal();
        ctx.Journals.Add(journal);
        ctx.SaveChanges();
        foreach (var iid in prov.MovedItemIds)
        {
            var i = ctx.Items.FirstOrDefault(x => x.Id == iid);
            if (i is not null) i.JournalId = journal.Id;
        }
    }

    private static void RevertSeries(ReciteDbContext ctx, MergeProvenance prov)
    {
        var series = prov.AbsorbedSeries!.ToSeries();
        ctx.Series.Add(series);
        ctx.SaveChanges();
        foreach (var iid in prov.MovedItemIds)
        {
            var i = ctx.Items.FirstOrDefault(x => x.Id == iid);
            if (i is not null) i.SeriesId = series.Id;
        }
    }

    private static void RevertItem(ReciteDbContext ctx, MergeProvenance prov)
    {
        var snap = prov.AbsorbedItem!;
        var item = new Item
        {
            Uuid = snap.Uuid, Type = snap.Type, FieldsJson = snap.FieldsJson, Title = snap.Title,
            Year = snap.Year, CitationKey = snap.CitationKey, JournalId = snap.JournalId,
            SeriesId = snap.SeriesId, ContainerParentId = snap.ContainerParentId,
            DateAdded = snap.DateAdded, DateModified = DateTimeOffset.UtcNow,
        };
        foreach (var c in snap.Contributions)
            item.Contributions.Add(new Contribution { PersonId = c.PersonId, Role = (ContributorRole)c.Role, Order = c.Order });
        ctx.Items.Add(item);
        ctx.SaveChanges();

        // Move back identifiers/attachments that came from the absorbed item.
        foreach (var idId in prov.MovedIdentifierIds)
        {
            var id = ctx.Identifiers.FirstOrDefault(x => x.Id == idId);
            if (id is not null) id.ItemId = item.Id;
        }
        foreach (var aId in prov.MovedAttachmentIds)
        {
            var a = ctx.Attachments.FirstOrDefault(x => x.Id == aId);
            if (a is not null) a.ItemId = item.Id;
        }
        // Remove categories/tags the merge added to the survivor and restore on the absorbed.
        var survivorUuid = ctx.MergeLogs.OrderByDescending(l => l.Id).First(l => l.AbsorbedUuid == snap.Uuid).SurvivingUuid;
        var survivor = ctx.Items.FirstOrDefault(i => i.Uuid == survivorUuid);
        if (survivor is not null)
        {
            foreach (var catId in prov.AddedCategoryIds)
            {
                var ic = ctx.ItemCategories.FirstOrDefault(x => x.ItemId == survivor.Id && x.CategoryId == catId);
                if (ic is not null) ctx.ItemCategories.Remove(ic);
                ctx.ItemCategories.Add(new ItemCategory { ItemId = item.Id, CategoryId = catId });
            }
            foreach (var tagId in prov.AddedTagIds)
            {
                var it = ctx.ItemTags.FirstOrDefault(x => x.ItemId == survivor.Id && x.TagId == tagId);
                if (it is not null) ctx.ItemTags.Remove(it);
                ctx.ItemTags.Add(new ItemTag { ItemId = item.Id, TagId = tagId });
            }
        }
        foreach (var childId in prov.RepointedChildItemIds)
        {
            var child = ctx.Items.FirstOrDefault(x => x.Id == childId);
            if (child is not null) child.ContainerParentId = item.Id;
        }
        foreach (var projId in prov.RepointedProjectIds)
        {
            if (survivor is not null)
            {
                var m = ctx.ProjectMembers.FirstOrDefault(x => x.ProjectId == projId && x.ItemId == survivor.Id);
                if (m is not null) ctx.ProjectMembers.Remove(m);
            }
            ctx.ProjectMembers.Add(new ProjectMember { ProjectId = projId, ItemId = item.Id });
        }
    }

    private static MergeLog WriteLog(ReciteDbContext ctx, MergeEntityKind kind, Guid surviving, Guid absorbed, MergeProvenance prov)
    {
        var log = new MergeLog
        {
            Kind = kind,
            SurvivingUuid = surviving,
            AbsorbedUuid = absorbed,
            Timestamp = DateTimeOffset.UtcNow,
            ProvenanceJson = prov.ToJson(),
        };
        ctx.MergeLogs.Add(log);
        return log;
    }
}
