using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Recite.Core.Citations;
using Recite.Core.Csl;
using Recite.Core.Formats;
using Recite.Core.Model;

namespace Recite.Data;

/// <summary>
/// The primary application service over a library: import, search (FTS5), item CRUD, and
/// the shared entity resolution (person/journal/series dedup) that keeps the database
/// consistent and deduplicated (PLAN §1).
/// </summary>
public sealed partial class LibraryService
{
    private readonly LibraryConnection _conn;
    private readonly FormatRegistry _formats;

    public LibraryService(LibraryConnection conn, FormatRegistry? formats = null)
    {
        _conn = conn;
        _formats = formats ?? FormatRegistry.CreateDefault();
    }

    public LibraryConnection Connection => _conn;
    public FormatRegistry Formats => _formats;
    public ReciteDbContext CreateContext() => _conn.CreateContext();

    // ---- import -------------------------------------------------------------

    public ImportResult ImportText(string text, string formatId, ImportOptions? options = null)
    {
        var reader = _formats.Reader(formatId)
            ?? throw new ArgumentException($"Unknown format '{formatId}'", nameof(formatId));
        return Import(reader.Read(text), options);
    }

    public ImportResult Import(IReadOnlyList<CslDocument> docs, ImportOptions? options = null)
    {
        options ??= new ImportOptions();
        using var ctx = CreateContext();

        var personCache = ctx.Persons.ToDictionary(p => p.UniqueKey, p => p);
        var journalCache = ctx.Journals.ToDictionary(j => j.UniqueKey, j => j);
        var seriesCache = ctx.Series.ToDictionary(s => s.UniqueKey, s => s);
        var takenIds = ctx.Identifiers
            .Select(i => new { i.Scheme, i.Value })
            .AsEnumerable()
            .Select(i => (i.Scheme, i.Value))
            .ToHashSet();
        var takenKeys = ctx.Items.Where(i => i.CitationKey != null)
            .Select(i => i.CitationKey!).AsEnumerable().ToHashSet();

        var scheme = new CitationKeyScheme(options.DefaultKeyScheme);
        var now = DateTimeOffset.UtcNow;

        var added = new List<Item>();
        var addedUuids = new List<Guid>();
        var warnings = new List<string>();
        var bySourceKey = new Dictionary<string, Item>(StringComparer.OrdinalIgnoreCase);
        var pending = new List<(Item item, string? crossref)>();
        int skipped = 0;

        foreach (var doc in docs)
        {
            var draft = CslMapper.ToDraft(doc);

            // Exact-duplicate short-circuit on any shared identifier.
            if (options.SkipExactDuplicates &&
                draft.Identifiers.Any(id => takenIds.Contains((id.Scheme, id.Value))))
            {
                skipped++;
                continue;
            }

            var item = new Item
            {
                Uuid = draft.Uuid ?? Guid.NewGuid(),
                Type = draft.Type,
                FieldsJson = draft.FieldsJson,
                Title = draft.Title,
                Year = draft.Year,
                DateAdded = now,
                DateModified = now,
            };

            // Contributors (deduped persons, ordered per role).
            var roleCounters = new Dictionary<ContributorRole, int>();
            foreach (var (role, name) in draft.Contributors)
            {
                var person = ResolvePerson(ctx, personCache, name);
                int order = roleCounters.GetValueOrDefault(role);
                roleCounters[role] = order + 1;
                item.Contributions.Add(new Contribution { Person = person, Role = role, Order = order });
            }

            if (!string.IsNullOrWhiteSpace(draft.JournalName))
                item.Journal = ResolveJournal(ctx, journalCache, draft.JournalName!, draft.JournalIssn, draft.JournalEissn);
            if (!string.IsNullOrWhiteSpace(draft.SeriesName))
                item.Series = ResolveSeries(ctx, seriesCache, draft.SeriesName!, draft.SeriesIssn);

            foreach (var (sc, val) in draft.Identifiers)
            {
                if (takenIds.Contains((sc, val)))
                {
                    warnings.Add($"'{draft.Title}': identifier {sc}:{val} already in library, kept without it.");
                    continue;
                }
                takenIds.Add((sc, val));
                item.Identifiers.Add(new ExternalIdentifier { Scheme = sc, Value = val });
            }

            // Library-default citation key (imported keys are not trusted, PLAN §7).
            var baseKey = scheme.BaseKey(doc);
            var key = CitationKeyScheme.Disambiguate(baseKey, takenKeys.Contains);
            takenKeys.Add(key);
            item.CitationKey = key;

            foreach (var w in DetectBakedAbbreviations(draft.Title)) warnings.Add(w);

            ctx.Items.Add(item);
            added.Add(item);
            addedUuids.Add(item.Uuid);
            if (!string.IsNullOrWhiteSpace(draft.SourceKey))
                bySourceKey[draft.SourceKey!] = item;
            pending.Add((item, doc.GetString("crossref")));
        }

        // Wire crossref parent→child links within this batch.
        foreach (var (item, crossref) in pending)
        {
            if (!string.IsNullOrWhiteSpace(crossref) && bySourceKey.TryGetValue(crossref!, out var parent) && parent != item)
                item.ContainerParent = parent;
        }

        ctx.SaveChanges();
        foreach (var item in added) IndexItem(ctx, item);

        return new ImportResult(added.Count, skipped, addedUuids, warnings);
    }

