using System.Text;
using Recite.Core.Csl;

namespace Recite.Core.Formats;

/// <summary>
/// biblatex/BibTeX reader/writer. Uses <see cref="BibtexParser"/> for the messy parse,
/// then maps entries to/from CSL. Preserves the <c>crossref</c> key as a CSL field so the
/// library importer can wire up container parent→child links (PLAN §3).
/// </summary>
public sealed class BibtexFormat : IReferenceReader, IReferenceWriter
{
    public string Id => "biblatex";
    public string DisplayName => "BibTeX / biblatex";
    public IReadOnlyList<string> Extensions => new[] { ".bib", ".bibtex" };
    public string Extension => ".bib";

    private static readonly Dictionary<string, string> BibToCsl = new(StringComparer.OrdinalIgnoreCase)
    {
        ["article"] = CslType.ArticleJournal,
        ["inproceedings"] = CslType.PaperConference,
        ["conference"] = CslType.PaperConference,
        ["incollection"] = CslType.Chapter,
        ["inbook"] = CslType.Chapter,
        ["book"] = CslType.Book,
        ["mvbook"] = CslType.Book,
        ["booklet"] = CslType.Book,
        ["proceedings"] = "proceedings",
        ["mvproceedings"] = "proceedings",
        ["collection"] = "collection",
        ["mvcollection"] = "collection",
        ["phdthesis"] = CslType.Thesis,
        ["mastersthesis"] = CslType.Thesis,
        ["thesis"] = CslType.Thesis,
        ["techreport"] = CslType.Report,
        ["report"] = CslType.Report,
        ["manual"] = CslType.Report,
        ["standard"] = CslType.Standard,
        ["patent"] = CslType.Patent,
        ["online"] = CslType.Webpage,
        ["electronic"] = CslType.Webpage,
        ["www"] = CslType.Webpage,
        ["dataset"] = CslType.Dataset,
        ["misc"] = CslType.Document,
        ["unpublished"] = CslType.Document,
    };

    private static readonly Dictionary<string, string> CslToBib = new()
    {
        [CslType.ArticleJournal] = "article",
        [CslType.ArticleMagazine] = "article",
        [CslType.ArticleNewspaper] = "article",
        [CslType.PaperConference] = "inproceedings",
        [CslType.Chapter] = "incollection",
        [CslType.Book] = "book",
        ["proceedings"] = "proceedings",
        ["collection"] = "collection",
        [CslType.Thesis] = "thesis",
        [CslType.Report] = "report",
        [CslType.Standard] = "standard",
        [CslType.Patent] = "patent",
        [CslType.Webpage] = "online",
        [CslType.Dataset] = "dataset",
        [CslType.Document] = "misc",
    };

    // ---- read ---------------------------------------------------------------

    public IReadOnlyList<CslDocument> Read(string text)
    {
        var entries = new BibtexParser(text).Parse();
        return entries.Select(ToCsl).ToList();
    }

    private static CslDocument ToCsl(BibEntry e)
    {
        var doc = new CslDocument
        {
            Type = BibToCsl.GetValueOrDefault(e.Type, CslType.Document),
            Id = e.Key,
        };
        bool isArticle = doc.Type is CslType.ArticleJournal or CslType.ArticleMagazine or CslType.ArticleNewspaper;

        foreach (var (field, rawValue) in e.Fields)
        {
            var v = LatexCodec.Decode(rawValue);
            switch (field.ToLowerInvariant())
            {
                case "title": doc.Title = v; break;
                case "subtitle": if (!string.IsNullOrEmpty(v)) doc.Title = string.IsNullOrEmpty(doc.Title) ? v : $"{doc.Title}: {v}"; break;
                case "author": doc.SetNames("author", ParseNameList(rawValue)); break;
                case "editor": doc.SetNames("editor", ParseNameList(rawValue)); break;
                case "translator": doc.SetNames("translator", ParseNameList(rawValue)); break;
                case "journal" or "journaltitle": doc.ContainerTitle = v; break;
                case "booktitle": if (string.IsNullOrEmpty(doc.ContainerTitle)) doc.ContainerTitle = v; break;
                case "series": doc.CollectionTitle = v; break;
                case "year": if (doc.Issued is null) doc.SetDate("issued", CslDate.Parse(v)); break;
                case "date": doc.SetDate("issued", CslDate.Parse(v)); break;
                case "volume": doc.Volume = v; break;
                case "number": if (isArticle) doc.Issue = v; else doc.Number = v; break;
                case "issue": doc.Issue = v; break;
                case "pages": doc.Page = v.Replace("--", "-").Replace("–", "-"); break;
                case "publisher" or "organization": if (string.IsNullOrEmpty(doc.Publisher)) doc.Publisher = v; break;
                case "school" or "institution": doc.Publisher = v; break;
                case "address" or "location": doc.PublisherPlace = v; break;
                case "doi": doc.Doi = v; break;
                case "isbn": doc.Isbn = v; break;
                case "issn": doc.Issn = v; break;
                case "url": doc.Url = v; break;
                case "eprint": doc.SetString("arxiv", v); break;
                case "abstract": doc.Abstract = v; break;
                case "edition": doc.Edition = v; break;
                case "note" or "annote": doc.Note = v; break;
                case "type": doc.Genre = v; break;
                case "crossref": doc.SetString("crossref", rawValue.Trim()); break;
                case "keywords": doc.SetString("keyword", v); break;
            }
        }
        return doc;
    }

