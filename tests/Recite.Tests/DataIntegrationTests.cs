using Recite.Core.Model;
using Recite.Core.Projections;
using Recite.Data;
using Xunit;

namespace Recite.Tests;

public class PersistenceIdentityTests
{
    // PLAN §10 Phase 0 acceptance: create → save → export → reload → identity preserved.
    [Fact]
    public void Identity_survives_reload()
    {
        using var tmp = new TempLibrary();
        const string bib = "@article{k, author={Doe, Jane}, title={A Title}, journal={J}, year={2020}, doi={10.1/x}}";
        var result = tmp.Library.ImportText(bib, "biblatex");
        var uuid = Assert.Single(result.AddedUuids);

        var lib2 = tmp.Reopen();
        var item = lib2.GetItem(uuid);
        Assert.NotNull(item);
        Assert.Equal("A Title", item!.Title);
        Assert.Equal(2020, item.Year);
        Assert.Equal(uuid, item.Uuid);
        Assert.Contains(item.Identifiers, i => i.Value == "10.1/x");
        Assert.False(string.IsNullOrEmpty(item.CitationKey));
    }
}

public class ImportBehaviourTests
{
    [Fact]
    public void Persons_and_journals_are_deduped()
    {
        using var tmp = new TempLibrary();
        const string bib = """
        @article{a, author={Smith, John}, title={First}, journal={Nature}, year={2019}, doi={10.1/a}}
        @article{b, author={Smith, John}, title={Second}, journal={Nature}, year={2020}, doi={10.1/b}}
        """;
        tmp.Library.ImportText(bib, "biblatex");

        using var ctx = tmp.Library.CreateContext();
        Assert.Equal(1, ctx.Persons.Count(p => p.Family == "Smith"));
        Assert.Equal(1, ctx.Journals.Count(j => j.Name == "Nature"));
        Assert.Equal(2, ctx.Items.Count());
    }

    [Fact]
    public void Exact_duplicate_is_skipped()
    {
        using var tmp = new TempLibrary();
        tmp.Library.ImportText("@article{a, title={T}, doi={10.1/dup}, year=2020}", "biblatex");
        var second = tmp.Library.ImportText("@article{b, title={T Again}, doi={10.1/dup}, year=2020}", "biblatex");
        Assert.Equal(0, second.Added);
        Assert.Equal(1, second.SkippedDuplicates);
        Assert.Equal(1, tmp.Library.CountItems());
    }

    [Fact]
    public void Crossref_links_container_parent()
    {
        using var tmp = new TempLibrary();
        const string bib = """
        @inproceedings{child, title={A Paper}, author={Ada, Alan}, crossref={proc}, year=2020}
        @proceedings{proc, title={Proceedings of X}, editor={Ed, Emma}, year=2020}
        """;
        tmp.Library.ImportText(bib, "biblatex");

        using var ctx = tmp.Library.CreateContext();
        var child = ctx.Items.First(i => i.Title == "A Paper");
        Assert.NotNull(child.ContainerParentId);
    }

    [Fact]
    public void Baked_in_abbreviation_is_flagged()
    {
        using var tmp = new TempLibrary();
        var r = tmp.Library.ImportText("@inproceedings{k, title={30th Int'l Conf. on Things}, year=2020}", "biblatex");
        Assert.Contains(r.Warnings, w => w.Contains("baked-in"));
    }
}

public class SearchTests
{
    [Fact]
    public void Fts_finds_by_title_and_author()
    {
        using var tmp = new TempLibrary();
        tmp.Library.ImportText("@article{k, author={Hopper, Grace}, title={Compilers and Automata}, year=1952}", "biblatex");

        var byTitle = tmp.Library.SearchIds("automata");
        Assert.Single(byTitle);
        var byAuthor = tmp.Library.SearchIds("hopper");
        Assert.Single(byAuthor);
        var none = tmp.Library.SearchIds("quantum");
        Assert.Empty(none);
    }
}

public class MergeTests
{
    [Fact]
    public void Merge_persons_is_reversible()
    {
        using var tmp = new TempLibrary();
        tmp.Library.ImportText("@article{a, author={Hull, Richard}, title={One}, year=2001, doi={10.1/a}}", "biblatex");
        tmp.Library.ImportText("@article{b, author={Hull, R.}, title={Two}, year=2002, doi={10.1/b}}", "biblatex");

        Guid richard, initial;
        using (var ctx = tmp.Library.CreateContext())
        {
            richard = ctx.Persons.First(p => p.Given == "Richard").Uuid;
            initial = ctx.Persons.First(p => p.Given == "R.").Uuid;
            Assert.Equal(2, ctx.Persons.Count());
        }

        var merge = new MergeService(tmp.Library);
        var logUuid = merge.MergePersons(richard, initial);

        using (var ctx = tmp.Library.CreateContext())
            Assert.Equal(1, ctx.Persons.Count());

        merge.Revert(logUuid);
        using (var ctx = tmp.Library.CreateContext())
            Assert.Equal(2, ctx.Persons.Count());
    }