    private static readonly Regex BakedAbbrev = new(
        @"\b(Int'l|Conf\.|Proc\.|Trans\.|J\.|Symp\.|Comput\.|Sci\.|Eng\.|Syst\.)",
        RegexOptions.Compiled);

    private static IEnumerable<string> DetectBakedAbbreviations(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) yield break;
        var m = BakedAbbrev.Match(title);
        if (m.Success)
            yield return $"'{title}': looks like a baked-in abbreviation ('{m.Value}') — consider a projection instead (PLAN §11).";
    }

    // ---- entity resolution --------------------------------------------------

    private static Person ResolvePerson(ReciteDbContext ctx, Dictionary<string, Person> cache, CslName name)
    {
        var probe = Person.FromCslName(name);
        if (cache.TryGetValue(probe.UniqueKey, out var existing)) return existing;
        ctx.Persons.Add(probe);
        cache[probe.UniqueKey] = probe;
        return probe;
    }

    private static Journal ResolveJournal(ReciteDbContext ctx, Dictionary<string, Journal> cache,
        string name, string? issn, string? eissn)
    {
        var key = Journal.ComputeKey(name);
        if (cache.TryGetValue(key, out var existing))
        {
            existing.Issn ??= issn;
            existing.Eissn ??= eissn;
            return existing;
        }
        var journal = new Journal { Name = name.Trim(), Issn = issn, Eissn = eissn, UniqueKey = key };
        ctx.Journals.Add(journal);
        cache[key] = journal;
        return journal;
    }

    private static Series ResolveSeries(ReciteDbContext ctx, Dictionary<string, Series> cache, string name, string? issn)
    {
        var key = Series.ComputeKey(name);
        if (cache.TryGetValue(key, out var existing))
        {
            existing.Issn ??= issn;
            return existing;
        }
        var series = new Series { Name = name.Trim(), Issn = issn, UniqueKey = key };
        ctx.Series.Add(series);
        cache[key] = series;
        return series;
    }

    // ---- FTS ----------------------------------------------------------------

    /// <summary>Rebuild the FTS row for a single item (must be called after Ids are assigned).</summary>
    public void IndexItem(ReciteDbContext ctx, Item item)
    {
        var full = ctx.Items
            .Include(i => i.Contributions).ThenInclude(c => c.Person)
            .Include(i => i.Journal)
            .Include(i => i.Tags).ThenInclude(t => t.Tag)
            .First(i => i.Id == item.Id);

        var authors = string.Join(' ', full.Contributions
            .Where(c => c.Role is ContributorRole.Author or ContributorRole.Editor)
            .Select(c => c.Person!.DisplayName()));
        var container = full.Journal?.Name ?? full.GetField("container-title") ?? "";
        var abstractText = full.GetField("abstract") ?? "";
        var tags = string.Join(' ', full.Tags.Select(t => t.Tag!.Name));

        ctx.Database.ExecuteSqlInterpolated($"DELETE FROM item_fts WHERE rowid = {item.Id}");
        ctx.Database.ExecuteSqlInterpolated(
            $"INSERT INTO item_fts(rowid, title, authors, container, abstract, tags) VALUES ({item.Id}, {full.Title ?? ""}, {authors}, {container}, {abstractText}, {tags})");
    }

    public void RemoveFromIndex(ReciteDbContext ctx, int itemId) =>
        ctx.Database.ExecuteSqlInterpolated($"DELETE FROM item_fts WHERE rowid = {itemId}");

    /// <summary>Full-text search returning matching item ids, most relevant first.</summary>
    public IReadOnlyList<int> SearchIds(string query, int limit = 500)
    {
        var match = BuildMatchQuery(query);
        if (match is null) return Array.Empty<int>();
        using var ctx = CreateContext();
        return ctx.Database
            .SqlQuery<int>($"SELECT rowid AS Value FROM item_fts WHERE item_fts MATCH {match} ORDER BY rank LIMIT {limit}")
            .ToList();
    }

    /// <summary>Turn free text into a safe FTS5 prefix query (each term becomes term*).</summary>
    internal static string? BuildMatchQuery(string query)
    {
        var terms = Regex.Split(query ?? "", @"\s+")
            .Where(t => t.Length > 0)
            .Select(t => new string(t.Where(char.IsLetterOrDigit).ToArray()))
            .Where(t => t.Length > 0)
            .Select(t => $"\"{t}\"*")
            .ToList();
        return terms.Count == 0 ? null : string.Join(" AND ", terms);
    }

    // ---- queries ------------------------------------------------------------

    public Item? GetItem(Guid uuid)
    {
        using var ctx = CreateContext();
        return LoadFull(ctx).FirstOrDefault(i => i.Uuid == uuid);
    }

    public IReadOnlyList<Item> GetItems(IReadOnlyCollection<int> ids)
    {
        using var ctx = CreateContext();
        var items = LoadFull(ctx).Where(i => ids.Contains(i.Id)).ToList();
        var order = ids.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i);
        return items.OrderBy(i => order.GetValueOrDefault(i.Id, int.MaxValue)).ToList();
    }

    public IReadOnlyList<Item> AllItems(int limit = 5000)
    {
        using var ctx = CreateContext();
        // Order by Id (insertion order ≈ chronological); SQLite can't ORDER BY DateTimeOffset.
        return LoadFull(ctx).OrderByDescending(i => i.Id).Take(limit).ToList();
    }

    public int CountItems()
    {
        using var ctx = CreateContext();
        return ctx.Items.Count();
    }

    private static IQueryable<Item> LoadFull(ReciteDbContext ctx) => ctx.Items
        .Include(i => i.Contributions).ThenInclude(c => c.Person)
        .Include(i => i.Identifiers)
        .Include(i => i.Journal)
        .Include(i => i.Series)
        .Include(i => i.ContainerParent).ThenInclude(p => p!.Contributions).ThenInclude(c => c.Person)
        .Include(i => i.ContainerParent).ThenInclude(p => p!.Series)
        .Include(i => i.Categories).ThenInclude(ic => ic.Category).ThenInclude(c => c!.Facet)
        .Include(i => i.Tags).ThenInclude(t => t.Tag)
        .Include(i => i.Attachments)
        .AsSplitQuery();
}
