# F# binds through an internal C# `Try*` seam over a managed failure object

---
status: accepted
---

`Pacpar.Alpm.FSharp` builds on top of `Pacpar.Alpm` rather than declaring its own P/Invoke layer.
The C# layer centralizes the native ABI, lifetime tracking, and memory management.
The F# layer owns language ergonomics and error presentation.
The internal bridge between assemblies is an allocation-free `Try*` seam.
C# operations delegate to this seam and project failures as exceptions.
F# operations call the seam directly and map failures to operation-specific discriminated unions.

## 1. Assembly Boundary and Friend Access

`Pacpar.Alpm.FSharp` accesses the internal C# core through `[InternalsVisibleTo("Pacpar.Alpm.FSharp")]`.
Both assemblies ship together.
We rejected a separate contract assembly or a public C# `Try*` API:
a public `Try*` API doubles the public surface before C# users request it.
F# code must never call `NativeMethods` directly.
F# code must never inspect raw pointers or invoke native destructors.

## 2. Failure Boundary: ROP vs. Exceptions

The internal bridge separates three kinds of outcomes:

1. **Expected Operational Outcomes (ROP `Result`)**:
   Belong in `Result<'T, 'Error>`. These represent anticipated domain outcomes arising from interaction with external environmental state (the filesystem, package repositories, mirrors, native lockfiles, database headers, and cryptographic signatures). Correct code encountering real-world external conditions must handle these as normal business flow.
   - *Examples*:
     - Archive missing, corrupted, or unsupported architecture (`TryLoadPackage`, `TryCheckMd5Sum`, `TryGetMtree`).
     - Unsatisfied dependencies, conflicting packages, or disk file collisions (`TryPrepare`, `TryCommit`, `TryAddPackage`).
     - Lockfile contention when another pacman instance holds `db.lck` (`TryCreate`, `TryBeginTransaction`).
     - Network timeout or HTTP 404 during synchronization (`TryUpdate`, `TryUpdateDatabases`, `TryFetchPackageUrls`).
     - Corrupted database cache or signature verification failure (`TryLoadPackageCache`, `TryValidate`).

2. **Non-Business Faults and Contract Violations (Exceptions)**:
   Must throw exceptions. These represent bugs, violated invariants, invalid state transitions, in-memory setup mistakes, or unrecoverable system failures. Business logic cannot meaningfully branch on them.
   - *Contract & argument violations*: Passing `null`, empty paths, or invalid inputs throws `ArgumentNullException` or `ArgumentException` (`ALPM_ERR_WRONG_ARGS`).
   - *In-memory configuration faults*: Setting scalar options, mutating option lists (`AlpmOptions`, `AlpmOptionList<T>`), configuring callback delegates (`AlpmCallbacks`), setting database usage (`Database.Usage`), or appending mirror URLs (`Database.AddServer`). In `libalpm`, these are simple in-memory linked-list or struct assignments; they do not touch I/O or network and fail only on invalid arguments or out-of-memory. They throw on invalid input.
   - *State machine & lifecycle violations*: Invoking operations on disposed wrappers, reading stale borrowed views, calling commit before prepare, calling interrupt when idle, or modifying databases during an active transaction (`ObjectDisposedException`, `AlpmLifetimeException`, `InvalidOperationException`, `ALPM_ERR_TRANS_NOT_NULL`, `ALPM_ERR_TRANS_TYPE`).
   - *Unrecoverable system failures*: Out of native memory (`ALPM_ERR_MEMORY`) maps to `OutOfMemoryException`.
   - *Teardown failures*: Cleanup failures during `Dispose` follow ADR 0008 (recorded on `ReleaseFailure`).

3. **Normal Query Misses and Pure Calculations (Plain Values / `option`)**:
   A query finding no match (e.g. searching for an uninstalled package or resolving a dependency satisfier) is a valid query result returning `None` / empty collection, not an error. Pure in-memory calculations (e.g. `CheckDependencies`, `CheckConflicts`) return their computed collections directly without an error channel.

## 3. Stable Semantic Codes (`AlpmFailureCode`)

libalpm's generated `_alpm_errno_t` remains internal.
The internal bridge exposes `AlpmFailureCode`: a stable semantic enum.
The failure object also preserves the numeric `NativeCode` (`int`) for diagnostics and forward compatibility.
Valid but unmapped native errnos fall back to `AlpmFailureCode.UnknownNative`.

## 4. The `Try*` Seam Shape

Internal `Try*` methods follow standard BCL conventions:

```csharp
// Session creation (static)
internal static bool TryCreate(string root, string dbpath,
  [NotNullWhen(true)] out AlpmSession? session,
  [NotNullWhen(false)] out AlpmFailure? failure);

// Session package loading
internal bool TryLoadPackage(string filename, bool full, SigLevel level,
  [NotNullWhen(true)] out LoadedPackage? package,
  [NotNullWhen(false)] out AlpmFailure? failure);

// Transaction preparation
internal bool TryPrepare([NotNullWhen(false)] out AlpmFailure? failure);
```

Public C# methods wrap their internal `Try*` twins in one line:

```csharp
public void Prepare()
{
  if (!TryPrepare(out var failure))
    throw failure.ToException("Failed to prepare transaction");
}
```

### The `AlpmFailure` Object

