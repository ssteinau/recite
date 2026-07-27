using Recite.Core.Csl;

namespace Recite.Core.Formats;

/// <summary>Reads a text format into canonical CSL documents.</summary>
public interface IReferenceReader
{
    string Id { get; }
    string DisplayName { get; }
    IReadOnlyList<string> Extensions { get; }
    IReadOnlyList<CslDocument> Read(string text);
}

/// <summary>Writes canonical CSL documents to a text format.</summary>
public interface IReferenceWriter
{
    string Id { get; }
    string DisplayName { get; }
    string Extension { get; }
    string Write(IEnumerable<CslDocument> documents);
}

/// <summary>
/// Strategy registry for format adapters (PLAN §3 "IReferenceReader/IReferenceWriter
/// in a strategy registry").
/// </summary>
public sealed class FormatRegistry
{
    private readonly Dictionary<string, IReferenceReader> _readers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReferenceWriter> _writers = new(StringComparer.OrdinalIgnoreCase);

    public FormatRegistry Register(IReferenceReader reader) { _readers[reader.Id] = reader; return this; }
    public FormatRegistry Register(IReferenceWriter writer) { _writers[writer.Id] = writer; return this; }

    public IReadOnlyCollection<IReferenceReader> Readers => _readers.Values;
    public IReadOnlyCollection<IReferenceWriter> Writers => _writers.Values;

    public IReferenceReader? Reader(string id) => _readers.GetValueOrDefault(id);
    public IReferenceWriter? Writer(string id) => _writers.GetValueOrDefault(id);

    /// <summary>Pick a reader by file extension (".bib", ".ris", ".json").</summary>
    public IReferenceReader? ReaderForExtension(string ext)
    {
        ext = ext.StartsWith('.') ? ext : "." + ext;
        return _readers.Values.FirstOrDefault(r =>
            r.Extensions.Any(e => string.Equals(e, ext, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>The default registry wired with the built-in adapters.</summary>
    public static FormatRegistry CreateDefault()
    {
        var reg = new FormatRegistry();
        var bib = new BibtexFormat();
        var ris = new RisFormat();
        var csl = new CslJsonFormat();
        reg.Register((IReferenceReader)bib).Register((IReferenceWriter)bib);
        reg.Register((IReferenceReader)ris).Register((IReferenceWriter)ris);
        reg.Register((IReferenceReader)csl).Register((IReferenceWriter)csl);
        return reg;
    }
}
