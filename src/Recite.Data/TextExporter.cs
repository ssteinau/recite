using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Recite.Core.Csl;
using Recite.Core.Model;

namespace Recite.Data;

/// <summary>
/// Writes the git-friendly text projection of the library (PLAN §5): one CSL-JSON doc per
/// item plus JSON dumps of persons/journals/series/categories/projects. Stable ordering and
/// formatting so a "checkpoint" produces meaningful diffs, not per-keystroke noise. The DB
/// stays canonical; this tree is derived.
/// </summary>
public sealed class TextExporter
{
    private readonly LibraryService _lib;
    public TextExporter(LibraryService lib) => _lib = lib;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public void ExportAll()
    {
        var paths = _lib.Connection.Paths;
        paths.EnsureDirectories();
        using var ctx = _lib.CreateContext();

        ExportItems(ctx, paths);
        ExportPersons(ctx, paths);
        ExportJournals(ctx, paths);
        ExportSeries(ctx, paths);
        ExportCategories(ctx, paths);
        ExportProjects(ctx, paths);
    }

    private void ExportItems(ReciteDbContext ctx, LibraryPaths paths)
    {
        var items = ctx.Items
            .Include(i => i.Contributions).ThenInclude(c => c.Person)
            .Include(i => i.Identifiers)
            .Include(i => i.Journal)
            .Include(i => i.Series)
            .Include(i => i.ContainerParent)
            .AsSplitQuery().AsNoTracking()
            .ToList();

        var keep = new HashSet<string>();
        foreach (var item in items)
        {
            var doc = CslMapper.ToDocument(item, item.CitationKey);
            // Stamp identity + housekeeping so the file is self-describing and stable.
            doc.Root["_uuid"] = item.Uuid.ToString();
            doc.Root["_dateAdded"] = item.DateAdded.ToString("O");
            var fileName = item.Uuid + ".json";
            keep.Add(fileName);
            File.WriteAllText(Path.Combine(paths.ItemsDir, fileName), Sorted(doc.Root));
        }

        // Prune orphaned item files.
        foreach (var file in Directory.EnumerateFiles(paths.ItemsDir, "*.json"))
            if (!keep.Contains(Path.GetFileName(file)))
                File.Delete(file);
    }

    private static void ExportPersons(ReciteDbContext ctx, LibraryPaths paths)
    {
        var arr = new JsonArray();
        foreach (var p in ctx.Persons.AsNoTracking().OrderBy(p => p.UniqueKey))
            arr.Add(new JsonObject
            {
                ["uuid"] = p.Uuid.ToString(),
                ["family"] = p.Family,
                ["given"] = p.Given,
                ["nonDroppingParticle"] = p.NonDroppingParticle,
                ["suffix"] = p.Suffix,
                ["literal"] = p.Literal,
                ["key"] = p.UniqueKey,
            });
        File.WriteAllText(paths.PersonsFile, arr.ToJsonString(Json));
    }

    private static void ExportJournals(ReciteDbContext ctx, LibraryPaths paths)
    {
        var arr = new JsonArray();
        foreach (var j in ctx.Journals.AsNoTracking().OrderBy(j => j.UniqueKey))
            arr.Add(new JsonObject
            {
                ["uuid"] = j.Uuid.ToString(),
                ["name"] = j.Name,
                ["abbreviation"] = j.Abbreviation,
                ["issn"] = j.Issn,
                ["eissn"] = j.Eissn,
            });
        File.WriteAllText(paths.JournalsFile, arr.ToJsonString(Json));
    }

    private static void ExportSeries(ReciteDbContext ctx, LibraryPaths paths)
    {
        var arr = new JsonArray();
        foreach (var s in ctx.Series.AsNoTracking().OrderBy(s => s.UniqueKey))
            arr.Add(new JsonObject
            {
                ["uuid"] = s.Uuid.ToString(),
                ["name"] = s.Name,
                ["abbreviation"] = s.Abbreviation,
                ["issn"] = s.Issn,
            });
        File.WriteAllText(paths.SeriesFile, arr.ToJsonString(Json));
    }

    private static void ExportCategories(ReciteDbContext ctx, LibraryPaths paths)
    {
        var facets = ctx.Facets.Include(f => f.Categories).AsNoTracking().OrderBy(f => f.Order).ToList();
        var arr = new JsonArray();
        foreach (var f in facets)
        {
            var node = new JsonObject { ["uuid"] = f.Uuid.ToString(), ["name"] = f.Name, ["order"] = f.Order };
            var cats = new JsonArray();
            foreach (var c in f.Categories.OrderBy(c => c.ParentId).ThenBy(c => c.Order))
                cats.Add(new JsonObject
                {
                    ["uuid"] = c.Uuid.ToString(),
                    ["name"] = c.Name,
                    ["parentId"] = c.ParentId,
                    ["order"] = c.Order,
                });
            node["categories"] = cats;
            arr.Add(node);
        }
        File.WriteAllText(paths.CategoriesFile, arr.ToJsonString(Json));
    }

    private static void ExportProjects(ReciteDbContext ctx, LibraryPaths paths)
    {
        foreach (var file in Directory.EnumerateFiles(paths.ProjectsDir, "*.json"))
            File.Delete(file);

        foreach (var p in ctx.Projects.AsNoTracking().OrderBy(p => p.Name))
        {
            var members = ctx.ProjectMembers
                .Where(m => m.ProjectId == p.Id)
                .Join(ctx.Items, m => m.ItemId, i => i.Id, (m, i) => new { i.Uuid, m.KeyOverride, m.Order })
                .AsNoTracking().ToList();

            var memberArr = new JsonArray();
            foreach (var m in members.OrderBy(m => m.Order))
                memberArr.Add(new JsonObject { ["item"] = m.Uuid.ToString(), ["keyOverride"] = m.KeyOverride });

            var obj = new JsonObject
            {
                ["uuid"] = p.Uuid.ToString(),
                ["name"] = p.Name,
                ["keyScheme"] = p.KeyScheme,
                ["projections"] = JsonNode.Parse(p.ProjectionsJson),
                ["members"] = memberArr,
            };
            var safe = string.Join("_", p.Name.Split(Path.GetInvalidFileNameChars()));
            File.WriteAllText(Path.Combine(paths.ProjectsDir, safe + ".json"), obj.ToJsonString(Json));
        }
    }

    /// <summary>Serialize a JSON object with keys sorted for stable, diff-friendly output.</summary>
    private static string Sorted(JsonObject obj)
    {
        var sorted = new JsonObject();
        foreach (var kv in obj.OrderBy(k => k.Key, StringComparer.Ordinal))
            sorted[kv.Key] = kv.Value?.DeepClone();
        return sorted.ToJsonString(Json);
    }
}
