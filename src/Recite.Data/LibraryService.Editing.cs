using Microsoft.EntityFrameworkCore;
using Recite.Core.Citations;
using Recite.Core.Csl;
using Recite.Core.Model;

namespace Recite.Data;

public sealed partial class LibraryService
{
    /// <summary>Create or update an item, preserving its <see cref="Item.Uuid"/> identity.</summary>
    public Guid Upsert(ItemEdit edit)
    {
        using var ctx = CreateContext();
        var draft = CslMapper.ToDraft(edit.Document);
        var now = DateTimeOffset.UtcNow;

        Item item;
        if (edit.Uuid is Guid uuid)
        {
            item = ctx.Items
                .Include(i => i.Contributions)
                .Include(i => i.Identifiers)
                .Include(i => i.Categories)
                .Include(i => i.Tags)
                .Include(i => i.Attachments)
                .First(i => i.Uuid == uuid);
            ctx.Contributions.RemoveRange(item.Contributions);
            ctx.Identifiers.RemoveRange(item.Identifiers);
            ctx.ItemCategories.RemoveRange(item.Categories);
            ctx.ItemTags.RemoveRange(item.Tags);
            ctx.Attachments.RemoveRange(item.Attachments);
            item.Contributions.Clear();
            item.Identifiers.Clear();
            item.Categories.Clear();
            item.Tags.Clear();
            item.Attachments.Clear();
        }
        else
        {
            item = new Item { Uuid = Guid.NewGuid(), DateAdded = now };
            ctx.Items.Add(item);
        }

        item.Type = draft.Type;
        item.FieldsJson = draft.FieldsJson;
        item.Title = draft.Title;
        item.Year = draft.Year;
        item.DateModified = now;

        // Contributors.
        var personCache = ctx.Persons.Local.ToDictionary(p => p.UniqueKey, p => p);
        foreach (var p in ctx.Persons) personCache.TryAdd(p.UniqueKey, p);
        var roleCounters = new Dictionary<ContributorRole, int>();
        foreach (var (role, name) in draft.Contributors)
        {
            var person = ResolvePerson(ctx, personCache, name);
            int order = roleCounters.GetValueOrDefault(role);
            roleCounters[role] = order + 1;
            item.Contributions.Add(new Contribution { Person = person, Role = role, Order = order });
        }

        // Journal / series.
        item.Journal = null; item.JournalId = null;
        if (!string.IsNullOrWhiteSpace(draft.JournalName))
        {
            var jc = ctx.Journals.ToDictionary(j => j.UniqueKey, j => j);
            item.Journal = ResolveJournal(ctx, jc, draft.JournalName!, draft.JournalIssn, draft.JournalEissn);
        }
        item.Series = null; item.SeriesId = null;
        if (!string.IsNullOrWhiteSpace(draft.SeriesName))
        {
            var sc = ctx.Series.ToDictionary(s => s.UniqueKey, s => s);
            item.Series = ResolveSeries(ctx, sc, draft.SeriesName!, draft.SeriesIssn);
        }

        // Container parent (crossref).
        item.ContainerParent = null; item.ContainerParentId = null;
        if (edit.ContainerParentUuid is Guid pu)
            item.ContainerParent = ctx.Items.FirstOrDefault(i => i.Uuid == pu);

        // Identifiers (skip ones taken by a different item to respect the unique index).
        foreach (var (sc, val) in draft.Identifiers)
        {
            var owner = ctx.Identifiers
                .Include(x => x.Item)
                .FirstOrDefault(x => x.Scheme == sc && x.Value == val);
            if (owner is not null && owner.Item!.Uuid != item.Uuid) continue;
            item.Identifiers.Add(new ExternalIdentifier { Scheme = sc, Value = val });
        }

        // Categories.
        int catOrder = 0;
        foreach (var cu in edit.CategoryUuids.Distinct())
        {
            var cat = ctx.Categories.FirstOrDefault(c => c.Uuid == cu);
            if (cat is not null) item.Categories.Add(new ItemCategory { Category = cat, Order = catOrder++ });
        }

        // Tags.
        foreach (var tagName in edit.Tags.Select(t => t.Trim()).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var tag = ctx.Tags.FirstOrDefault(t => t.Name == tagName) ?? new Tag { Name = tagName };
            item.Tags.Add(new ItemTag { Tag = tag });
        }

        // Attachments.
        int attOrder = 0;
        foreach (var a in edit.Attachments.Where(a => !string.IsNullOrWhiteSpace(a.RelativePath)))
            item.Attachments.Add(new Attachment { RelativePath = a.RelativePath, Title = a.Title, Order = attOrder++ });

        // Citation key.
        var takenKeys = ctx.Items.Where(i => i.CitationKey != null && i.Uuid != item.Uuid)
            .Select(i => i.CitationKey!).AsEnumerable().ToHashSet();
        var baseKey = string.IsNullOrWhiteSpace(edit.CitationKeyOverride)
            ? new CitationKeyScheme(edit.KeyScheme).BaseKey(edit.Document)
            : edit.CitationKeyOverride!;
        item.CitationKey = CitationKeyScheme.Disambiguate(baseKey, takenKeys.Contains);

        ctx.SaveChanges();
        IndexItem(ctx, item);
        return item.Uuid;
    }

    public void DeleteItem(Guid uuid)
    {
        using var ctx = CreateContext();
        var item = ctx.Items.FirstOrDefault(i => i.Uuid == uuid);
        if (item is null) return;
        int id = item.Id;
        ctx.Items.Remove(item);
        ctx.SaveChanges();
        RemoveFromIndex(ctx, id);
    }

    /// <summary>Rebuild the whole FTS index from scratch (after bulk changes / merges).</summary>
    public void RebuildIndex()
    {
        using var ctx = CreateContext();
        ctx.Database.ExecuteSqlRaw("DELETE FROM item_fts");
        foreach (var item in ctx.Items.Select(i => new { i.Id }).ToList())
            IndexItem(ctx, new Item { Id = item.Id });
    }
}
