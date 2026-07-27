using Recite.Core.Citations;
using Recite.Core.Csl;
using Recite.Core.Dedup;
using Recite.Core.Formats;
using Recite.Core.Projections;
using Xunit;

namespace Recite.Tests;

public class CitationKeyTests
{
    [Fact]
    public void Default_scheme_builds_author_year_shorttitle()
    {
        var doc = new CslDocument { Title = "The Analysis of Complex Systems" };
        doc.SetNames("author", new[] { CslName.Parse("Smith, John") });
        doc.SetDate("issued", CslDate.FromYear(2021));

        var key = new CitationKeyScheme().BaseKey(doc);
        Assert.Equal("Smith2021Analysis", key);
    }

    [Fact]
    public void Disambiguation_appends_letters()
    {
        var taken = new HashSet<string> { "Smith2021", "Smith2021a" };
        var key = CitationKeyScheme.Disambiguate("Smith2021", taken.Contains);
        Assert.Equal("Smith2021b", key);
    }

    [Fact]
    public void Handles_missing_author_and_year()
    {
        var doc = new CslDocument { Title = "Untitled" };
        var key = new CitationKeyScheme().BaseKey(doc);
        Assert.Equal("AnonndUntitled", key);
    }
}

public class ProjectionDeterminismTests
{
    private static CslDocument SampleArticle()
    {
        var doc = new CslDocument
        {
            Type = CslType.ArticleJournal,
            Id = "k",
            Title = "International Perspectives",
            ContainerTitle = "International Journal of Examples",
        };
        doc.SetNames("author", new[] { CslName.Parse("Doe, Jane") });
        doc.SetDate("issued", CslDate.FromYear(2020));
        return doc;
    }

    private static ProjectionSet Sample()
    {
        var set = new ProjectionSet();
        set.JournalAbbreviations.Add(new AbbreviationRule
        {
            Match = "International Journal of Examples",
            Abbreviation = "Int'l J. Examples",
        });
        set.Substitutions.Add(new StringSubstitution
        {
            Field = "title", Pattern = "International", Replacement = "Int'l",
        });
        return set;
    }

    [Fact]
    public void Render_is_pure_and_leaves_input_unchanged()
    {
        var input = SampleArticle();
        var before = input.ToJsonString();
        var _ = ProjectionEngine.Project(input, Sample());
        Assert.Equal(before, input.ToJsonString());
    }

    [Fact]
    public void Render_is_repeatable_byte_for_byte()
    {
        var set = Sample();
        var a = ProjectionEngine.Project(SampleArticle(), set).ToJsonString();
        var b = ProjectionEngine.Project(SampleArticle(), set).ToJsonString();
        Assert.Equal(a, b);
    }

    [Fact]
    public void Abbreviation_and_substitution_applied()
    {
        var result = ProjectionEngine.Project(SampleArticle(), Sample());
        Assert.Equal("Int'l J. Examples", result.ContainerTitle);
        Assert.Equal("Int'l Perspectives", result.Title);
    }

    [Fact]
    public void Item_override_wins_over_substitution()
    {
        var uuid = Guid.NewGuid();
        var set = Sample();
        set.ItemOverrides[uuid.ToString()] = new() { ["title"] = "Explicit Title" };
        var result = ProjectionEngine.Project(SampleArticle(), set, uuid);
        Assert.Equal("Explicit Title", result.Title);
    }
}

public class DedupTests
{
    private static DedupRecord Rec(string title, int? year, string type = CslType.ArticleJournal,
        string[]? authors = null, (string, string)[]? ids = null) =>
        new(Guid.NewGuid(), type, title, year, authors ?? Array.Empty<string>(), ids ?? Array.Empty<(string, string)>());

    [Fact]
    public void Exact_match_on_shared_doi()
    {
        var a = Rec("Title One", 2020, ids: new[] { ("doi", "10.1/x") });
        var b = Rec("Slightly Different", 2019, ids: new[] { ("doi", "10.1/x") });
        var pairs = new DedupService().FindDuplicates(new[] { a, b });
        var p = Assert.Single(pairs);
        Assert.Equal(DupConfidence.Exact, p.Confidence);
    }

    [Fact]
    public void Same_title_different_year_is_not_a_duplicate()
    {
        // PLAN §3: "Business Process Management" ×4 as different-year proceedings volumes.
        var a = Rec("Business Process Management", 2019);
        var b = Rec("Business Process Management", 2020);
        var pairs = new DedupService().FindDuplicates(new[] { a, b });
        Assert.Empty(pairs);
    }

    [Fact]
    public void Near_identical_same_year_is_flagged()
    {
        var a = Rec("A Study of Neural Networks", 2020, authors: new[] { "smith|j" });
        var b = Rec("A Study of Neural Network", 2020, authors: new[] { "smith|j" });
        var pairs = new DedupService().FindDuplicates(new[] { a, b });
        var p = Assert.Single(pairs);
        Assert.True(p.Score >= 0.80, $"score was {p.Score}");
    }
}

public class RisRoundTripTests
{
    [Fact]
    public void Reads_and_writes_journal_article()
    {
        const string ris = "TY  - JOUR\r\nAU  - Smith, John\r\nTI  - Example Title\r\nJO  - Some Journal\r\nPY  - 2018\r\nVL  - 5\r\nSP  - 10\r\nEP  - 20\r\nDO  - 10.1/ris\r\nER  - \r\n";
        var fmt = new RisFormat();
        var d = Assert.Single(fmt.Read(ris));
        Assert.Equal(CslType.ArticleJournal, d.Type);
        Assert.Equal("Example Title", d.Title);
        Assert.Equal("Some Journal", d.ContainerTitle);
        Assert.Equal("10-20", d.Page);

        var round = Assert.Single(fmt.Read(fmt.Write(new[] { d })));
        Assert.Equal(d.Title, round.Title);
        Assert.Equal(d.Page, round.Page);
        Assert.Equal(d.Doi, round.Doi);
    }
}
