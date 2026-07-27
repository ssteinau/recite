# Recite

A completely vibe coded application to see what vive-coding is like. No guarantees on anything.

> A bibliography manager done right — a Citavi clone with the pain points fixed.
> One consistent, deduplicated, integrity-checked database of all your literature,
> plus per-paper **projections** that shape output without ever mutating the database.

## What it does

- **Stable identity** — every reference gets an immutable UUID at creation; DOIs/ISBNs/… are
  *external identifiers* (matching signals), never the primary key.
- **CSL-JSON canonical schema** with **biblatex / RIS / CSL-JSON** reader/writer adapters
  (a real hand-written BibTeX parser: `@string` macros, `#` concatenation, nested braces,
  LaTeX accents, `crossref`).
- **Normalized entities** — Persons, Journals, Series factored out and deduped; three
  container shapes (journal, container reference, series) with parent→child `crossref` links.
- **Faceted categories** — multiple independent, orthogonal category trees.
- **Dedup & merge** — exact (shared identifier) + fuzzy (title+year+type+authors, blocked)
  into a human review queue; every merge recorded in a reversible `MergeLog`.
- **Projects & projections** — a project is a curated, category-independent membership plus a
  citation-key scheme and a stack of output transforms (journal/series abbreviations, string
  substitutions, per-item overrides). Export is a **pure function** `render(data, projections)`
  that never touches the database → repeatable, byte-for-byte `.bib` output.
- **Full-text search** (SQLite FTS5), **attachment linking** (opens in the system viewer),
  and a **git-friendly text tree** written on an explicit "Checkpoint".

## Layout

```
Recite.Core    domain model, CSL-JSON, format adapters, dedup, projection engine, key gen  (no framework deps)
Recite.Data    EF Core + SQLite + migrations + FTS5 + import/export/merge/project/text-export services
Recite.App     Avalonia + CommunityToolkit.Mvvm  (Library, Duplicates, Persons, Categories, Venues, Projects)
Recite.Tests   xUnit — bib/RIS round-trip, projection determinism, dedup, merge reversibility,
               persistence identity, messy-corpus round-trip, and a headless UI smoke test
```

## Build & test

```sh
dotnet build Recite.slnx
dotnet test  tests/Recite.Tests/Recite.Tests.csproj
```

## Run

```sh
./scripts/run.sh
```

On first launch an empty library (default: `~/.config/Recite/Library` or the OS app-data
folder) is seeded with a small demo corpus. Point elsewhere with the `RECITE_LIBRARY`
environment variable.

> **NixOS note:** the prebuilt Skia/X11 native libraries can't find their transitive system
> deps on the default loader path, so `scripts/run.sh` discovers `libfontconfig`, `libX11`,
> `libICE`, `libGL`, … in the nix store and assembles `LD_LIBRARY_PATH` for the run only. On a
> standard FHS distro the native assets resolve on their own and `dotnet run` is enough.

## Known warnings

Two transitive dependency advisories (`NU1903`) surface from the framework/UI stack:
`SQLitePCLRaw.lib.e_sqlite3` (bundled by EF Core 10's SQLite provider) and
`Tmds.DBus.Protocol` (Avalonia's Linux D-Bus support); these are the framework's own transitive versions.
