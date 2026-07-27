using Microsoft.EntityFrameworkCore;
using Recite.Core.Citations;
using Recite.Core.Csl;
using Recite.Core.Model;
using Recite.Core.Projections;

namespace Recite.Data;

/// <summary>
/// Renders references to a text format. Library export uses each item's stored default key
/// and no projections; project export is the repeatable deliverable (PLAN §3/§7): it applies
/// the project's key scheme and projection stack via the pure <see cref="ProjectionEngine"/>.
/// </summary>
public sealed class ExportService
{
    private readonly LibraryService _lib;

    public ExportService(LibraryService lib) => _lib = lib;

    /// <summary>Export specific items (or the whole library) using stored default keys.</summary>
    public string ExportItems(IReadOnlyCollection<Guid>? uuids, string formatId)
    {
        var writer = _lib.Formats.Writer(formatId)
            ?? throw new ArgumentException($"Unknown format '{formatId}'", nameof(formatId));

        using var ctx = _lib.CreateContext();
        var items = FullQuery(ctx)
            .Where(i => uuids == null || uuids.Contains(i.Uuid))
            .ToList()
            .OrderBy(i => i.CitationKey ?? i.Uuid.ToString(), StringComparer.Ordinal)
            .ToList();

        var docs = items.Select(i => CslMapper.ToDocument(i, i.CitationKey)).ToList();
        return writer.Write(docs);
    }

    /// <summary>Export a project's bibliography — deterministic, projection-applied.</summary>
    public string ExportProject(Guid projectUuid, string formatId)
    {
        var writer = _lib.Formats.Writer(formatId)
            ?? throw new ArgumentException($"Unknown format '{formatId}'", nameof(formatId));

        using var ctx = _lib.CreateContext();
        var project = ctx.Projects.First(p => p.Uuid == projectUuid);
        var set = ProjectionSet.FromJson(project.ProjectionsJson);
        var scheme = new CitationKeyScheme(project.KeyScheme);

        var members = ctx.ProjectMembers
            .Where(m => m.ProjectId == project.Id)
            .OrderBy(m => m.Order)
            .ToList();

        var itemIds = members.Select(m => m.ItemId).ToHashSet();
        var items = FullQuery(ctx).Where(i => itemIds.Contains(i.Id)).ToList()
            .ToDictionary(i => i.Id, i => i);

        var takenKeys = new HashSet<string>(StringComparer.Ordinal);
        var docs = new List<CslDocument>();
        foreach (var m in members)
        {
            if (!items.TryGetValue(m.ItemId, out var item)) continue;
            var canonical = CslMapper.ToDocument(item);

            var baseKey = string.IsNullOrWhiteSpace(m.KeyOverride)
                ? scheme.BaseKey(canonical)
                : m.KeyOverride!;
            var key = CitationKeyScheme.Disambiguate(baseKey, takenKeys.Contains);
            takenKeys.Add(key);

            var projected = ProjectionEngine.Project(canonical, set, item.Uuid);
            projected.Id = key;
            docs.Add(projected);
        }

        return writer.Write(docs);
    }

    private static IQueryable<Item> FullQuery(ReciteDbContext ctx) => ctx.Items
        .Include(i => i.Contributions).ThenInclude(c => c.Person)
        .Include(i => i.Identifiers)
        .Include(i => i.Journal)
        .Include(i => i.Series)
        .Include(i => i.ContainerParent).ThenInclude(p => p!.Contributions).ThenInclude(c => c.Person)
        .Include(i => i.ContainerParent).ThenInclude(p => p!.Series)
        .AsSplitQuery();
}
