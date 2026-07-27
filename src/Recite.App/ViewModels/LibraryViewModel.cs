using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Recite.App.Services;

namespace Recite.App.ViewModels;

public sealed partial class LibraryViewModel : ViewModelBase, IRefreshable
{
    private readonly AppServices _services;
    private readonly Action<string> _status;

    public ObservableCollection<ItemRow> Items { get; } = new();

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private ItemRow? _selectedItem;
    [ObservableProperty] private ItemEditorViewModel? _editor;
    [ObservableProperty] private string _resultSummary = "";

    public LibraryViewModel(AppServices services, Action<string> status)
    {
        _services = services;
        _status = status;
    }

    public void Refresh() => LoadItems();

    partial void OnSearchTextChanged(string value) => LoadItems();

    partial void OnSelectedItemChanged(ItemRow? value)
    {
        if (value is null) { Editor = null; return; }
        var item = _services.Library.GetItem(value.Uuid);
        Editor = item is null ? null : ItemEditorViewModel.ForExisting(_services, item, OnSaved, OnDeleted);
    }

    private void LoadItems()
    {
        var rows = string.IsNullOrWhiteSpace(SearchText)
            ? _services.Library.AllItems().Select(ItemRow.From)
            : _services.Library.GetItems(_services.Library.SearchIds(SearchText)).Select(ItemRow.From);

        var selectedUuid = SelectedItem?.Uuid;
        Items.Clear();
        foreach (var r in rows) Items.Add(r);
        ResultSummary = $"{Items.Count} item{(Items.Count == 1 ? "" : "s")}" +
            (string.IsNullOrWhiteSpace(SearchText) ? "" : $" matching “{SearchText}”");

        if (selectedUuid is Guid u)
            SelectedItem = Items.FirstOrDefault(i => i.Uuid == u);
    }

    private void OnSaved(Guid uuid)
    {
        LoadItems();
        SelectedItem = Items.FirstOrDefault(i => i.Uuid == uuid);
        _status("Saved.");
    }

    private void OnDeleted(Guid uuid)
    {
        Editor = null;
        LoadItems();
        _status("Deleted.");
    }

    [RelayCommand]
    private void NewItem()
    {
        SelectedItem = null;
        Editor = ItemEditorViewModel.ForNew(_services, OnSaved, OnDeleted);
    }

    [RelayCommand]
    private async Task Import()
    {
        var path = await Dialogs.OpenFileAsync("Import references",
            ("Bibliographies", new[] { "*.bib", "*.bibtex", "*.ris", "*.json" }),
            ("All files", new[] { "*" }));
        if (path is null) return;

        var ext = Path.GetExtension(path);
        var reader = _services.Formats.ReaderForExtension(ext);
        if (reader is null) { _status($"No reader for {ext}"); return; }

        try
        {
            var text = File.ReadAllText(path);
            var result = _services.Library.ImportText(text, reader.Id);
            LoadItems();
            var warn = result.Warnings.Count > 0 ? $" ({result.Warnings.Count} warning(s))" : "";
            _status($"Imported {result.Added}, skipped {result.SkippedDuplicates} duplicate(s){warn}.");
        }
        catch (Exception ex)
        {
            _status("Import failed: " + ex.Message);
        }
    }

    [RelayCommand]
    private async Task ExportAll()
    {
        var path = await Dialogs.SaveFileAsync("Export library (biblatex)", "library", "bib");
        if (path is null) return;
        var bib = _services.Export.ExportItems(null, "biblatex");
        File.WriteAllText(path, bib);
        _status($"Exported {Items.Count} items to {Path.GetFileName(path)}.");
    }
}
