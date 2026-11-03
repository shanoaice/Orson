# Bound enums mirror native numbering; member names follow FDG

---
status: accepted
---

Enums that project libalpm native constants are FFI projections, not independent managed designs.
This document fixes two rules for their values and naming.

## 1. Value Numbering Mirrors libalpm

Enums representing native constants preserve libalpm's exact numbering while adhering to .NET Framework Design Guidelines (FDG) conventions:

### Bitmask Flag Enums (`[Flags]`)
Bitmask enums (`TransactionFlags`, `DatabaseUsage`, `SigLevel`, `Capability`, `LogLevel`) support `0` natively. In C, a bitmask of `0` indicates that no flag bits are set. In libalpm, this represents a standard and well-defined state:
- `TransactionFlags`: 0 specifies standard, fully-checked transaction mode (no check-skipping or relaxation flags set).
- `DatabaseUsage`: 0 disables all database operations (neither sync, search, install, nor upgrade).
- `SigLevel`: 0 disables all signature verification (<c>Never</c> in pacman configuration).
- `Capability`: 0 indicates no optional compile-time features are supported.
- `LogLevel`: 0 indicates no log filter bits are set.

Per FDG, flag enums provide `None = 0` to denote all flags cleared / default state. This avoids forcing callers to write `default` or `(T)0`, and ensures value-type zero initialization produces a valid enum member.

### Simple Enums (Closed Domain Sets)
Enums that project 1-based native closed sets (`DepMod`, `HookWhen`, `PackageOperation`, `PackageOrigin`) retain libalpm's exact 1-based numbering without an artificial `None = 0` member:
- In libalpm, 0 is completely undefined for these sets (e.g., unconstrained dependencies use <c>ALPM_DEP_MOD_ANY = 1</c>).
- These enums are exposed strictly as read-only outputs from library operations; consumers do not construct them.
- Per FDG guidelines for external standard enums, omitting 0 is appropriate when 0 is invalid in the underlying domain.

### Library-Defined Enums
Enums that are library designs rather than native projections (such as `AlpmFailureCode` and `FetchResult`) follow standard FDG rules and carry a zero member (`None = 0` or `Success = 0`).
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
