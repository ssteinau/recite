using System.Linq;
using Recite.Core.Model;

namespace Recite.App.Services;

/// <summary>Formatting helpers for showing domain entities in the UI.</summary>
public static class Display
{
    public static string Authors(Item item, int max = 3)
    {
        var names = item.Contributions
            .Where(c => c.Role is ContributorRole.Author)
            .OrderBy(c => c.Order)
            .Select(c => c.Person!.ToCslName().DisplayName())
            .ToList();
        if (names.Count == 0)
            names = item.Contributions
                .Where(c => c.Role is ContributorRole.Editor)
                .OrderBy(c => c.Order)
                .Select(c => c.Person!.ToCslName().DisplayName() + " (ed.)")
                .ToList();
        if (names.Count == 0) return "—";
        if (names.Count <= max) return string.Join("; ", names);
        return string.Join("; ", names.Take(max)) + " et al.";
    }

    public static string Venue(Item item)
    {
        if (item.Journal is not null) return item.Journal.Name;
        if (item.ContainerParent is not null) return item.ContainerParent.Title ?? "";
        return item.GetField("container-title") ?? item.Series?.Name ?? "";
    }

    public static string TypeLabel(string cslType) => cslType switch
    {
        "article-journal" => "Journal article",
        "article-magazine" => "Magazine article",
        "article-newspaper" => "Newspaper article",
        "paper-conference" => "Conference paper",
        "chapter" => "Chapter",
        "book" => "Book",
        "proceedings" => "Proceedings",
        "collection" => "Edited book",
        "thesis" => "Thesis",
        "report" => "Report",
        "standard" => "Standard",
        "webpage" => "Web page",
        "dataset" => "Dataset",
        "patent" => "Patent",
        _ => "Document",
    };
}
