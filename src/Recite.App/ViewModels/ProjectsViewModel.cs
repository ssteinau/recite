using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Recite.App.Services;
using Recite.Core.Projections;

namespace Recite.App.ViewModels;

public sealed class ProjectRow
{
    public Guid Uuid { get; init; }
    public string Name { get; init; } = "";
}

public sealed partial class AbbrevRow : ObservableObject
{
    [ObservableProperty] private string _match = "";
    [ObservableProperty] private string _abbreviation = "";
}

public sealed partial class SubRow : ObservableObject
{
    [ObservableProperty] private string _field = "*";
    [ObservableProperty] private string _pattern = "";
    [ObservableProperty] private string _replacement = "";
    [ObservableProperty] private bool _isRegex;
}

public sealed partial class ProjectsViewModel : ViewModelBase, IRefreshable
{
    private readonly AppServices _services;
    private readonly Action<string> _status;

    public ObservableCollection<ProjectRow> Projects { get; } = new();
    public ObservableCollection<ItemRow> Members { get; } = new();
    public ObservableCollection<ItemRow> SearchResults { get; } = new();
    public ObservableCollection<AbbrevRow> JournalAbbrevs { get; } = new();
    public ObservableCollection<AbbrevRow> SeriesAbbrevs { get; } = new();
    public ObservableCollection<SubRow> Substitutions { get; } = new();

    [ObservableProperty] private ProjectRow? _selectedProject;
    [ObservableProperty] private string _newProjectName = "";
    [ObservableProperty] private string _keyScheme = "";
    [ObservableProperty] private string _memberSearch = "";
    [ObservableProperty] private ItemRow? _selectedMember;
    [ObservableProperty] private ItemRow? _selectedSearchResult;
    [ObservableProperty] private string _exportPreview = "";
    [ObservableProperty] private bool _hasSelection;

    public ProjectsViewModel(AppServices services, Action<string> status)
    {
        _services = services;
        _status = status;
    }

    public void Refresh() => LoadProjects();

    private void LoadProjects()
    {
        var sel = SelectedProject?.Uuid;
        Projects.Clear();
        foreach (var p in _services.Projects.GetProjects())
            Projects.Add(new ProjectRow { Uuid = p.Uuid, Name = p.Name });
        if (sel is Guid u) SelectedProject = Projects.FirstOrDefault(p => p.Uuid == u);
        else if (SelectedProject is null) SelectedProject = Projects.FirstOrDefault();
    }

    partial void OnSelectedProjectChanged(ProjectRow? value)
    {
        HasSelection = value is not null;
        if (value is null) { Members.Clear(); return; }

        var project = _services.Projects.GetProject(value.Uuid)!;
        KeyScheme = project.KeyScheme;

        LoadMembers();

        var set = _services.Projects.GetProjections(value.Uuid);
        JournalAbbrevs.Clear();
        foreach (var a in set.JournalAbbreviations) JournalAbbrevs.Add(new AbbrevRow { Match = a.Match, Abbreviation = a.Abbreviation });
        SeriesAbbrevs.Clear();
        foreach (var a in set.SeriesAbbreviations) SeriesAbbrevs.Add(new AbbrevRow { Match = a.Match, Abbreviation = a.Abbreviation });
        Substitutions.Clear();
        foreach (var s in set.Substitutions) Substitutions.Add(new SubRow { Field = s.Field, Pattern = s.Pattern, Replacement = s.Replacement, IsRegex = s.IsRegex });

        ExportPreview = "";
    }

    private void LoadMembers()
    {
        Members.Clear();
        if (SelectedProject is null) return;
        var members = _services.Projects.GetMembers(SelectedProject.Uuid);
        var items = _services.Library.GetItems(
            members.Select(m => _services.Library.GetItem(m.ItemUuid))
                   .Where(i => i is not null)
                   .Select(i => i!.Id).ToList());
        // Preserve curated order.
        var order = members.Select((m, idx) => (m.ItemUuid, idx)).ToDictionary(x => x.ItemUuid, x => x.idx);
        foreach (var row in items.Select(ItemRow.From).OrderBy(r => order.GetValueOrDefault(r.Uuid, int.MaxValue)))
            Members.Add(row);
    }

    // ---- projects -----------------------------------------------------------

    [RelayCommand]
    private void CreateProject()
    {
        if (string.IsNullOrWhiteSpace(NewProjectName)) return;
        var uuid = _services.Projects.CreateProject(NewProjectName.Trim());
        NewProjectName = "";
        LoadProjects();
        SelectedProject = Projects.FirstOrDefault(p => p.Uuid == uuid);
        _status("Project created.");
    }

