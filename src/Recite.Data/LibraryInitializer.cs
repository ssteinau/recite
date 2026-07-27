using Microsoft.EntityFrameworkCore;

namespace Recite.Data;

/// <summary>
/// Opens or creates a library folder: migrates the schema, enables WAL, provisions the
/// FTS5 index, and enforces the schema-version guard (older app refuses a newer library).
/// </summary>
public static class LibraryInitializer
{
    public sealed class SchemaTooNewException(int found, int supported)
        : Exception($"Library schema version {found} is newer than this build supports ({supported}). Please update Recite.")
    {
        public int Found { get; } = found;
        public int Supported { get; } = supported;
    }

    public static LibraryConnection Open(string root) => Open(new LibraryPaths(root));

    public static LibraryConnection Open(LibraryPaths paths)
    {
        paths.EnsureDirectories();
        GuardSchemaVersion(paths);

        var options = new DbContextOptionsBuilder<ReciteDbContext>()
            .UseSqlite(paths.ConnectionString)
            .Options;

        using (var ctx = new ReciteDbContext(options))
        {
            ctx.Database.Migrate();
            ctx.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
            ctx.Database.ExecuteSqlRaw("PRAGMA foreign_keys=ON;");
            CreateFts(ctx);
        }

        WriteSchemaVersion(paths);
        return new LibraryConnection(paths, options);
    }

    /// <summary>Open an in-memory / test connection against an existing open connection string.</summary>
    public static LibraryConnection OpenSqlite(string connectionString, LibraryPaths? paths = null)
    {
        var options = new DbContextOptionsBuilder<ReciteDbContext>()
            .UseSqlite(connectionString)
            .Options;
        using (var ctx = new ReciteDbContext(options))
        {
            ctx.Database.EnsureCreated();
            CreateFts(ctx);
        }
        return new LibraryConnection(paths ?? new LibraryPaths(Path.GetTempPath()), options);
    }

    private static void CreateFts(ReciteDbContext ctx)
    {
        // Standalone FTS5 table keyed by Item.Id (rowid). Maintained by LibraryService.
        ctx.Database.ExecuteSqlRaw(
            "CREATE VIRTUAL TABLE IF NOT EXISTS item_fts USING fts5(" +
            "title, authors, container, abstract, tags, tokenize='unicode61 remove_diacritics 2');");
    }

    private static void GuardSchemaVersion(LibraryPaths paths)
    {
        if (!File.Exists(paths.SchemaVersionFile)) return;
        var text = File.ReadAllText(paths.SchemaVersionFile).Trim();
        if (int.TryParse(text, out var found) && found > LibraryConnection.SchemaVersion)
            throw new SchemaTooNewException(found, LibraryConnection.SchemaVersion);
    }

    private static void WriteSchemaVersion(LibraryPaths paths) =>
        File.WriteAllText(paths.SchemaVersionFile, LibraryConnection.SchemaVersion.ToString());
}
