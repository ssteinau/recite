using Recite.Core.Csl;

namespace Recite.Core.Formats;

/// <summary>The trivial adapter: CSL-JSON is already the canonical form.</summary>
public sealed class CslJsonFormat : IReferenceReader, IReferenceWriter
{
    public string Id => "csljson";
    public string DisplayName => "CSL-JSON";
    public IReadOnlyList<string> Extensions => new[] { ".json" };
    public string Extension => ".json";

    public IReadOnlyList<CslDocument> Read(string text) => CslJson.ParseArray(text);

    public string Write(IEnumerable<CslDocument> documents) => CslJson.WriteArray(documents);
}