    [RelayCommand]
    private void DeleteProject()
    {
        if (SelectedProject is null) return;
        _services.Projects.DeleteProject(SelectedProject.Uuid);
        SelectedProject = null;
        LoadProjects();
        _status("Project deleted.");
    }

    [RelayCommand]
    private void SaveKeyScheme()
    {
        if (SelectedProject is null) return;
        _services.Projects.SetKeyScheme(SelectedProject.Uuid, KeyScheme);
        _status("Key scheme saved.");
    }

    // ---- membership ---------------------------------------------------------

    partial void OnMemberSearchChanged(string value)
    {
        SearchResults.Clear();
        if (string.IsNullOrWhiteSpace(value)) return;
        var ids = _services.Library.SearchIds(value, 25);
        foreach (var row in _services.Library.GetItems(ids).Select(ItemRow.From))
            SearchResults.Add(row);
    }

    [RelayCommand]
    private void AddMember()
    {
        if (SelectedProject is null || SelectedSearchResult is null) return;
        _services.Projects.AddMembers(SelectedProject.Uuid, new[] { SelectedSearchResult.Uuid });
        LoadMembers();
        _status("Added to project.");
    }

    [RelayCommand]
    private void RemoveMember()
    {
        if (SelectedProject is null || SelectedMember is null) return;
        _services.Projects.RemoveMember(SelectedProject.Uuid, SelectedMember.Uuid);
        LoadMembers();
        _status("Removed from project.");
    }

    // ---- projections --------------------------------------------------------

    [RelayCommand] private void AddJournalAbbrev() => JournalAbbrevs.Add(new AbbrevRow());
    [RelayCommand] private void AddSeriesAbbrev() => SeriesAbbrevs.Add(new AbbrevRow());
    [RelayCommand] private void AddSubstitution() => Substitutions.Add(new SubRow());
    [RelayCommand] private void RemoveJournalAbbrev(AbbrevRow? r) { if (r is not null) JournalAbbrevs.Remove(r); }
    [RelayCommand] private void RemoveSeriesAbbrev(AbbrevRow? r) { if (r is not null) SeriesAbbrevs.Remove(r); }
    [RelayCommand] private void RemoveSubstitution(SubRow? r) { if (r is not null) Substitutions.Remove(r); }

    [RelayCommand]
    private void SaveProjections()
    {
        if (SelectedProject is null) return;
        var set = new ProjectionSet();
        set.JournalAbbreviations.AddRange(JournalAbbrevs
            .Where(a => !string.IsNullOrWhiteSpace(a.Match))
            .Select(a => new AbbreviationRule { Match = a.Match.Trim(), Abbreviation = a.Abbreviation.Trim() }));
        set.SeriesAbbreviations.AddRange(SeriesAbbrevs
            .Where(a => !string.IsNullOrWhiteSpace(a.Match))
            .Select(a => new AbbreviationRule { Match = a.Match.Trim(), Abbreviation = a.Abbreviation.Trim() }));
        set.Substitutions.AddRange(Substitutions
            .Where(s => !string.IsNullOrWhiteSpace(s.Pattern))
            .Select(s => new StringSubstitution { Field = string.IsNullOrWhiteSpace(s.Field) ? "*" : s.Field.Trim(), Pattern = s.Pattern, Replacement = s.Replacement, IsRegex = s.IsRegex }));
        _services.Projects.SetProjections(SelectedProject.Uuid, set);
        _status("Projections saved.");
    }

    // ---- export -------------------------------------------------------------

    [RelayCommand]
    private void PreviewExport()
    {
        if (SelectedProject is null) return;
        SaveProjections();
        _services.Projects.SetKeyScheme(SelectedProject.Uuid, KeyScheme);
        ExportPreview = _services.Export.ExportProject(SelectedProject.Uuid, "biblatex");
        _status("Preview generated.");
    }

    [RelayCommand]
    private async Task ExportBib()
    {
        if (SelectedProject is null) return;
        SaveProjections();
        _services.Projects.SetKeyScheme(SelectedProject.Uuid, KeyScheme);
        var path = await Dialogs.SaveFileAsync("Export project bibliography", SelectedProject.Name, "bib");
        if (path is null) return;
        File.WriteAllText(path, _services.Export.ExportProject(SelectedProject.Uuid, "biblatex"));
        _status($"Exported {Members.Count} references to {Path.GetFileName(path)}.");
    }
}
