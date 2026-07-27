using Recite.Data;

namespace Recite.Tests;

/// <summary>Creates a throwaway file-based library (real migrations + FTS) and cleans it up.</summary>
public sealed class TempLibrary : IDisposable
{
    public string Root { get; }
    public LibraryConnection Connection { get; private set; }
    public LibraryService Library { get; private set; }

    public TempLibrary()
    {
        Root = Path.Combine(Path.GetTempPath(), "recite-test-" + Guid.NewGuid().ToString("N"));
        Connection = LibraryInitializer.Open(Root);
        Library = new LibraryService(Connection);
    }

    /// <summary>Re-open the same folder to prove persistence across sessions.</summary>
    public LibraryService Reopen()
    {
        Connection = LibraryInitializer.Open(Root);
        Library = new LibraryService(Connection);
        return Library;
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
        catch { /* best effort */ }
    }
}
