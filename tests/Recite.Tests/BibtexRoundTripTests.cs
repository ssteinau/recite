using Recite.Core.Csl;
using Recite.Core.Formats;
using Xunit;

namespace Recite.Tests;

public class BibtexRoundTripTests
{
    private readonly BibtexFormat _bib = new();

    [Fact]
    public void Parses_basic_article()
    {
        const string src = """
        @article{smith2020,
          author = {Smith, John and Doe, Jane},
          title = {A Study of Things},
          journal = {Journal of Things},
          year = {2020},
          volume = {12},
          number = {3},
          pages = {100--120},
          doi = {10.1000/xyz123}
        }
        """;
        var docs = _bib.Read(src);
        var d = Assert.Single(docs);
        Assert.Equal(CslType.ArticleJournal, d.Type);
        Assert.Equal("A Study of Things", d.Title);
        Assert.Equal("Journal of Things", d.ContainerTitle);
        Assert.Equal(2020, d.Issued!.Year);
        Assert.Equal("12", d.Volume);
        Assert.Equal("3", d.Issue);
        Assert.Equal("100-120", d.Page);
        Assert.Equal("10.1000/xyz123", d.Doi);
        Assert.Equal(2, d.Authors.Count);
        Assert.Equal("Smith", d.Authors[0].Family);
        Assert.Equal("John", d.Authors[0].Given);
    }

    [Fact]
    public void Resolves_string_macros_and_concatenation()
    {
        const string src = """
        @string{ieee = {IEEE Transactions on }}
        @string{pami = {Pattern Analysis}}
        @article{k,
          title = {X},
          journal = ieee # pami,
          year = 2019
        }
        """;
        var d = Assert.Single(_bib.Read(src));
        Assert.Equal("IEEE Transactions on Pattern Analysis", d.ContainerTitle);
        Assert.Equal(2019, d.Issued!.Year);
    }

    [Fact]
    public void Decodes_latex_accents()
    {
        const string src = """
        @book{k, title = {Na{\"i}ve Set Theory}, author = {G{\"o}del, Kurt}, year = {1940}}
        """;
        var d = Assert.Single(_bib.Read(src));
        Assert.Equal("Naïve Set Theory", d.Title);
        Assert.Equal("Gödel", d.Authors[0].Family);
    }

    [Fact]
    public void Handles_nested_braces_in_title()
    {
        const string src = "@misc{k, title = {The {DNA} of {Software}}, year=2001}";
        var d = Assert.Single(_bib.Read(src));
        Assert.Equal("The DNA of Software", d.Title);
    }

    [Fact]
    public void Carries_crossref_key()
    {
        const string src = """
        @inproceedings{child, title={A Paper}, author={Ada, Alan}, crossref={proc2020}, year=2020}
        @proceedings{proc2020, title={Proc of Stuff}, year=2020, editor={Ed, Emma}}
        """;
        var docs = _bib.Read(src);
        var child = docs.First(d => d.Id == "child");
        Assert.Equal("proc2020", child.GetString("crossref"));
    }

    [Fact]
    public void Csl_survives_bib_write_then_read()
    {
        const string src = """
        @inproceedings{smith2020,
          author = {Smith, John and van der Berg, Jan},
          title = {Réseaux and Things},
          booktitle = {Proceedings of the 30th Conference},
          year = {2020},
          pages = {1--10},
          doi = {10.1/abc}
        }
        """;
        var original = Assert.Single(_bib.Read(src));
        var round = Assert.Single(_bib.Read(_bib.Write(new[] { original })));

        Assert.Equal(original.Type, round.Type);
        Assert.Equal(original.Title, round.Title);
        Assert.Equal(original.ContainerTitle, round.ContainerTitle);
        Assert.Equal(original.Issued!.Year, round.Issued!.Year);
        Assert.Equal(original.Page, round.Page);
        Assert.Equal(original.Doi, round.Doi);
        Assert.Equal(original.Authors.Count, round.Authors.Count);
        Assert.Equal("Berg", round.Authors[1].Family);
        Assert.Equal("van der", round.Authors[1].NonDroppingParticle);
    }
}
