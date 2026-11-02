# Native errno capture, numeric typing, and public exception mapping

---
status: accepted
---

libalpm reports operation errors via a single mutable `pm_errno` field per handle.
libalpm does not clear `pm_errno` on success.
This document fixes three related rules for handling native errno values in C#:
1. When errno may be read.
2. What type represents errno on public APIs.
3. How errno maps to public exception types.

## 1. When Errno May Be Read (Reading Discipline)

Reading errno after a successful call reports stale error state from an earlier call.
External callers cannot reset the handle's errno.
Therefore, errno must only be read under two strict conditions:

1. **Return value signals failure**:
   The return value is inspected immediately.
   The handle is passed to `NativeCall.Failure`, which reads errno in its first statement.
   No other native calls may run between the failing call and the errno read.
   If libalpm signals failure but sets no errno, the wrapper throws `AlpmNativeFailureException`.

2. **Entry point resets errno on entry**:
   Only entry points verified to clear `pm_errno` on entry may use errno to disambiguate a null or empty return value.
   In this library, only `Database.LoadPackageCache` and `Database.LoadGroupCache` qualify.
   Other property getters (such as `Database.Servers` and `Database.CacheServers`) deliberately do not check errno.

## 2. Public Errors Expose a Numeric Native Code (`int`)

The generated enum `_alpm_errno_t` is internal (ADR 0006).
Public APIs expose native error codes as plain `int` values.
This applies to `AlpmSession.Errno`, `AlpmException.Errno`, and exception constructors.

We rejected mirroring `_alpm_errno_t` with a public C# enum:
Arch Linux rolls continuously, so libalpm's errno values can change or expand.
An `int` makes no false stability promises.
Consumers requiring stable semantic matching must use `AlpmFailureCode` via the F# layer or internal Try seam (ADR 0001).

## 3. One Errno Maps to One Public Exception Type

Every libalpm errno maps to exactly one public exception type, regardless of which entry point reports it.
The exception hierarchy answers *which category of operation failed*.
Specific failure data travels inside typed payload properties.

In particular, the four invalid-package errnos always map to their specific exception subclasses:
- `ALPM_ERR_PKG_INVALID` $\rightarrow$ `AlpmPackageException.InvalidPackage`
- `ALPM_ERR_PKG_INVALID_CHECKSUM` $\rightarrow$ `AlpmPackageException.InvalidPackageChecksum`
- `ALPM_ERR_PKG_INVALID_SIG` $\rightarrow$ `AlpmPackageException.InvalidPackageSignature`
- `ALPM_ERR_PKG_INVALID_ARCH` $\rightarrow$ `AlpmPackageException.InvalidPackageArchitecture`

Payload-carrying exceptions retain their payload snapshots whether constructed via transaction failures or generic error paths.

## Consequences

- Successful native calls never read errno. Stale errors cannot cause false failures.
- C# public API depends on no generated native types.
- Exception filtering by type (`catch (AlpmPackageException.InvalidPackage)`) is deterministic across all entry points.
- Future agents adding native call wrappers must verify return code conventions before reading errno.

## Supersedes

- ADR 0007: One errno maps to one public exception type
- ADR 0009: Public errors expose a numeric native code, not a generated type
- ADR 0012: Read errno only after a call whose return value says it failed

## See also

- [ADR 0001: F# interop boundary and Try seam](./0001-fsharp-interop-boundary-and-try-seam.md)
- [ADR 0006: Ship no escape hatch into the binding layer](./0006-ship-no-escape-hatch-into-the-binding-layer.md)
- [ADR 0008: Cleanup failures are exposed, never thrown from Dispose](./0008-cleanup-failures-are-exposed-not-thrown.md)
