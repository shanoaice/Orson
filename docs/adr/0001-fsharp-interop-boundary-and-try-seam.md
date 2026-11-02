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

## 2. Failure Boundary and Error Contract

The core seam reports expected ALPM operation failures as values.
It does not throw exceptions on expected operational failures.
The boundary distinguishes three outcomes:

1. **Success**: The method returns `true`. The result is valid; `failure` is `null`.
2. **Query miss**: The lookup completes normally with no match. The method returns `true`, result is `null`, and `failure` is `null`.
3. **ALPM operation failure**: Native libalpm rejected the operation. The method returns `false`, result is `null`, and `failure` is non-null.

The seam throws exceptions only for programmer errors and critical runtime faults:
- Argument misuse (`ArgumentNullException`, `ArgumentException`).
- Lifetime violations (`AlpmLifetimeException`, `ObjectDisposedException`).
- Out-of-memory conditions (`ALPM_ERR_MEMORY` maps to `OutOfMemoryException`).
- Missing native error state (`AlpmNativeFailureException`).

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

Only operations reporting failure through return codes or entry points that reset errno get a `Try*` twin:

| Type | `Try*` Twin |
|---|---|
| `AlpmSession` | `TryCreate(string, string, out AlpmSession?, out AlpmFailure?)` |
| | `TryLoadPackage(string, bool, SigLevel, out LoadedPackage?, out AlpmFailure?)` |
| | `TryBeginTransaction(TransactionFlags, out Transaction?, out AlpmFailure?)` |
| | `TryRegisterSyncDatabase(string, SigLevel, out Database?, out AlpmFailure?)` |
| | `TryUnregisterAllSyncDatabases(out AlpmFailure?)` |
| `Database` | `TryLoadPackageCache(out AlpmList<PackageView>?, out AlpmFailure?)` |
| | `TryLoadGroupCache(out AlpmList<Group>?, out AlpmFailure?)` |
| | `TryUnregister(out AlpmFailure?)` |
| | `TryValidate(out AlpmFailure?)` |
| `Transaction` | `TryPrepare(out AlpmFailure?)` |
| | `TryCommit(out AlpmFailure?)` |
| | `TryAddPackage(PackageView, out AlpmFailure?)` |
| | `TryAddPackage(LoadedPackage, out PackageView?, out AlpmFailure?)` |
| | `TryRemovePackage(PackageView, out AlpmFailure?)` |
| | `TrySystemUpgrade(bool, out AlpmFailure?)` |
| | `TryInterrupt(out AlpmFailure?)` |

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