    /// <summary>Split an "and"-separated name list at brace depth 0, then parse each.</summary>
    internal static List<CslName> ParseNameList(string raw)
    {
        var names = new List<CslName>();
        var sb = new StringBuilder();
        int depth = 0;
        for (int i = 0; i < raw.Length; i++)
        {
            char c = raw[i];
            if (c == '{') depth++;
            else if (c == '}') depth = Math.Max(0, depth - 1);

            if (depth == 0 && c == ' ' && Matches(raw, i, " and "))
            {
                names.Add(CslName.Parse(LatexCodec.Decode(sb.ToString())));
                sb.Clear();
                i += 4; // skip "and "
                continue;
            }
            sb.Append(c);
        }
        if (sb.Length > 0) names.Add(CslName.Parse(LatexCodec.Decode(sb.ToString())));
        return names.Where(n => !n.IsEmpty).ToList();
    }

    private static bool Matches(string s, int i, string token) =>
        i + token.Length <= s.Length && s.Substring(i, token.Length).Equals(token, StringComparison.OrdinalIgnoreCase);

    // ---- write --------------------------------------------------------------

    public string Write(IEnumerable<CslDocument> documents)
    {
        var sb = new StringBuilder();
        foreach (var d in documents)
        {
            WriteEntry(sb, d);
            sb.Append('\n');
        }
        return sb.ToString();
    }

    private static void WriteEntry(StringBuilder sb, CslDocument d)
    {
        var bibType = CslToBib.GetValueOrDefault(d.Type, "misc");
        bool isArticle = d.Type is CslType.ArticleJournal or CslType.ArticleMagazine or CslType.ArticleNewspaper;
        var key = string.IsNullOrWhiteSpace(d.Id) ? "ref" : d.Id;

        var fields = new List<(string, string)>();
        void F(string name, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value)) fields.Add((name, value!));
        }

        if (!string.IsNullOrWhiteSpace(d.Title)) F("title", "{" + LatexCodec.Encode(d.Title!) + "}");
        if (d.Authors.Count > 0) F("author", NameList(d.Authors));
        if (d.Editors.Count > 0) F("editor", NameList(d.Editors));

        if (isArticle) F("journal", Brace(d.ContainerTitle));
        else F("booktitle", Brace(d.ContainerTitle));
        F("series", Brace(d.CollectionTitle));

        if (d.Issued?.Year is int y) F("year", y.ToString());
        F("volume", Brace(d.Volume));
        if (isArticle) F("number", Brace(d.Issue));
        else { F("number", Brace(d.Number)); F("issue", Brace(d.Issue)); }
        if (!string.IsNullOrWhiteSpace(d.Page)) F("pages", "{" + d.Page!.Replace("-", "--") + "}");

        if (d.Type == CslType.Thesis) F("school", Brace(d.Publisher));
        else if (d.Type == CslType.Report) F("institution", Brace(d.Publisher));
        else F("publisher", Brace(d.Publisher));
        F("address", Brace(d.PublisherPlace));
        F("edition", Brace(d.Edition));
        F("type", Brace(d.Genre));

        F("doi", Brace(d.Doi));
        F("isbn", Brace(d.Isbn));
        F("issn", Brace(d.Issn));
        F("url", Brace(d.Url));
        F("eprint", Brace(d.GetString("arxiv")));
        F("crossref", string.IsNullOrWhiteSpace(d.GetString("crossref")) ? null : "{" + d.GetString("crossref") + "}");
        F("abstract", Brace(d.Abstract));
        F("note", Brace(d.Note));
        F("keywords", Brace(d.GetString("keyword")));

        sb.Append('@').Append(bibType).Append('{').Append(key).Append(",\n");
        for (int i = 0; i < fields.Count; i++)
        {
            sb.Append("  ").Append(fields[i].Item1).Append(" = ").Append(fields[i].Item2);
            sb.Append(i < fields.Count - 1 ? ",\n" : "\n");
        }
        sb.Append("}\n");
    }

    private static string Brace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "" : "{" + LatexCodec.Encode(value!) + "}";

    private static string NameList(IReadOnlyList<CslName> names) =>
        "{" + string.Join(" and ", names.Select(n => LatexCodec.Encode(n.DisplayName()))) + "}";
}
