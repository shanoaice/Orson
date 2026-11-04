# The public surface keeps native widths inside the binding layer

---
status: accepted (amended)
---

**Amendment (2026-11-04):** `nint` and `nuint` are permitted on the public API surface when modeling native address-space, pointer-sized values, or native offsets. In modern .NET (.NET 7+), `nint` is fully CLS-compliant. While domain item counts continue to prefer `int` (matching `IReadOnlyCollection<T>.Count`) and byte sizes prefer `long`, native integer types are no longer categorically banned. Public API surface drift is tracked via `Microsoft.CodeAnalysis.PublicApiAnalyzers`.

The public API uses `int` for item counts and `long` for byte sizes by convention. It does not
leak types from `Orson.Alpm.Bindings`. `nint` and `nuint` are permitted where native pointer-sized
semantics are genuinely required, but are not forced onto high-level domain counts or sizes.

1. **Counts are `int`.** A count of packages, hooks or files takes the same type as
   `FileList.Count` and `AlpmList.Count`.
2. **Byte sizes are `long`.** `off_t` is a signed 64-bit quantity. It maps to `long`, the type
   already used by `PackageBase.Size` and `AlpmDownloadEvent.Progress.Total`.
3. **The binding layer keeps the native type.** `NativeMethods` keeps `nuint` for `size_t` and
   `CLong` for `off_t`, because that is what the C signatures say. The wrapper translates once, at
   the property or callback boundary.

## Why a single-platform library still follows this

Orson.Alpm targets Linux. Every supported machine is 64-bit, so `size_t` never changes width in
practice. The rule is not about supporting 32-bit hosts. It exists for three other reasons.

- **Layering.** ADR 0006 makes the generated bindings internal. A `nuint` property puts a
  binding-layer type back into the domain API, one member at a time.
- **Language interop.** `nuint` is `System.UIntPtr` in metadata. That type is not CLS-compliant.
  A VB consumer sees `UIntPtr`, and an F# consumer sees `unativeint`, where the reader expected a
  count.
- **Stability.** Changing `nuint` to `int` later is a breaking change. The library is pre-1.0, so
  the change is cheap now.

## What the rule changes today

The public surface has eight native-width members, all of them in the event layer:

| Member | Was | Now |
|---|---|---|
| `AlpmCallbacks.ProgressHandler` | `Action<ProgressType, string, int, nuint, nuint>?` | `Action<AlpmProgressEvent>?` |
| `AlpmEvent.HookRunStart.Position` / `Total` | `nuint` | `int` |
| `AlpmEvent.HookRunDone.Position` / `Total` | `nuint` | `int` |
| `AlpmEvent.PackageRetrieve{Start,Done,Failed}.PackageCount` | `nuint` | `int` |

`off_t` needs no change: every byte size already surfaces as `long`.

## Consequences

- The progress payload is a `readonly struct`. libalpm calls the progress callback once per file
  entry while it extracts or removes a package, so a class payload would add a heap object on the
  hottest path.
- Public API additions are recorded in `PublicAPI.Unshipped.txt` and validated by
  `Microsoft.CodeAnalysis.PublicApiAnalyzers`. The prior reflection guard `PublicApiSurfaceTests` was retired.
- A new `size_t`-backed member arrives as `int` (a count) or `long` (a size). The binding keeps
  the native type.

## Rejected alternatives

- **Keep `nuint` and document the width.** The width is not part of the domain value, and XML
  documentation cannot make `UIntPtr` CLS-compliant.
- **Use `ulong` for counts.** It matches neither existing convention (`int` for counts, `long`
  for sizes) and only changes the width of the leak.
- **Ban `uint` throughout.** Out of scope. ADR 0019 owns bound enum values, which keep
  libalpm's numbering.
