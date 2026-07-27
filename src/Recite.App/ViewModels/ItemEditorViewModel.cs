using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Recite.App.Services;
using Recite.Core.Csl;
using Recite.Core.Model;
using Recite.Data;

namespace Recite.App.ViewModels;

public sealed partial class CategoryChoice : ObservableObject
{
    public Guid Uuid { get; init; }
    public string Label { get; init; } = "";
    [ObservableProperty] private bool _isSelected;
}

public sealed partial class AttachmentRow : ObservableObject
{
    [ObservableProperty] private string _relativePath = "";
    [ObservableProperty] private string? _title;
}

public sealed partial class ContainerChoice
{
    public Guid? Uuid { get; init; }
    public string Label { get; init; } = "";
}

public sealed partial class ItemEditorViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private readonly Guid? _uuid;
    private readonly Action<Guid> _onSaved;
    private readonly Action<Guid> _onDeleted;

    public bool IsNew => _uuid is null;
    public string HeaderText => IsNew ? "New reference" : "Edit reference";

    public IReadOnlyList<string> Types { get; } = CslType.All;

    [ObservableProperty] private string _type = CslType.ArticleJournal;
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _authorsText = "";
    [ObservableProperty] private string _editorsText = "";
    [ObservableProperty] private string _year = "";
    [ObservableProperty] private string _journalName = "";
    [ObservableProperty] private string _seriesName = "";
    [ObservableProperty] private string _volume = "";
    [ObservableProperty] private string _issue = "";
    [ObservableProperty] private string _pages = "";
    [ObservableProperty] private string _publisher = "";
    [ObservableProperty] private string _place = "";
    [ObservableProperty] private string _edition = "";
    [ObservableProperty] private string _doi = "";
    [ObservableProperty] private string _isbn = "";
    [ObservableProperty] private string _url = "";
    [ObservableProperty] private string _abstract = "";
    [ObservableProperty] private string _note = "";
    [ObservableProperty] private string _tagsText = "";
    [ObservableProperty] private string _citationKeyOverride = "";
    [ObservableProperty] private string _generatedKey = "";
    [ObservableProperty] private ContainerChoice? _containerParent;

    public ObservableCollection<CategoryChoice> Categories { get; } = new();
    public ObservableCollection<AttachmentRow> Attachments { get; } = new();
    public ObservableCollection<ContainerChoice> Containers { get; } = new();

    private ItemEditorViewModel(AppServices services, Guid? uuid, Action<Guid> onSaved, Action<Guid> onDeleted)
    {
        _services = services;
        _uuid = uuid;
        _onSaved = onSaved;
        _onDeleted = onDeleted;
        LoadCategories();
        LoadContainers();
    }

    public static ItemEditorViewModel ForNew(AppServices services, Action<Guid> onSaved, Action<Guid> onDeleted)
        => new(services, null, onSaved, onDeleted);

    public static ItemEditorViewModel ForExisting(AppServices services, Item item, Action<Guid> onSaved, Action<Guid> onDeleted)
    {
        var vm = new ItemEditorViewModel(services, item.Uuid, onSaved, onDeleted);
        vm.LoadFrom(item);
        return vm;
    }

    private void LoadCategories()
    {
        foreach (var facet in _services.Library.GetFacets())
            foreach (var cat in facet.Categories.OrderBy(c => c.Name))
                Categories.Add(new CategoryChoice { Uuid = cat.Uuid, Label = $"{facet.Name} ▸ {cat.Name}" });
    }

    private void LoadContainers()
    {
        Containers.Add(new ContainerChoice { Uuid = null, Label = "(none)" });
        foreach (var (uuid, label) in _services.Library.GetContainers())
            Containers.Add(new ContainerChoice { Uuid = uuid, Label = label });
        ContainerParent = Containers[0];
    }

    private void LoadFrom(Item item)
    {
        Type = item.Type;
        Title = item.Title ?? "";
        Year = item.Year?.ToString() ?? "";
        var bag = item.FieldBag();
        string F(string k) => bag.TryGetPropertyValue(k, out var n) && n is not null ? n.ToString() : "";
        Volume = F("volume"); Issue = F("issue"); Pages = F("page");
        Publisher = F("publisher"); Place = F("publisher-place"); Edition = F("edition");
        Abstract = F("abstract"); Note = F("note");

        JournalName = item.Journal?.Name ?? (Type is CslType.ArticleJournal ? F("container-title") : "");
        if (string.IsNullOrEmpty(JournalName) && !CslMapper.IsJournalType(item.Type)) JournalName = F("container-title");
        SeriesName = item.Series?.Name ?? "";

        AuthorsText = string.Join("\n", item.Contributions
            .Where(c => c.Role == ContributorRole.Author).OrderBy(c => c.Order)
            .Select(c => c.Person!.ToCslName().DisplayName()));
        EditorsText = string.Join("\n", item.Contributions
            .Where(c => c.Role == ContributorRole.Editor).OrderBy(c => c.Order)
            .Select(c => c.Person!.ToCslName().DisplayName()));

        Doi = item.Identifiers.FirstOrDefault(i => i.Scheme == ExternalIdentifier.Schemes.Doi)?.Value ?? "";
        Isbn = item.Identifiers.FirstOrDefault(i => i.Scheme == ExternalIdentifier.Schemes.Isbn)?.Value ?? "";
        Url = item.Identifiers.FirstOrDefault(i => i.Scheme == ExternalIdentifier.Schemes.Url)?.Value ?? "";

        TagsText = string.Join(", ", item.Tags.Select(t => t.Tag!.Name));
        GeneratedKey = item.CitationKey ?? "";

        var owned = item.Categories.Select(ic => ic.Category!.Uuid).ToHashSet();
        foreach (var c in Categories) c.IsSelected = owned.Contains(c.Uuid);

        foreach (var a in item.Attachments.OrderBy(a => a.Order))
            Attachments.Add(new AttachmentRow { RelativePath = a.RelativePath, Title = a.Title });

        if (item.ContainerParent is not null)
            ContainerParent = Containers.FirstOrDefault(c => c.Uuid == item.ContainerParent.Uuid) ?? Containers[0];
    }

    private CslDocument BuildDocument()
    {
        var doc = new CslDocument { Type = Type };
        void S(string field, string value) => doc.SetString(field, value?.Trim() ?? "");
        S("title", Title);
        if (int.TryParse(Year.Trim(), out var y)) doc.SetDate("issued", CslDate.FromYear(y));
        S("volume", Volume); S("issue", Issue); S("page", Pages);
        S("publisher", Publisher); S("publisher-place", Place); S("edition", Edition);
        S("abstract", Abstract); S("note", Note);
        if (!string.IsNullOrWhiteSpace(JournalName)) doc.ContainerTitle = JournalName.Trim();
        if (!string.IsNullOrWhiteSpace(SeriesName)) doc.CollectionTitle = SeriesName.Trim();
        S("DOI", Doi); S("ISBN", Isbn); S("URL", Url);

        doc.SetNames("author", ParseNames(AuthorsText));
        doc.SetNames("editor", ParseNames(EditorsText));
        return doc;
    }

    private static IEnumerable<CslName> ParseNames(string text) =>
        text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => CslName.Parse(line.Trim()))
            .Where(n => !n.IsEmpty);

    [RelayCommand]
    private void Save()
    {
        var edit = new ItemEdit
        {
            Uuid = _uuid,
            Document = BuildDocument(),
            CategoryUuids = Categories.Where(c => c.IsSelected).Select(c => c.Uuid).ToList(),
            Tags = TagsText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
            Attachments = Attachments.Select(a => new AttachmentEdit { RelativePath = a.RelativePath, Title = a.Title }).ToList(),
            ContainerParentUuid = ContainerParent?.Uuid,
            CitationKeyOverride = string.IsNullOrWhiteSpace(CitationKeyOverride) ? null : CitationKeyOverride.Trim(),
        };
        var uuid = _services.Library.Upsert(edit);
        _onSaved(uuid);
    }

    [RelayCommand]
    private void Delete()
    {
        if (_uuid is Guid u)
        {
            _services.Library.DeleteItem(u);
            _onDeleted(u);
        }
    }

    [RelayCommand]
    private async Task AddAttachment()
    {
        var path = await Dialogs.OpenFileAsync("Attach file", ("PDF", new[] { "*.pdf" }), ("All files", new[] { "*" }));
        if (path is null) return;

        // Copy into the library files/ folder and store the relative path.
        var filesDir = _services.Connection.Paths.FilesDir;
        Directory.CreateDirectory(filesDir);
        var name = Path.GetFileName(path);
        var dest = Path.Combine(filesDir, name);
        int n = 1;
        while (File.Exists(dest) && !SameFile(path, dest))
            dest = Path.Combine(filesDir, $"{Path.GetFileNameWithoutExtension(name)}-{n++}{Path.GetExtension(name)}");
        if (!File.Exists(dest)) File.Copy(path, dest);
        Attachments.Add(new AttachmentRow { RelativePath = Path.GetFileName(dest), Title = name });
    }

    private static bool SameFile(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    [RelayCommand]
    private void RemoveAttachment(AttachmentRow? row)
    {
        if (row is not null) Attachments.Remove(row);
    }

    [RelayCommand]
    private void OpenAttachment(AttachmentRow? row)
    {
        if (row is null) return;
        _services.OpenExternally(_services.ResolveAttachmentPath(row.RelativePath));
    }
}
