using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Recite.App.Services;
using Recite.Core.Model;

namespace Recite.App.ViewModels;

/// <summary>A node in a facet tree — either the facet root or a category.</summary>
public sealed partial class CategoryNode : ObservableObject
{
    public Guid Uuid { get; init; }
    public bool IsFacet { get; init; }
    public Guid FacetUuid { get; init; }
    [ObservableProperty] private string _name = "";
    public ObservableCollection<CategoryNode> Children { get; } = new();
}

public sealed partial class CategoriesViewModel : ViewModelBase, IRefreshable
{
    private readonly AppServices _services;
    private readonly Action<string> _status;

    public ObservableCollection<CategoryNode> Facets { get; } = new();

    [ObservableProperty] private CategoryNode? _selected;
    [ObservableProperty] private string _newName = "";

    public CategoriesViewModel(AppServices services, Action<string> status)
    {
        _services = services;
        _status = status;
    }

    public void Refresh() => Load();

    private void Load()
    {
        Facets.Clear();
        foreach (var facet in _services.Library.GetFacets())
        {
            var root = new CategoryNode { Uuid = facet.Uuid, IsFacet = true, FacetUuid = facet.Uuid, Name = facet.Name };
            var nodesById = facet.Categories.ToDictionary(
                c => c.Id,
                c => new CategoryNode { Uuid = c.Uuid, FacetUuid = facet.Uuid, Name = c.Name });
            foreach (var c in facet.Categories.OrderBy(c => c.Order))
            {
                var node = nodesById[c.Id];
                if (c.ParentId is int pid && nodesById.TryGetValue(pid, out var parent))
                    parent.Children.Add(node);
                else
                    root.Children.Add(node);
            }
            Facets.Add(root);
        }
    }

    [RelayCommand]
    private void AddFacet()
    {
        if (string.IsNullOrWhiteSpace(NewName)) return;
        _services.Library.AddFacet(NewName.Trim());
        NewName = "";
        Load();
        _status("Facet added.");
    }

    [RelayCommand]
    private void AddCategory()
    {
        if (string.IsNullOrWhiteSpace(NewName) || Selected is null) return;
        Guid facetUuid = Selected.FacetUuid;
        Guid? parentUuid = Selected.IsFacet ? null : Selected.Uuid;
        _services.Library.AddCategory(facetUuid, parentUuid, NewName.Trim());
        NewName = "";
        Load();
        _status("Category added.");
    }

    [RelayCommand]
    private void Rename()
    {
        if (Selected is null || string.IsNullOrWhiteSpace(NewName)) return;
        if (Selected.IsFacet) _services.Library.RenameFacet(Selected.Uuid, NewName.Trim());
        else _services.Library.RenameCategory(Selected.Uuid, NewName.Trim());
        NewName = "";
        Load();
        _status("Renamed.");
    }

    [RelayCommand]
    private void Delete()
    {
        if (Selected is null) return;
        if (Selected.IsFacet) _services.Library.DeleteFacet(Selected.Uuid);
        else _services.Library.DeleteCategory(Selected.Uuid);
        Selected = null;
        Load();
        _status("Deleted.");
    }
}
