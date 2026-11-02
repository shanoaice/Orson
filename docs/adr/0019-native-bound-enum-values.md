# Bound enums mirror native numbering; member names follow FDG

---
status: accepted
---

Enums that project libalpm native constants are FFI projections, not independent managed designs.
This document fixes two rules for their values and naming.

## 1. Value Numbering Mirrors libalpm

Enums representing native constants (`SigLevel`, `DepMod`, `PackageOrigin`, `PackageOperation`, `HookWhen`, `PackageValidation`, `TransactionFlags`, `Capability`, `DatabaseUsage`) preserve libalpm's exact numbering:

- If libalpm starts numbering at 1, the C# enum starts at 1. `default(T)` is undefined on purpose.
- Do not invent a synthetic `None = 0` member when libalpm defines none.
- Inventing a zero member risks passing undefined constants to native calls.
- `TransactionFlags` has no `None = 0`. Native 0 means default fully checked mode.

Enums that are library designs rather than native projections (such as `AlpmFailureCode` and `FetchResult`) follow the standard FDG rule and carry a zero member.

## 2. Member and Type Naming

- **Member names**: Follow FDG PascalCase. Strip repetitive type prefixes (e.g., `ALPM_PKG_VALIDATION_MD5SUM` becomes `Md5Sum`).
- **Native constant traceability**: Record the native constant name inside the member's XML `<summary>`.
- **Type names**: Keep the singular native typedef name (`SigLevel`, `Capability`, `DatabaseUsage`, `PackageValidation`). Do not pluralize them.

## Consequences

- Direct casts between managed enums and native types remain valid across the FFI boundary.
- Member renamings do not mutate numeric values.
- Future agents adding bound enums must inspect `alpm.h` to mirror the native numeric baseline.

## See also

- [ADR 0007: Native errno capture and exception mapping](./0007-native-errno-and-exception-contract.md)
- [ADR 0014: Enum file placement is determined by primary consumer](./0014-enum-file-placement-by-consumer.md)
