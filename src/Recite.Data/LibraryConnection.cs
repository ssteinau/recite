using Microsoft.EntityFrameworkCore;

namespace Recite.Data;

/// <summary>
/// An opened library: holds the resolved paths and hands out fresh <see cref="ReciteDbContext"/>
/// instances (contexts are not thread-safe, so each unit of work gets its own).
/// </summary>
public sealed class LibraryConnection
{
    /// <summary>Bump when the relational schema changes incompatibly (PLAN §11 guard).</summary>
    public const int SchemaVersion = 1;

    public LibraryPaths Paths { get; }
    private readonly DbContextOptions<ReciteDbContext> _options;

    internal LibraryConnection(LibraryPaths paths, DbContextOptions<ReciteDbContext> options)
    {
        Paths = paths;
        _options = options;
    }

    public ReciteDbContext CreateContext() => new(_options);
}
