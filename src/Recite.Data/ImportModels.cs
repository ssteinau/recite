using Recite.Core.Citations;

namespace Recite.Data;

public sealed class ImportOptions
{
    /// <summary>Skip items whose external identifier already exists in the library.</summary>
    public bool SkipExactDuplicates { get; set; } = true;

    /// <summary>Citation-key scheme used to mint the library-default key for imported items.</summary>
    public string DefaultKeyScheme { get; set; } = CitationKeyScheme.Default;
}

public sealed record ImportResult(
    int Added,
    int SkippedDuplicates,
    IReadOnlyList<Guid> AddedUuids,
    IReadOnlyList<string> Warnings);
