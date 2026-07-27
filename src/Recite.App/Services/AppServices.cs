using System;
using System.IO;
using Recite.Core.Formats;
using Recite.Data;

namespace Recite.App.Services;

/// <summary>
/// Composition root for the app: opens the library and exposes the Data services.
/// One process = one open library (PLAN §5, single-library model).
/// </summary>
public sealed class AppServices
{
    public LibraryConnection Connection { get; private set; }
    public LibraryService Library { get; private set; }
    public ExportService Export { get; private set; }
    public MergeService Merge { get; private set; }
    public ProjectService Projects { get; private set; }
    public TextExporter Text { get; private set; }
    public FormatRegistry Formats { get; }

    public string LibraryRoot => Connection.Paths.Root;

    public AppServices(string? root = null)
    {
        Formats = FormatRegistry.CreateDefault();
        Connection = LibraryInitializer.Open(root ?? LibraryPaths.DefaultRoot);
        Library = new LibraryService(Connection, Formats);
        Export = new ExportService(Library);
        Merge = new MergeService(Library);
        Projects = new ProjectService(Library);
        Text = new TextExporter(Library);
    }

    /// <summary>Populate an empty library with a small demo corpus so first-run isn't blank.</summary>
    public void SeedSampleIfEmpty()
    {
        if (Library.CountItems() > 0) return;
        Library.ImportText(SampleCorpus, "biblatex");
        SeedFacets();
    }

    private void SeedFacets()
    {
        var approach = Library.AddFacet("Approach");
        Library.AddCategory(approach, null, "Formal methods");
        Library.AddCategory(approach, null, "Empirical study");
        Library.AddCategory(approach, null, "Systems");
        var kind = Library.AddFacet("Content Kind");
        Library.AddCategory(kind, null, "Survey");
        Library.AddCategory(kind, null, "Case study");
        Library.AddCategory(kind, null, "Tool paper");
    }

    private const string SampleCorpus = """
    @string{lncs = {Lecture Notes in Computer Science}}

    @article{codd1970,
      author = {Codd, E. F.},
      title = {A Relational Model of Data for Large Shared Data Banks},
      journal = {Communications of the ACM},
      volume = {13}, number = {6}, pages = {377--387},
      year = {1970}, doi = {10.1145/362384.362685}
    }

    @inproceedings{vandenBerg2019,
      author = {van den Berg, Jan and G{\"o}del, Kurt},
      title = {On {NP}-Complete R{\'e}seaux in Business Processes},
      crossref = {bpm2019}, pages = {10--25},
      year = {2019}, doi = {10.1007/bpm.2019.3}
    }

    @proceedings{bpm2019,
      title = {Business Process Management},
      editor = {Editor, First and Second, Ann},
      series = lncs, volume = {11675},
      publisher = {Springer}, address = {Cham},
      year = {2019}, isbn = {9783030266189}
    }

    @book{knuth1997,
      author = {Knuth, Donald E.},
      title = {The Art of Computer Programming, Volume 1},
      publisher = {Addison-Wesley}, address = {Reading, MA},
      edition = {3}, year = {1997}, isbn = {9780201896831}
    }

    @phdthesis{hull2001,
      author = {Hull, Richard},
      title = {A Dissertation on Data Integration over {\"O}sterreich},
      school = {ETH Z{\"u}rich}, year = {2001}
    }

    @article{hull2003,
      author = {Hull, R. and Codd, E. F.},
      title = {Revisiting the Relational Model},
      journal = {Communications of the ACM},
      volume = {46}, number = {2}, pages = {40--45},
      year = {2003}, doi = {10.1145/hull.2003}
    }
    """;

    public void OpenExternally(string absolutePath)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = absolutePath,
                UseShellExecute = true,
            };
            System.Diagnostics.Process.Start(psi);
        }
        catch { /* no viewer available */ }
    }

    public string ResolveAttachmentPath(string relativePath) =>
        Path.Combine(Connection.Paths.FilesDir, relativePath);
}
