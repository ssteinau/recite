using System;
using Recite.App.Services;
using Recite.Core.Model;

namespace Recite.App.ViewModels;

/// <summary>A read-only display projection of an item for list views.</summary>
public sealed class ItemRow
{
    public Guid Uuid { get; init; }
    public string Title { get; init; } = "";
    public string Authors { get; init; } = "";
    public string Year { get; init; } = "";
    public string TypeLabel { get; init; } = "";
    public string Venue { get; init; } = "";
    public string CitationKey { get; init; } = "";

    public string YearVenue => string.IsNullOrEmpty(Venue) ? Year : $"{Year} · {Venue}";

    public static ItemRow From(Item item) => new()
    {
        Uuid = item.Uuid,
        Title = string.IsNullOrWhiteSpace(item.Title) ? "(untitled)" : item.Title!,
        Authors = Display.Authors(item),
        Year = item.Year?.ToString() ?? "",
        TypeLabel = Display.TypeLabel(item.Type),
        Venue = Display.Venue(item),
        CitationKey = item.CitationKey ?? "",
    };
}