    [Fact]
    public void Merge_items_absorbs_identifiers()
    {
        using var tmp = new TempLibrary();
        tmp.Library.ImportText("@article{a, title={Same Paper}, year=2020, doi={10.1/keep}}", "biblatex");
        tmp.Library.ImportText("@article{b, title={Same Paper}, year=2020, isbn={9780000000000}}", "biblatex");

        Guid survivor, absorbed;
        using (var ctx = tmp.Library.CreateContext())
        {
            var items = ctx.Items.OrderBy(i => i.Id).ToList();
            survivor = items[0].Uuid;
            absorbed = items[1].Uuid;
        }

        new MergeService(tmp.Library).MergeItems(survivor, absorbed);
        var kept = tmp.Library.GetItem(survivor);
        Assert.NotNull(kept);
        Assert.Equal(2, kept!.Identifiers.Count);
        Assert.Equal(1, tmp.Library.CountItems());
    }
}

public class ProjectExportTests
{
    [Fact]
    public void Project_export_applies_projection_and_is_repeatable()
    {
        using var tmp = new TempLibrary();
        var r = tmp.Library.ImportText(
            "@article{k, author={Doe, Jane}, title={International Study}, journal={International Journal of Examples}, year=2020}",
            "biblatex");
        var itemUuid = r.AddedUuids[0];

        var projects = new ProjectService(tmp.Library);
        var pUuid = projects.CreateProject("Paper A");
        projects.AddMembers(pUuid, new[] { itemUuid });

        var set = new ProjectionSet();
        set.JournalAbbreviations.Add(new AbbreviationRule
        { Match = "International Journal of Examples", Abbreviation = "Int'l J. Examples" });
        projects.SetProjections(pUuid, set);

        var export = new ExportService(tmp.Library);
        var bib1 = export.ExportProject(pUuid, "biblatex");
        var bib2 = export.ExportProject(pUuid, "biblatex");

        Assert.Equal(bib1, bib2);                       // repeatable
        Assert.Contains("Int'l J. Examples", bib1);     // projection applied

        // Canonical DB is untouched.
        var item = tmp.Library.GetItem(itemUuid);
        Assert.Equal("International Journal of Examples", item!.Journal!.Name);
    }
}

public class CorpusRoundTripTests
{
    private const string MessyCorpus = """
    @string{lncs = {Lecture Notes in Computer Science}}

    @inproceedings{vandenBerg2019,
      author = {van den Berg, Jan and G{\"o}del, Kurt},
      title = {On {NP}-Complete R{\'e}seaux},
      crossref = {bpm2019},
      pages = {10--25},
      year = {2019},
      doi = {10.1/paper}
    }

    @proceedings{bpm2019,
      title = {Business Process Management},
      editor = {Editor, First and Second, Ann},
      series = lncs,
      year = {2019},
      isbn = {9783030266189}
    }

    @book{knuth1997,
      author = {Knuth, Donald E.},
      title = {The Art of Computer Programming},
      publisher = {Addison-Wesley},
      year = {1997},
      isbn = {9780201896831}
    }

    @phdthesis{smith2020,
      author = {Smith, Jane},
      title = {A Dissertation on {\"O}sterreich},
      school = {ETH},
      year = {2020}
    }
    """;

    [Fact]
    public void Corpus_survives_bib_export_and_reimport()
    {
        using var a = new TempLibrary();
        var imported = a.Library.ImportText(MessyCorpus, "biblatex");
        Assert.Equal(4, imported.Added);

        var bib = new ExportService(a.Library).ExportItems(null, "biblatex");

        using var b = new TempLibrary();
        b.Library.ImportText(bib, "biblatex");
        Assert.Equal(4, b.Library.CountItems());

        using var ctx = b.Library.CreateContext();
        var titles = ctx.Items.Select(i => i.Title).ToList();
        Assert.Contains("On NP-Complete Réseaux", titles);
        Assert.Contains("The Art of Computer Programming", titles);
        Assert.Contains("A Dissertation on Österreich", titles);
    }
}
