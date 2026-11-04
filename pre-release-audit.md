# Pre-Release Audit: Orson.Alpm

## Release Strategy Recommendation

Release `Orson.Alpm` as a **preview package** (`0.1.0-preview.1` or `1.0.0-preview.1`). Do not publish a stable `1.0.0`
package before `Orson.Alpm.FSharp` is complete.

1. **`InternalsVisibleTo` coupling:** `Orson.Alpm.FSharp` binds to `Orson.Alpm` through internal `Try*` seams (ADR
   0001). Any change to internal signatures breaks binary compatibility between mismatched package versions.
2. **Seam inventory alignment:** ADR 0001 and the F# design (`src/Orson.Alpm.FSharp/DESIGN.md`) have been clarified to distinguish operational ROP boundaries from in-memory exception boundaries.
3. **API surface validation:** Implementing the F# computation expressions and pipeline modules will test the shared
   public types (`PackageView`, `Depend`, `AlpmEvent`, `AlpmQuestion`, `PackageSnapshot`). A preview tag allows public
   API adjustments without a major version bump.

---

## Verified Areas

- **Compilation:** `Orson.Alpm` and `Orson.Alpm.Tests` build with 0 errors and 0 warnings in both Debug and Release configurations.
- **Test suite:** All 303 unit and integration tests pass in both Debug and Release configurations (`dotnet test` and `dotnet test -c Release`).
- **XML documentation:** 100% coverage across public API surface with 0 CS1591 warnings (`dotnet build src/Orson.Alpm -p:NoWarn=""`). All RCS1139 doc comment warnings resolved.
- **DocFX documentation:** Site builds cleanly with 0 warnings and 0 errors (`dotnet docfx docfx/docfx.json`).
- **Public API boundary (`Microsoft.CodeAnalysis.PublicApiAnalyzers`):**
  - All public types reside in the `Orson.Alpm` namespace.
  - `Orson.Alpm.Bindings` is `internal` (ADR 0006).
  - No `nint`, `nuint`, or raw pointers appear in public signatures (ADR 0016).
  - Public enums match native `libalpm` numbering; flag enums provide `None = 0` for default/all-cleared states per FDG (ADR 0019).
- **Lifetime and GC safety:**
  - `LifetimeStamp` and `LifetimeFacet` guard all borrowed views (`PackageView`, `Database`, `AlpmList<T>`, `FileList`,
    `AlpmEvent`, `AlpmQuestion`).
  - `SafeAlpmHandle` lease overloads (94 methods in `NativeMethods.SafeHandle.cs`) and `GC.KeepAlive` anchors protect
    native pointers and `alpm_errno` reads.
- **Dependencies:**
  - `ZLinq` dependency completely removed from `Orson.Alpm.csproj` and `Directory.Packages.props`. Full repository history confirms `tests/Orson.Alpm.Tests` never used it either; all internal LINQ micro-allocations were refactored to zero-allocation loops.

---

## Pre-Release Checklist

### 1. Packaging & Versioning

- [x] **Set preview version tag (`src/Orson.Alpm/Orson.Alpm.csproj`):**
  Update `<Version>0.1.0</Version>` to `<Version>0.1.0-preview.1</Version>` (or `1.0.0-preview.1`) so `dotnet pack` generates a pre-release package instead of an unintended stable release.
- [x] **Add missing NuGet metadata (`src/Orson.Alpm/Orson.Alpm.csproj`):**
  Add `<RepositoryType>git</RepositoryType>` and `<PackageTags>alpm;pacman;archlinux;package-management;bindings;csharp</PackageTags>`.
- [x] **Gate the `GenerateBindings` Cargo target (`src/Orson.Alpm/Orson.Alpm.csproj:15-16`):**
  Gated behind `'$(CI)' == 'true'` or explicit `-p:GenerateBindings=true`. Local builds use committed bindings by default.
- [x] **Exclude `Orson.Alpm.FSharp` from packing (`src/Orson.Alpm.FSharp/Orson.Alpm.FSharp.fsproj`):**
  Configured `<IsPackable>false</IsPackable>` until the F# implementation replaces placeholder `Library.fs`.

### 2. Source Link & Debugging Symbols

- [x] **Reference Source Link provider (`Directory.Packages.props`, `Orson.Alpm.csproj`):**
  Added `Microsoft.SourceLink.Gitea`. When used as the single provider, it implicitly infers the host from `RepositoryUrl` without requiring explicit `SourceLinkGiteaHost` mapping.
- [x] **Configure Source Link build properties (`src/Orson.Alpm/Orson.Alpm.csproj`):**
  Enabled `<PublishRepositoryUrl>true</PublishRepositoryUrl>`, `<EmbedUntrackedSources>true</EmbedUntrackedSources>`, and `<ContinuousIntegrationBuild Condition="'$(CI)' == 'true'">true</ContinuousIntegrationBuild>`.
- [x] **Configure symbol package distribution (`src/Orson.Alpm/Orson.Alpm.csproj`):**
  Configured `<IncludeSymbols>true</IncludeSymbols>` and `<SymbolPackageFormat>snupkg</SymbolPackageFormat>`, generating both `.nupkg` and `.snupkg` artifacts on pack.

### 3. Documentation & Project Renaming Debt

- [x] **Fix DocFX project path (`docfx/docfx.json`):**
  Update source file from `Pacpar.Alpm/Pacpar.Alpm.csproj` to `Orson.Alpm/Orson.Alpm.csproj`.
- [x] **Update DocFX app metadata (`docfx/docfx.json`):**
  Update `_appName` and `_appTitle` from `Pacpar.Alpm` to `Orson.Alpm`.
- [x] **Clean stale DocFX artifacts & verify build:**
  Purge stale `Pacpar.Alpm.*.yml` files in `docfx/obj/api/` and confirm `dotnet docfx docfx/docfx.json` builds with 0 warnings.
- [x] **Update consumer-facing documentation:**
  Replace legacy `Pacpar.Alpm` references with `Orson.Alpm` in `docfx/index.md`, `docfx/docs/introduction.md`, and `docfx/docs/getting-started.md`.
- [x] **Correct test runner documentation (`tests/Orson.Alpm.Tests/README.md`):**
  Fix test command paths from `src/Orson.Alpm.Tests/` to `tests/Orson.Alpm.Tests/`.
- [x] **Update coverage ledger script (`src/Orson.Alpm/COMPLETENESS.md`):**
  Fix recount script directory from `src/Pacpar.Alpm` to `src/Orson.Alpm`.

### 4. Code Quality & Analyzer Warnings

- [x] **Fix RCS1139 doc comment warnings:**
  Add missing `<summary>` tags to constructor doc comments in `Database.cs:74`, `Groups.cs:15`, `AlpmList.cs:208`, `AlpmStringList.cs:17`, `PackageView.cs:13`, and `FileList.cs:22`.
- [x] **Resolve or suppress RCS1194 exception constructor warnings:**
  Implement standard exception constructors in `Exceptions.cs` (`AlpmException`, `AlpmNativeFailureException`, `AlpmLifetimeException`), or explicitly suppress `RCS1194` in `.editorconfig` if non-standard constructors are domain-enforced.
