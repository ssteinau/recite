using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Recite.App.Services;
using Recite.Core.Model;

namespace Recite.App.ViewModels;

public sealed class PersonRow
{
    public Guid Uuid { get; init; }
    public string Name { get; init; } = "";
    public int Count { get; init; }
    public string CountLabel => $"{Count} ref{(Count == 1 ? "" : "s")}";
}

public sealed partial class PersonsViewModel : ViewModelBase, IRefreshable
{
    private readonly AppServices _services;
    private readonly Action<string> _status;

    public ObservableCollection<PersonRow> Persons { get; } = new();

    [ObservableProperty] private PersonRow? _selected;
    [ObservableProperty] private string _family = "";
    [ObservableProperty] private string _given = "";
    [ObservableProperty] private string _suffix = "";
    [ObservableProperty] private string _literal = "";
    [ObservableProperty] private string _filter = "";

    public PersonsViewModel(AppServices services, Action<string> status)
    {
        _services = services;
        _status = status;
    }

    public void Refresh() => Load();

    partial void OnFilterChanged(string value) => Load();

    partial void OnSelectedChanged(PersonRow? value)
    {
        if (value is null) return;
        var p = _services.Library.GetPerson(value.Uuid);
        if (p is null) return;
        Family = p.Family ?? ""; Given = p.Given ?? ""; Suffix = p.Suffix ?? ""; Literal = p.Literal ?? "";
    }

    private void Load()
    {
        var selectedUuid = Selected?.Uuid;
        Persons.Clear();
        var rows = _services.Library.GetPersons()
            .Select(x => new PersonRow { Uuid = x.Person.Uuid, Name = x.Person.DisplayName(), Count = x.Count });
        if (!string.IsNullOrWhiteSpace(Filter))
            rows = rows.Where(r => r.Name.Contains(Filter, StringComparison.OrdinalIgnoreCase));
        foreach (var r in rows) Persons.Add(r);
        if (selectedUuid is Guid u) Selected = Persons.FirstOrDefault(p => p.Uuid == u);
    }

    [RelayCommand]
    private void Save()
    {
        if (Selected is null) return;
        var edited = new Person
        {
            Family = NullIfBlank(Family),
            Given = NullIfBlank(Given),
            Suffix = NullIfBlank(Suffix),
            Literal = NullIfBlank(Literal),
        };
        _services.Library.RenamePerson(Selected.Uuid, edited);
        Load();
        _status("Person updated.");
    }

    private static string? NullIfBlank(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
