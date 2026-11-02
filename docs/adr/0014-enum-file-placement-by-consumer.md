# Enum file placement is determined by primary consumer

---
status: accepted
---

The Framework Design Guidelines (FDG) govern type granularity and namespaces.
They do not prescribe which file a type belongs in.
This document establishes the repository convention for enum file placement.

## 1. Placement Rules

1. **One consumer**:
   The enum lives in the file of that consumer.
   `TransactionFlags` lives in `Transaction.cs`.
   `Capability` lives in `AlpmSession.cs`.
   `DatabaseUsage` lives in `Database.cs`.

2. **Two or more feature areas**:
   The enum gets its own file, named after the enum.
   `SigLevel` lives in `SigLevel.cs` because session options, database registration, and package loading all consume it.

3. **No central enum drawers**:
   Do not create shared collection files such as `XxxEnums.cs`.
   Grouping enums together purely because they are enums destroys domain cohesion.

## 2. Layout Mapping

| Enum | Location | Justification |
|---|---|---|
| `TransactionFlags` | `Transaction.cs` | Used only by `Transaction` |
| `Capability` | `AlpmSession.cs` | Used only by `AlpmSession.Capabilities` |
| `DatabaseUsage` | `Database.cs` | Used only by `Database.Usage` |
| `ProgressType`, `LogLevel`, `FetchResult` | `AlpmCallbacks.cs` | Used only in callback payloads |
| `DepMod` | `Dependencies.cs` | Ships with `Depend` |
| `PackageOrigin`, `PackageReason`, `PackageValidation` | `Package/PackageEnums.cs` | Package domain only |
| `HookWhen`, `PackageOperation` | `Events/AlpmEvent.cs` | Event payloads only |
| `SigLevel` | `SigLevel.cs` | Shared across session options, databases, and packages |
| `AlpmFailureCode`, `AlpmErrorCategory` | `AlpmFailure.cs` | Consumed only by `AlpmFailure` |

## Consequences

- An agent adding or moving an enum must identify its consumer count first.
- Reorganizing enums into a generic file is prohibited.

## See also

- [ADR 0018: Member shape follows work, not failure](./0018-member-shape-follows-work-not-failure.md)
- [ADR 0019: Native bound enum values and member naming](./0019-native-bound-enum-values.md)
