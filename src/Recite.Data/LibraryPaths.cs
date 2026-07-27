namespace Recite.Data;

/// <summary>
/// Resolves the on-disk layout of a library folder (PLAN §5). One library = one folder.
/// </summary>
public sealed class LibraryPaths
{
    public string Root { get; }

    public LibraryPaths(string root) => Root = Path.GetFullPath(root);

    public string Database => Path.Combine(Root, "recite.sqlite");
    public string SchemaVersionFile => Path.Combine(Root, "schema-version");
    public string ItemsDir => Path.Combine(Root, "items");
    public string ProjectsDir => Path.Combine(Root, "projects");
    public string FilesDir => Path.Combine(Root, "files");
    public string PersonsFile => Path.Combine(Root, "persons.json");
    public string JournalsFile => Path.Combine(Root, "journals.json");
    public string SeriesFile => Path.Combine(Root, "series.json");
    public string CategoriesFile => Path.Combine(Root, "categories.json");

    public string ConnectionString => $"Data Source={Database}";

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(ItemsDir);
        Directory.CreateDirectory(ProjectsDir);
        Directory.CreateDirectory(FilesDir);
    }

    /// <summary>Default library location under the user's app data.</summary>
    public static string DefaultRoot
    {
        get
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrWhiteSpace(appData))
                appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
            return Path.Combine(appData, "Recite", "Library");
        }
    }
}
