# Phase 5 Transient Native List Marshalling Report

## Result

Phase 5 is implemented in the current Jujutsu changeset. Transient `_alpm_list_t*` pointer lists passed into P/Invoke boundaries no longer perform individual `malloc` and `free` operations per node via `alpm_list_add`. Instead, they are constructed using a stack-first `ref struct` (`TransientAlpmListBuilder`) with automatic exact-sized native heap fallback.

## Design Trade-off Evaluation

The initial plan (§7.2 of `reports/libalpm-refactor-audit.md`) proposed a thread-static multi-chunk bump allocator (`UnmanagedScratchArena`). During code review and evaluation, that design was refined:

1. **Call frequency tiers (ADR 0020)**: Transient P/Invoke list construction occurs in Tier 3 (discrete events such as `CheckDependencies`) or Tier 2 (bursty network I/O in `UpdateDatabases`), rather than tight CPU callback loops.
2. **Known size**: The node count $N$ is known upfront from collection counts, and each `_alpm_list_t` node has a fixed 24-byte layout.
3. **Leak elimination & RAII**: Rather than retaining a 64 KiB unmanaged chunk indefinitely in thread-local storage (which leaks on ephemeral worker threads), `TransientAlpmListBuilder` is an `IDisposable ref struct` that carves nodes from a caller-provided stack span (`stackalloc _alpm_list_t[N]`) and falls back to a single exact-sized `NativeMemory.Alloc` when exceeded, guaranteeing complete cleanup on scope exit.

## Implemented

- **`INativeWrapper` ([`src/Orson.Alpm/Interop/INativeWrapper.cs`](../src/Orson.Alpm/Interop/INativeWrapper.cs))**:
  Internal interface exposing `void* BackingStruct` for uniform pointer extraction. Implemented by `PackageView` and `Database` with zero boxing overhead.
- **`TransientAlpmListBuilder` ([`src/Orson.Alpm/Interop/TransientAlpmListBuilder.cs`](../src/Orson.Alpm/Interop/TransientAlpmListBuilder.cs))**:
  Stack-first `ref struct` builder with contiguous array linking, preserving libalpm's circular tail back-link invariant (`head->prev == tail`). Supports multiple sequential lists in a single scope by slicing `_stack`.
- **P/Invoke Call Sites**:
  - `AlpmSession.cs`: `CheckDependencies`, `CheckConflicts`, and `ResolveDependency`.
  - `AlpmSession.Seams.cs`: `UpdateDatabases`.
  - `SyncDatabase.Seams.cs`: `Update`.
  - `PackageView.cs`: `FindSatisfier` and `GetNewVersion`.
- **Cleanup**:
  - Removed `UnmanagedScratchArena.cs`.
  - Cleaned `NativeListMarshal.cs`, removing obsolete `alpm_list_add` pointer list builders.
- **Unit Tests & Benchmarks**:
  - `ListOwnershipTests.cs`: Validates ordering, tail back-link, multi-list coexistence, and 3,000-node heap fallback with `mallinfo2` verification.
  - `NativeListScratchBenchmarks.cs`: Benchmarks `Heap_AlpmListAdd` vs. `TransientAlpmListBuilder`.

## Verification

- `dotnet build`: Succeeded with 0 warnings and 0 errors.
- `dotnet test`: 365 passed, 0 failed, 0 skipped.