```csharp
internal sealed class AlpmFailure
{
  internal AlpmFailureCode Code { get; }
  internal int NativeCode { get; }
  internal string? Message { get; }
  internal string Operation { get; }
  internal AlpmFailureDetail? Detail { get; }

  internal Exception ToException(string? context = null);
  internal static AlpmFailure Of(int errno, string operation);
  internal static unsafe AlpmFailure Take(int errno, _alpm_list_t* data, string operation);
}
```

`AlpmFailure.Take` consumes native error payloads immediately.
It marshals them into safe managed snapshots (`AlpmFailureDetail`) and frees the native list.

## 5. Operations Providing a `Try*` Twin

Only operations reporting expected operational failures against external environmental state get a `Try*` twin:

| Type | `Try*` Twin | Operational Failure Modes |
|---|---|---|
| `AlpmSession` | `TryCreate(string, string, out AlpmSession?, out AlpmFailure?)` | Database path inaccessible, permission denied, lock contention |
| | `TryLoadPackage(string, bool, SigLevel, out LoadedPackage?, out AlpmFailure?)` | File not found, corrupted archive, signature failure, bad arch |
| | `TryBeginTransaction(TransactionFlags, out Transaction?, out AlpmFailure?)` | Database lock contention (`db.lck` held by another process) |
| | `TryRegisterSyncDatabase(string, SigLevel, out Database?, out AlpmFailure?)` | Database file missing or corrupted |
| | `TryUpdateDatabases(IEnumerable<Database>, bool, out bool, out AlpmFailure?)` | Network download failure, HTTP error, mirror unreachable |
| | `TryFetchPackageUrls(IEnumerable<string>, out IReadOnlyList<string>?, out AlpmFailure?)` | Network transfer failure, connection drop |
| `Database` | `TryLoadPackageCache(out AlpmList<PackageView>?, out AlpmFailure?)` | Local/sync database archive corrupted or invalid |
| | `TryLoadGroupCache(out AlpmList<Group>?, out AlpmFailure?)` | Group database metadata missing or corrupted |
| | `TryUpdate(bool, out bool, out AlpmFailure?)` | Network failure syncing repository database |
| | `TryValidate(out AlpmFailure?)` | Database PGP signature invalid or untrusted |
| | `TrySearch(IEnumerable<string>, out IReadOnlyList<PackageView>?, out AlpmFailure?)` | Invalid search regex from user query |
| `Transaction` | `TryPrepare(out AlpmFailure?)` | Unsatisfied dependencies, package conflicts |
| | `TryCommit(out AlpmFailure?)` | File conflicts on disk, insufficient disk space, hook failure |
| | `TryAddPackage(PackageView, out AlpmFailure?)` | Duplicate target, package conflict |
| | `TryAddPackage(LoadedPackage, out PackageView?, out AlpmFailure?)` | Target validation failure, conflict |
| | `TryRemovePackage(PackageView, out AlpmFailure?)` | Package cannot be removed (required by another package) |
| | `TrySystemUpgrade(bool, out AlpmFailure?)` | Unresolvable upgrade dependencies |
| `PackageBase` | `TryCheckMd5Sum(out bool, out AlpmFailure?)` | Package archive corrupted or unreadable on disk |
| | `TryGetMtree(out PackageMtreeReader?, out AlpmFailure?)` | Package mtree archive corrupted on disk |
| `PackageView` | `TrySetReason(PackageReason, out AlpmFailure?)` | Database write failure updating package install reason |

**Operations explicitly excluded from `Try*` twins:**
- In-memory configuration setters and collections (`AlpmOptions`, `AlpmOptionList<T>`, `AlpmCallbacks`, `Database.Usage`, `Database.AddServer`): these modify in-memory structs without I/O or network; failures represent programming bugs or OOM and throw exceptions.
- Pure in-memory queries and calculations (`CheckDependencies`, `CheckConflicts`, `ResolveDependency`, `FindSatisfier`): these return computed data or `option` directly; missing matches are normal query misses.
- State-machine invariant checks (`Unlock`, `Interrupt`, `Unregister`): calling these in invalid states violates runtime preconditions and throws `InvalidOperationException`.
- Teardown operations (`Dispose`): follow ADR 0008 (cleanup failures recorded on `ReleaseFailure`).

Teardown operations (`Dispose`) do not use `Try*` twins.
They follow ADR 0008: cleanup failures are recorded on `Transaction.ReleaseFailure` rather than thrown.

## Consequences

- The C# throwing facade and the F# `Result` facade delegate to one core. Error semantics cannot drift.
- Native payload ownership is centralized in `AlpmFailure.Take`.
- C# internals stay accessible to F# via friend assembly.
- C# public API remains clean and exception-based.
- Future agents must implement new fallible native operations as an internal `Try*` twin first, then add the throwing public C# wrapper.

## Supersedes

- ADR 0001: F# binds through the C# layer
- ADR 0002: Keep native failure interpretation inside C#
- ADR 0003: Stable failure codes for F#
- ADR 0005: Keep the F# layer as a friend assembly
- ADR 0013: The failure-returning core is a `Try*` seam over a managed failure object

## See also

- [ADR 0006: Ship no escape hatch into the binding layer](./0006-ship-no-escape-hatch-into-the-binding-layer.md)
- [ADR 0007: Native errno capture and exception mapping](./0007-native-errno-and-exception-contract.md)
- [ADR 0008: Cleanup failures are exposed, never thrown from Dispose](./0008-cleanup-failures-are-exposed-not-thrown.md)
