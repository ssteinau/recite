using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Recite.App.Services;
using Recite.Core.Dedup;
using Recite.Core.Model;

namespace Recite.App.ViewModels;

public sealed class ItemDupRow
{
    public Guid LeftUuid { get; init; }
    public Guid RightUuid { get; init; }
    public string LeftText { get; init; } = "";
    public string RightText { get; init; } = "";
    public string Confidence { get; init; } = "";
    public string Reason { get; init; } = "";
}

public sealed class PersonDupRow
{
    public Guid LeftUuid { get; init; }
    public Guid RightUuid { get; init; }
    public string LeftText { get; init; } = "";
    public string RightText { get; init; } = "";
    public string Reason { get; init; } = "";
}

public sealed class MergeLogRow
{
    public Guid Uuid { get; init; }
    public string Text { get; init; } = "";
    public bool Reverted { get; init; }
    public bool CanRevert => !Reverted;
}

public sealed partial class DuplicatesViewModel : ViewModelBase, IRefreshable
{
    private readonly AppServices _services;
    private readonly Action<string> _status;

    public ObservableCollection<ItemDupRow> ItemPairs { get; } = new();
    public ObservableCollection<PersonDupRow> PersonPairs { get; } = new();
    public ObservableCollection<MergeLogRow> MergeHistory { get; } = new();

    public DuplicatesViewModel(AppServices services, Action<string> status)
    {
        _services = services;
        _status = status;
    }

    public void Refresh() => Scan();

    [RelayCommand]
    private void Scan()
    {
        ItemPairs.Clear();
        foreach (var p in _services.Library.FindItemDuplicates().Take(200))
        {
            var left = _services.Library.GetItemLite(p.LeftUuid);
            var right = _services.Library.GetItemLite(p.RightUuid);
            if (left is null || right is null) continue;
            ItemPairs.Add(new ItemDupRow
            {
                LeftUuid = p.LeftUuid,
                RightUuid = p.RightUuid,
                LeftText = Summarize(left),
                RightText = Summarize(right),
                Confidence = p.Confidence.ToString(),
                Reason = p.Reason,
            });
        }

        PersonPairs.Clear();
        foreach (var m in _services.Library.FindPersonDuplicates().Take(200))
        {
            var left = _services.Library.GetPerson(m.LeftUuid);
            var right = _services.Library.GetPerson(m.RightUuid);
            if (left is null || right is null) continue;
            PersonPairs.Add(new PersonDupRow
            {
                LeftUuid = m.LeftUuid,
                RightUuid = m.RightUuid,
                LeftText = left.DisplayName(),
                RightText = right.DisplayName(),
                Reason = m.Reason,
            });
        }

        LoadHistory();
        _status($"{ItemPairs.Count} item and {PersonPairs.Count} person candidate(s).");
    }

    private static string Summarize(Item i) =>
        $"{Display.Authors(i, 2)} ({i.Year}) — {(string.IsNullOrEmpty(i.Title) ? "(untitled)" : i.Title)}  [{i.CitationKey}]";

    private void LoadHistory()
    {
        MergeHistory.Clear();
        foreach (var log in _services.Library.GetMergeLog().Take(50))
            MergeHistory.Add(new MergeLogRow
            {
                Uuid = log.Uuid,
                Reverted = log.Reverted,
                Text = $"{log.Timestamp.LocalDateTime:g} · {log.Kind} · {(log.Reverted ? "reverted" : "merged")}",
            });
    }

    [RelayCommand]
    private void MergeItemsKeepLeft(ItemDupRow? row)
    {
        if (row is null) return;
        _services.Merge.MergeItems(row.LeftUuid, row.RightUuid);
        _status("Items merged (kept left).");
        Scan();
    }

    [RelayCommand]
    private void MergeItemsKeepRight(ItemDupRow? row)
    {
        if (row is null) return;
        _services.Merge.MergeItems(row.RightUuid, row.LeftUuid);
        _status("Items merged (kept right).");
        Scan();
    }

    [RelayCommand]
    private void MergePersonsKeepLeft(PersonDupRow? row)
    {
        if (row is null) return;
        _services.Merge.MergePersons(row.LeftUuid, row.RightUuid);
        _status("Persons merged (kept left).");
        Scan();
    }

    [RelayCommand]
    private void MergePersonsKeepRight(PersonDupRow? row)
    {
        if (row is null) return;
        _services.Merge.MergePersons(row.RightUuid, row.LeftUuid);
        _status("Persons merged (kept right).");
        Scan();
    }

    [RelayCommand]
    private void RevertMerge(MergeLogRow? row)
    {
        if (row is null || row.Reverted) return;
        _services.Merge.Revert(row.Uuid);
        _status("Merge reverted.");
        Scan();
    }
}
