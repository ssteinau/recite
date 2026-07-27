using Microsoft.EntityFrameworkCore;
using Recite.Core.Citations;
using Recite.Core.Model;
using Recite.Core.Projections;

namespace Recite.Data;

/// <summary>
/// Manages projects: their explicit, curated membership (independent of categories, PLAN §3),
/// citation-key scheme, and projection stack.
/// </summary>
public sealed class ProjectService
{
    private readonly LibraryService _lib;
    public ProjectService(LibraryService lib) => _lib = lib;

    public IReadOnlyList<Project> GetProjects()
    {
        using var ctx = _lib.CreateContext();
        return ctx.Projects.OrderBy(p => p.Name).AsNoTracking().ToList();
    }

    public Project? GetProject(Guid uuid)
    {
        using var ctx = _lib.CreateContext();
        return ctx.Projects.AsNoTracking().FirstOrDefault(p => p.Uuid == uuid);
    }

    public Guid CreateProject(string name)
    {
        using var ctx = _lib.CreateContext();
        var project = new Project
        {
            Name = name.Trim(),
            KeyScheme = CitationKeyScheme.Default,
            ProjectionsJson = new ProjectionSet().ToJson(),
            DateCreated = DateTimeOffset.UtcNow,
        };
        ctx.Projects.Add(project);
        ctx.SaveChanges();
        return project.Uuid;
    }

    public void RenameProject(Guid uuid, string name)
    {
        using var ctx = _lib.CreateContext();
        var p = ctx.Projects.First(x => x.Uuid == uuid);
        p.Name = name.Trim();
        ctx.SaveChanges();
    }

    public void DeleteProject(Guid uuid)
    {
        using var ctx = _lib.CreateContext();
        var p = ctx.Projects.First(x => x.Uuid == uuid);
        ctx.Projects.Remove(p);
        ctx.SaveChanges();
    }

    public void SetKeyScheme(Guid uuid, string scheme)
    {
        using var ctx = _lib.CreateContext();
        var p = ctx.Projects.First(x => x.Uuid == uuid);
        p.KeyScheme = string.IsNullOrWhiteSpace(scheme) ? CitationKeyScheme.Default : scheme.Trim();
        ctx.SaveChanges();
    }

    public ProjectionSet GetProjections(Guid uuid)
    {
        using var ctx = _lib.CreateContext();
        var p = ctx.Projects.First(x => x.Uuid == uuid);
        return ProjectionSet.FromJson(p.ProjectionsJson);
    }

    public void SetProjections(Guid uuid, ProjectionSet set)
    {
        using var ctx = _lib.CreateContext();
        var p = ctx.Projects.First(x => x.Uuid == uuid);
        p.ProjectionsJson = set.ToJson();
        ctx.SaveChanges();
    }

    // ---- membership ---------------------------------------------------------

    public void AddMembers(Guid projectUuid, IEnumerable<Guid> itemUuids)
    {
        using var ctx = _lib.CreateContext();
        var project = ctx.Projects.First(p => p.Uuid == projectUuid);
        var existing = ctx.ProjectMembers.Where(m => m.ProjectId == project.Id).Select(m => m.ItemId).ToHashSet();
        int order = (ctx.ProjectMembers.Where(m => m.ProjectId == project.Id).Max(m => (int?)m.Order) ?? -1) + 1;
        foreach (var iu in itemUuids.Distinct())
        {
            var item = ctx.Items.FirstOrDefault(i => i.Uuid == iu);
            if (item is null || existing.Contains(item.Id)) continue;
            ctx.ProjectMembers.Add(new ProjectMember { ProjectId = project.Id, ItemId = item.Id, Order = order++ });
            existing.Add(item.Id);
        }
        ctx.SaveChanges();
    }

    public void RemoveMember(Guid projectUuid, Guid itemUuid)
    {
        using var ctx = _lib.CreateContext();
        var project = ctx.Projects.First(p => p.Uuid == projectUuid);
        var item = ctx.Items.FirstOrDefault(i => i.Uuid == itemUuid);
        if (item is null) return;
        var m = ctx.ProjectMembers.FirstOrDefault(x => x.ProjectId == project.Id && x.ItemId == item.Id);
        if (m is not null) { ctx.ProjectMembers.Remove(m); ctx.SaveChanges(); }
    }

    public void SetKeyOverride(Guid projectUuid, Guid itemUuid, string? keyOverride)
    {
        using var ctx = _lib.CreateContext();
        var project = ctx.Projects.First(p => p.Uuid == projectUuid);
        var item = ctx.Items.First(i => i.Uuid == itemUuid);
        var m = ctx.ProjectMembers.First(x => x.ProjectId == project.Id && x.ItemId == item.Id);
        m.KeyOverride = string.IsNullOrWhiteSpace(keyOverride) ? null : keyOverride.Trim();
        ctx.SaveChanges();
    }

    /// <summary>Members in curated order, with the item's UUID for the UI.</summary>
    public IReadOnlyList<(Guid ItemUuid, string? KeyOverride, int Order)> GetMembers(Guid projectUuid)
    {
        using var ctx = _lib.CreateContext();
        var project = ctx.Projects.First(p => p.Uuid == projectUuid);
        return ctx.ProjectMembers
            .Where(m => m.ProjectId == project.Id)
            .OrderBy(m => m.Order)
            .Join(ctx.Items, m => m.ItemId, i => i.Id, (m, i) => new { i.Uuid, m.KeyOverride, m.Order })
            .AsNoTracking().ToList()
            .Select(x => (x.Uuid, x.KeyOverride, x.Order))
            .ToList();
    }
}
