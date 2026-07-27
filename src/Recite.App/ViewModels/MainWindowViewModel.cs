using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Recite.App.Services;

namespace Recite.App.ViewModels;

public sealed partial class NavItem : ObservableObject
{
    public string Title { get; }
    public string Glyph { get; }
    public ViewModelBase Content { get; }

    public NavItem(string title, string glyph, ViewModelBase content)
    {
        Title = title;
        Glyph = glyph;
        Content = content;
    }
}

public sealed partial class MainWindowViewModel : ViewModelBase
{
    private readonly AppServices _services;

    public ObservableCollection<NavItem> Sections { get; } = new();

    [ObservableProperty] private NavItem? _selectedSection;
    [ObservableProperty] private string _libraryPath = "";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private int _itemCount;

    public MainWindowViewModel(AppServices services)
    {
        _services = services;
        LibraryPath = services.LibraryRoot;
    }

    public void Initialize()
    {
        _services.SeedSampleIfEmpty();

        var library = new LibraryViewModel(_services, SetStatus);
        Sections.Add(new NavItem("Library", "", library));
        Sections.Add(new NavItem("Duplicates", "", new DuplicatesViewModel(_services, SetStatus)));
        Sections.Add(new NavItem("Persons", "", new PersonsViewModel(_services, SetStatus)));
        Sections.Add(new NavItem("Categories", "", new CategoriesViewModel(_services, SetStatus)));
        Sections.Add(new NavItem("Venues", "", new VenuesViewModel(_services, SetStatus)));
        Sections.Add(new NavItem("Projects", "", new ProjectsViewModel(_services, SetStatus)));

        SelectedSection = Sections[0];
        RefreshCount();
    }

    partial void OnSelectedSectionChanged(NavItem? value)
    {
        if (value?.Content is IRefreshable r) r.Refresh();
        RefreshCount();
    }

    private void SetStatus(string message)
    {
        Status = message;
        RefreshCount();
    }

    private void RefreshCount() => ItemCount = _services.Library.CountItems();

    [RelayCommand]
    private void Checkpoint()
    {
        _services.Text.ExportAll();
        SetStatus($"Checkpoint written to {_services.Connection.Paths.Root}");
    }
}

/// <summary>Sections that reload their data when navigated to.</summary>
public interface IRefreshable
{
    void Refresh();
}
