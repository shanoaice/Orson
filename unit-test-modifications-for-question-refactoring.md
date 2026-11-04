# Unit Test Modifications for Question Architecture Refactoring

## 1. Executive Summary

We refactored the question callback system in `Orson.Alpm`.
The legacy `AlpmQuestion` class hierarchy is gone.
We now have seven granular `ref struct` types.
We also added seven granular hook properties on `AlpmCallbacks`.
Existing unit tests in `tests/Orson.Alpm.Tests/` now require updates.
This report identifies every affected test file and describes the required changes.

---

## 2. Affected Test Locations Overview

| File Path | Nature of Changes | Priority |
| :--- | :--- | :--- |
| `tests/Orson.Alpm.Tests/Unit/Callbacks/QuestionCallbackTests.cs` (Move from `Unit/Snapshots/QuestionPayloadDetailTests.cs`) | Move to `Unit/Callbacks/`, fix compilation for `ref struct`, add exhaustive test coverage across all 7 question types, verify answer write-backs and lifetime invalidation. | High |
| `tests/Orson.Alpm.Tests/Unit/Snapshots/ManagedSnapshotTests.cs` | Remove question snapshot tests because questions are no longer snapshots. | High |
| `tests/Orson.Alpm.Tests/Unit/Callbacks/CallbackRegistrationTests.cs` | Update `questioncb` registration assertions and test new hook properties. | Medium |
| `tests/Orson.Alpm.Tests/Unit/GcAnchor/gc-anchor-allowlist.txt` | Remove stale `AlpmQuestion` entry from allowlist. | Medium |
| `tests/Orson.Alpm.Tests/Unit/Events/QuestionDispatchTests.cs` (New) | Add unit tests for `AlpmCallbacks.DispatchQuestion` and exception safety. | Recommended |

---

## 3. Detailed File-by-File Guidance

### 3.1. Relocate & Expand: `QuestionPayloadDetailTests.cs` -> `Unit/Callbacks/QuestionCallbackTests.cs`

#### Current Status & Location Rationale
The file currently lives in `tests/Orson.Alpm.Tests/Unit/Snapshots/QuestionPayloadDetailTests.cs`.
Because question payloads are now `ref struct` wrappers over the active callback union rather than snapshots, this file should be moved to:
`tests/Orson.Alpm.Tests/Unit/Callbacks/QuestionCallbackTests.cs` (or `QuestionPayloadDetailTests.cs` under `Unit/Callbacks/`).

#### Required Modifications & Exhaustive Case Coverage
The legacy test only verified `InstallIgnorePackageQuestion`. We should exhaustively verify payload read, answer write-back, and lifetime detachment/invalidation across all 7 question types.

1. **Question Construction & Ref Struct Lifetime Rules:**
   - Because `ref struct` cannot cross lambda closures, use standard `try...catch` or extract [`PackageView`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Package/PackageView.cs) before invalidation to assert `AlpmLifetimeException`.
   - Allocate `_alpm_question_t` zeroed on the heap or stack for each test scenario.
   - For question types requiring child lifetimes, obtain `ChildLifetime` from `_environment.AlpmSession.RootLifetime.CreateChild(...)`.

2. **Native Memory & `alpm_list_t` Helpers:**
   - Questions involving lists (`RemoveUnresolvablePackagesQuestion` and `SelectProviderQuestion`) or conflict structs (`PackageConflictQuestion`) require native list/struct allocation:
     - Use `NativeListMarshal.BuildPointerList(...)` to assemble `_alpm_list_t*` pointers.
     - Always pair with `NativeListMarshal.Free(list)` in `finally` blocks so unmanaged memory is deterministically freed.
     - Use `NativeString.ToNative(...)` for dependency and string properties, pairing with `NativeMemory.Free(...)`.

3. **Exhaustive Matrix of Question Structs to Cover:**
   - **`InstallIgnorePackageQuestion`**:
     - Reads `PackageName`, `PackageVersion`, and borrowed `Package`.
     - Answer mutation: `Install = true/false` directly updates `native->install_ignorepkg.install`.
     - `Package` invalidates when its `ChildLifetime` completes.
   - **`ReplacePackageQuestion`**:
     - Reads `OldPackageName`, `NewPackageName`, `OldPackageVersion`, `NewPackageVersion`, `NewDatabase`, `OldPackage`, `NewPackage`.
     - Answer mutation: `Replace = true/false` writes to `native->replace.replace`.
     - `OldPackage` and `NewPackage` invalidate with their respective child lifetimes.
   - **`PackageConflictQuestion`**:
     - Requires allocating `_alpm_question_conflict_t` and `_alpm_conflict_t`.
     - Reads `ConflictName`, `Package1`, `Package2`, `Reason`.
     - Answer mutation: `Remove = true/false` writes to `native->conflict.remove`.
   - **`CorruptedPackageQuestion`**:
     - Reads `FilePath` and `Reason` (strings from native pointers).
     - Answer mutation: `Remove = true/false` writes to `native->corrupted.remove`.
   - **`RemoveUnresolvablePackagesQuestion`**:
     - Requires building an `_alpm_list_t*` of packages via `NativeListMarshal.BuildPointerList`.
     - Reads `Packages` list as borrowed `PackageView` elements.
     - Answer mutation: `Skip = true/false` writes to `native->remove_pkgs.skip`.
   - **`SelectProviderQuestion`**:
     - Requires building `_alpm_list_t*` of candidate packages and native `_alpm_depend_t*`.
     - Reads `DependencyName`, `DependencyVersion`, `DependencyDescription`, and `Providers`.
     - Answer mutation: `UseIndex = n` writes to `native->select_provider.use_index`.
   - **`ImportKeyQuestion`**:
     - Reads `Uid`, `Fingerprint`.
     - Answer mutation: `Import = true/false` writes to `native->import_key.import`.

