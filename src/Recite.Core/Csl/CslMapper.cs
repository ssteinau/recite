using System.Text.Json.Nodes;
using Recite.Core.Model;

namespace Recite.Core.Csl;

/// <summary>
/// Converts between the domain graph (<see cref="Item"/> + relations) and the canonical
/// <see cref="CslDocument"/>. Export goes Item → CslDocument; import goes CslDocument →
/// <see cref="ItemDraft"/> (which the library layer resolves into deduped relations).
/// </summary>
public static class CslMapper
{
    /// <summary>CSL fields that are represented by relations and therefore stripped from the field bag.</summary>
    private static readonly string[] FactoredFields =
    {
        "id", "type", "author", "editor", "translator", "container-author",
        "collection-editor", "director", "composer", "recipient",
        "DOI", "ISBN", "ISSN", "URL", "collection-title",
    };

    // ---- export: Item → CslDocument ----------------------------------------

    /// <summary>
    /// Build a CSL document from a fully-loaded item. Journal/series/container relations
    /// are reconstructed into their CSL fields; identifiers become DOI/ISBN/URL fields.
    /// </summary>
    public static CslDocument ToDocument(Item item, string? idOverride = null)
    {
        var doc = new CslDocument(item.FieldBag());
        doc.Type = item.Type;
        doc.Id = idOverride ?? item.CitationKey ?? item.Uuid.ToString();

        // Contributors, grouped by role and ordered.
        foreach (var group in item.Contributions
                     .GroupBy(c => c.Role)
                     .OrderBy(g => (int)g.Key))
        {
            var names = group.OrderBy(c => c.Order)
                .Select(c => c.Person!.ToCslName())
                .ToList();
            doc.SetNames(group.Key.ToCsl(), names);
        }

        // Journal → container-title + ISSN.
        if (item.Journal is not null)
        {
            doc.ContainerTitle = item.Journal.Name;
            if (!string.IsNullOrWhiteSpace(item.Journal.Issn)) doc.Issn = item.Journal.Issn;
        }

        // Series → collection-title.
        if (item.Series is not null)
            doc.CollectionTitle = item.Series.Name;

        // crossref parent → container-title + inherited editors/publisher.
        if (item.ContainerParent is not null)
        {
            var parent = item.ContainerParent;
            doc.ContainerTitle = parent.Title ?? doc.ContainerTitle;
            if (doc.Editors.Count == 0)
            {
                var parentEditors = parent.Contributions
                    .Where(c => c.Role == ContributorRole.Editor)
                    .OrderBy(c => c.Order)
                    .Select(c => c.Person!.ToCslName())
                    .ToList();
                if (parentEditors.Count > 0) doc.SetNames("editor", parentEditors);
            }
            var pbag = parent.FieldBag();
            CopyIfAbsent(doc, pbag, "publisher");
            CopyIfAbsent(doc, pbag, "publisher-place");
            if (parent.Series is not null && string.IsNullOrEmpty(doc.CollectionTitle))
                doc.CollectionTitle = parent.Series.Name;
        }

        // Identifiers → CSL fields.
        foreach (var id in item.Identifiers)
        {
            switch (id.Scheme)
            {
                case ExternalIdentifier.Schemes.Doi: doc.Doi = id.Value; break;
                case ExternalIdentifier.Schemes.Isbn: doc.Isbn = id.Value; break;
                case ExternalIdentifier.Schemes.Url: doc.Url = id.Value; break;
                case ExternalIdentifier.Schemes.ArXiv: doc.SetString("arxiv", id.Value); break;
                case ExternalIdentifier.Schemes.PubMed: doc.SetString("PMID", id.Value); break;
            }
        }

        return doc;
    }

    private static void CopyIfAbsent(CslDocument doc, JsonObject from, string field)
    {
        if (doc.GetString(field) is null &&
            from.TryGetPropertyValue(field, out var n) && n is not null)
            doc.SetString(field, n.ToString());
    }

    // ---- import: CslDocument → ItemDraft -----------------------------------

    public static ItemDraft ToDraft(CslDocument doc)
    {
        var draft = new ItemDraft
        {
            Type = string.IsNullOrWhiteSpace(doc.Type) ? CslType.Document : doc.Type,
            Title = doc.Title,
            Year = doc.Issued?.Year,
            SourceKey = doc.Id,
        };

        // Contributors.
        foreach (var role in doc.PresentNameRoles())
            foreach (var name in doc.Names(role))
                draft.Contributors.Add((ContributorRoleExtensions.FromCsl(role), name));

        // Journal vs plain container-title.
        var container = doc.ContainerTitle;
        if (!string.IsNullOrWhiteSpace(container))
        {
            if (IsJournalType(draft.Type))
            {
                draft.JournalName = container;
                draft.JournalIssn = ExternalIdentifier.Normalize(ExternalIdentifier.Schemes.Issn, doc.Issn ?? "");
                if (string.IsNullOrEmpty(draft.JournalIssn)) draft.JournalIssn = null;
            }
            else
            {
                draft.ContainerTitle = container;
            }
        }

        if (!string.IsNullOrWhiteSpace(doc.CollectionTitle))
            draft.SeriesName = doc.CollectionTitle;

        // Identifiers.
        void AddId(string scheme, string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return;
            var v = ExternalIdentifier.Normalize(scheme, raw);
            if (!string.IsNullOrWhiteSpace(v)) draft.Identifiers.Add((scheme, v));
        }
        AddId(ExternalIdentifier.Schemes.Doi, doc.Doi);
        AddId(ExternalIdentifier.Schemes.Isbn, doc.Isbn);
        AddId(ExternalIdentifier.Schemes.Url, doc.Url);
        AddId(ExternalIdentifier.Schemes.ArXiv, doc.GetString("arxiv"));
        AddId(ExternalIdentifier.Schemes.PubMed, doc.GetString("PMID"));

        // Own fields = the doc minus everything factored out.
        var bag = doc.Root.DeepClone().AsObject();
        foreach (var f in FactoredFields) bag.Remove(f);
        bag.Remove("arxiv");
        bag.Remove("PMID");
        // For journal types, container-title is represented by the Journal relation.
        if (IsJournalType(draft.Type)) bag.Remove("container-title");
        draft.FieldsJson = bag.ToJsonString(CslJson.Pretty);

        return draft;
    }

    public static bool IsJournalType(string type) =>
        type is CslType.ArticleJournal or CslType.ArticleMagazine or CslType.ArticleNewspaper;
}
