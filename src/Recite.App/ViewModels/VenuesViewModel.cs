using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Recite.App.Services;

namespace Recite.App.ViewModels;

public sealed class JournalRow
{
    public Guid Uuid { get; init; }
    public string Name { get; init; } = "";
    public string? Abbreviation { get; init; }
    public string? Issn { get; init; }
    public int Count { get; init; }
}

public sealed class SeriesRow
{
    public string Name { get; init; } = "";
    public string? Abbreviation { get; init; }
    public int Count { get; init; }
}

public sealed class TagRow
{
    public string Name { get; init; } = "";
    public int Count { get; init; }
}

public sealed partial class VenuesViewModel : ViewModelBase, IRefreshable
{
    private readonly AppServices _services;
    private readonly Action<string> _status;

    public ObservableCollection<JournalRow> Journals { get; } = new();
    public ObservableCollection<SeriesRow> Series { get; } = new();
    public ObservableCollection<TagRow> Tags { get; } = new();

    [ObservableProperty] private JournalRow? _selectedJournal;
    [ObservableProperty] private string _editName = "";
    [ObservableProperty] private string _editAbbreviation = "";
    [ObservableProperty] private string _editIssn = "";

    public VenuesViewModel(AppServices services, Action<string> status)
    {
        _services = services;
        _status = status;
    }

    public void Refresh() => Load();

    partial void OnSelectedJournalChanged(JournalRow? value)
    {
        EditName = value?.Name ?? "";
        EditAbbreviation = value?.Abbreviation ?? "";
        EditIssn = value?.Issn ?? "";
    }

    private void Load()
    {
        var selUuid = SelectedJournal?.Uuid;
        Journals.Clear();
        foreach (var (j, c) in _services.Library.GetJournals())
            Journals.Add(new JournalRow { Uuid = j.Uuid, Name = j.Name, Abbreviation = j.Abbreviation, Issn = j.Issn, Count = c });
        Series.Clear();
        foreach (var (s, c) in _services.Library.GetSeries())
            Series.Add(new SeriesRow { Name = s.Name, Abbreviation = s.Abbreviation, Count = c });
        Tags.Clear();
        foreach (var (t, c) in _services.Library.GetTags())
            Tags.Add(new TagRow { Name = t.Name, Count = c });

        if (selUuid is Guid u)
            SelectedJournal = System.Linq.Enumerable.FirstOrDefault(Journals, j => j.Uuid == u);
    }

    [RelayCommand]
    private void SaveJournal()
    {
        if (SelectedJournal is null || string.IsNullOrWhiteSpace(EditName)) return;
        _services.Library.RenameJournal(SelectedJournal.Uuid, EditName.Trim(),
            string.IsNullOrWhiteSpace(EditAbbreviation) ? null : EditAbbreviation.Trim(),
            string.IsNullOrWhiteSpace(EditIssn) ? null : EditIssn.Trim());
        Load();
        _status("Journal updated.");
    }
}