---

### 3.2. `ManagedSnapshotTests.cs`

#### Current Status
This suite verifies that managed types copy data eagerly out of native memory.
Five tests specifically test `AlpmQuestion` types:
- `QuestionPayload_CopiesItsFieldsOutOfTheCallbackUnion` (lines 321–352)
- `InstallIgnoredPackage_CopiesItsFieldsOutOfTheCallbackUnion` (lines 354–380)
- `ConflictPackage_CopiesItsFieldsOutOfTheCallbackUnion` (lines 382–434)
- `QuestionPayload_CopiesTheMemberList` (lines 441–474)
- `QuestionAnswer_WritesBackToNativeAndThrowsWhenDisarmed` (lines 504–538)

#### Required Modifications
1. **Remove obsolete question tests:**
   - Questions are no longer heap snapshots.
   - Questions are stack-allocated `ref struct` views over `_alpm_question_t*`.
   - Remove these five methods from `ManagedSnapshotTests.cs`.
2. **Relocate valid coverage:**
   - Move test logic that verifies field reads and answer mutation into question-specific unit tests.

---

### 3.3. `CallbackRegistrationTests.cs`

#### Current Status
This suite verifies native callback registration on `SafeAlpmHandle`.

#### Required Modifications
1. **Update `InitialHandle_HasNoCallbacksRegistered`:**
   - `AlpmCallbacks` now registers `questioncb` unconditionally during construction.
   - Change `Assert.True(NativeMethods.alpm_option_get_questioncb(handle) == null)` to:
     ```csharp
     Assert.True(NativeMethods.alpm_option_get_questioncb(handle) != null,
       "questioncb is registered unconditionally");
     ```
2. **Update `Handlers_RegisterWhenSet_AndUnregisterWhenNull`:**
   - Setting granular question hooks does not toggle the native `questioncb` pointer.
   - Add assertions verifying that setting and clearing granular hooks retains the native pointer.
3. **Update disposed handle tests:**
   - In `DisposedHandle_ThrowsObjectDisposedException_WhenMutatingHandler`:
   - Add checks for all seven question hooks:
     ```csharp
     Assert.Throws<ObjectDisposedException>(() => callback.OnInstallIgnorePackage = (ref InstallIgnorePackageQuestion _) => { });
     Assert.Throws<ObjectDisposedException>(() => callback.OnReplacePackage = (ref ReplacePackageQuestion _) => { });
     Assert.Throws<ObjectDisposedException>(() => callback.OnPackageConflict = (ref PackageConflictQuestion _) => { });
     Assert.Throws<ObjectDisposedException>(() => callback.OnCorruptedPackage = (ref CorruptedPackageQuestion _) => { });
     Assert.Throws<ObjectDisposedException>(() => callback.OnRemoveUnresolvablePackages = (ref RemoveUnresolvablePackagesQuestion _) => { });
     Assert.Throws<ObjectDisposedException>(() => callback.OnSelectProvider = (ref SelectProviderQuestion _) => { });
     Assert.Throws<ObjectDisposedException>(() => callback.OnImportKey = (ref ImportKeyQuestion _) => { });
     ```
4. **Update leaked handle tests:**
   - In `LeakedHandle_ThrowsAlpmLifetimeException_WhenMutatingHandler`:
   - Add checks for all seven question hooks throwing `AlpmLifetimeException`.

---

### 3.4. `gc-anchor-allowlist.txt`

#### Current Status
`tests/Orson.Alpm.Tests/Unit/GcAnchor/gc-anchor-allowlist.txt` contains:
```text
AlpmQuestion | 20 | delegated to caller | FromUnion receives the question union by parameter and copies it out eagerly...
```

#### Required Modifications
1. Remove line 29 (`AlpmQuestion`).
2. Run `GcAnchorAuditTests.cs` to ensure no stale allowlist entry warnings exist.
3. If the audit reports new hazard sites for question structs, record them according to audit rules.

---

### 3.5. Recommended New Test Suite: `QuestionDispatchTests.cs`

Create a new file `tests/Orson.Alpm.Tests/Unit/Events/QuestionDispatchTests.cs`.
Use it to test internal dispatch mechanics directly.

#### Test Cases to Implement
1. **Hook Routing:**
   - Allocate `_alpm_question_t` on the stack for each question type.
   - Call `callbacks.DispatchQuestion(&raw)`.
   - Verify that the designated hook runs.
2. **No-op When Unhandled:**
   - Call `callbacks.DispatchQuestion(&raw)` when the corresponding hook property is `null`.
   - Verify no exception occurs.
3. **Answer Mutation Verification:**
   - Assign answer properties inside the hook:
     - `InstallIgnorePackageQuestion.Install = true`
     - `ReplacePackageQuestion.Replace = true`
     - `PackageConflictQuestion.Remove = true`
     - `CorruptedPackageQuestion.Remove = true`
     - `RemoveUnresolvablePackagesQuestion.Skip = true`
     - `SelectProviderQuestion.UseIndex = 3`
     - `ImportKeyQuestion.Import = true`
   - Assert that native union fields update correctly after dispatch.
4. **Exception Containment:**
   - Set a hook that throws an exception.
   - Register a capture delegate on `callbacks.HandlerException`.
   - Call `callbacks.DispatchQuestion(&raw)`.
   - Verify that `DispatchQuestion` swallows the exception.
   - Verify that `HandlerException` captures the thrown exception.
