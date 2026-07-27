using System.Text;
using Recite.Core.Csl;

namespace Recite.Core.Formats;

/// <summary>RIS reader/writer. Line-based tags (<c>XX  - value</c>), records end at <c>ER</c>.</summary>
public sealed class RisFormat : IReferenceReader, IReferenceWriter
{
    public string Id => "ris";
    public string DisplayName => "RIS";
    public IReadOnlyList<string> Extensions => new[] { ".ris" };
    public string Extension => ".ris";

    private static readonly Dictionary<string, string> RisToCsl = new(StringComparer.OrdinalIgnoreCase)
    {
        ["JOUR"] = CslType.ArticleJournal,
        ["MGZN"] = CslType.ArticleMagazine,
        ["NEWS"] = CslType.ArticleNewspaper,
        ["CONF"] = CslType.PaperConference,
        ["CPAPER"] = CslType.PaperConference,
        ["CHAP"] = CslType.Chapter,
        ["BOOK"] = CslType.Book,
        ["EBOOK"] = CslType.Book,
        ["THES"] = CslType.Thesis,
        ["RPRT"] = CslType.Report,
        ["STD"] = CslType.Standard,
        ["PAT"] = CslType.Patent,
        ["DATA"] = CslType.Dataset,
        ["ELEC"] = CslType.Webpage,
        ["GEN"] = CslType.Document,
    };

    private static readonly Dictionary<string, string> CslToRis = new()
    {
        [CslType.ArticleJournal] = "JOUR",
        [CslType.ArticleMagazine] = "MGZN",
        [CslType.ArticleNewspaper] = "NEWS",
        [CslType.PaperConference] = "CPAPER",
        [CslType.Chapter] = "CHAP",
        [CslType.Book] = "BOOK",
        [CslType.Thesis] = "THES",
        [CslType.Report] = "RPRT",
        [CslType.Standard] = "STD",
        [CslType.Patent] = "PAT",
        [CslType.Dataset] = "DATA",
        [CslType.Webpage] = "ELEC",
        [CslType.Document] = "GEN",
    };

    public IReadOnlyList<CslDocument> Read(string text)
    {
        var docs = new List<CslDocument>();
        CslDocument? current = null;
        var authors = new List<CslName>();
        var editors = new List<CslName>();
        string? startPage = null, endPage = null;

        void Flush()
        {
            if (current is null) return;
            if (authors.Count > 0) current.SetNames("author", authors);
            if (editors.Count > 0) current.SetNames("editor", editors);
            if (startPage is not null || endPage is not null)
                current.Page = endPage is not null && startPage is not null ? $"{startPage}-{endPage}" : startPage ?? endPage;
            docs.Add(current);
            current = null; authors.Clear(); editors.Clear(); startPage = endPage = null;
        }

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            // RIS lines are "XX  - value": tag, two spaces, hyphen at index 4, space, value.
            if (line.Length < 6 || line[4] != '-' || !char.IsLetterOrDigit(line[0])) continue;
            var tag = line[..2].ToUpperInvariant();
            var value = line.Length > 6 ? line[6..].Trim() : "";

            if (tag == "TY")
            {
                Flush();
                current = new CslDocument { Type = RisToCsl.GetValueOrDefault(value, CslType.Document) };
                continue;
            }
            if (current is null) continue;
            if (tag == "ER") { Flush(); continue; }

            switch (tag)
            {
                case "AU" or "A1": authors.Add(CslName.Parse(value)); break;
                case "ED" or "A2": editors.Add(CslName.Parse(value)); break;
                case "TI" or "T1": current.Title = value; break;
                case "T2" or "JO" or "JF" or "JA":
                    if (string.IsNullOrEmpty(current.ContainerTitle)) current.ContainerTitle = value;
                    break;
                case "T3": current.CollectionTitle = value; break;
                case "PY" or "Y1" or "DA":
                    if (current.Issued is null) current.SetDate("issued", CslDate.Parse(value));
                    break;
                case "VL": current.Volume = value; break;
                case "IS": current.Issue = value; break;
                case "SP": startPage = value; break;
                case "EP": endPage = value; break;
                case "DO": current.Doi = value; break;
                case "SN":
                    if (value.Replace("-", "").Length == 8 && current.Issn is null) current.Issn = value;
                    else current.Isbn = value;
                    break;
                case "UR": current.Url = value; break;
                case "AB" or "N2": current.Abstract = value; break;
                case "PB": current.Publisher = value; break;
                case "CY" or "PP": current.PublisherPlace = value; break;
                case "ID": current.Id = value; break;
            }
        }
        Flush();
        return docs;
    }

    public string Write(IEnumerable<CslDocument> documents)
    {
        var sb = new StringBuilder();
        foreach (var d in documents)
        {
            void W(string tag, string? value)
            {
                if (!string.IsNullOrWhiteSpace(value)) sb.Append(tag).Append("  - ").Append(value).Append("\r\n");
            }

            W("TY", CslToRis.GetValueOrDefault(d.Type, "GEN"));
            if (!string.IsNullOrWhiteSpace(d.Id)) W("ID", d.Id);
            foreach (var a in d.Authors) W("AU", a.DisplayName());
            foreach (var e in d.Editors) W("ED", e.DisplayName());
            W("TI", d.Title);
            W("T2", d.ContainerTitle);
            W("T3", d.CollectionTitle);
            if (d.Issued?.Year is int y) W("PY", y.ToString());
            W("VL", d.Volume);
            W("IS", d.Issue);
            if (!string.IsNullOrWhiteSpace(d.Page))
            {
                var parts = d.Page!.Split(new[] { '-', '–' }, 2);
                W("SP", parts[0].Trim());
                if (parts.Length > 1) W("EP", parts[1].Trim());
            }
            W("DO", d.Doi);
            W("SN", d.Issn ?? d.Isbn);
            W("UR", d.Url);
            W("AB", d.Abstract);
            W("PB", d.Publisher);
            W("CY", d.PublisherPlace);
            sb.Append("ER  - \r\n\r\n");
        }
        return sb.ToString();
    }
}
