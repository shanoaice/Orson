# Lifetime Dynamics of libalpm Binding Objects in Orson

## 1. Executive Summary

[Orson](file:///home/shanoaice/Projects/github/Orson) binds the unmanaged C library `libalpm` to .NET.
`libalpm` relies on manual, explicit memory management without reference counting.
The .NET runtime uses non-deterministic garbage collection.
Directly bridging these environments creates severe use-after-free and double-free hazards.

Orson implements a hybrid lifetime architecture to solve this mismatch:
1. **Critical Handle Wrapper (Pattern 1):** [`SafeAlpmHandle`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Interop/SafeHandles.cs#L10) encapsulates `_alpm_handle_t*` lifecycle and prevents early collection during P/Invoke.
2. **Generational Domain Mirroring (Pattern 4 + Distributed Stamps):** [`RootLifetime`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Interop/Lifetime.cs#L122) and [`ChildLifetime`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Interop/Lifetime.cs#L215) mirror native object hierarchies.
3. **Constant-Time Stamp Validation:** Every borrowed view carries a 32-byte [`LifetimeStamp`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Interop/Lifetime.cs#L290). Checks take two integer comparisons.
4. **Compile-Time Stack Confinement:** Transient questions use `ref struct` types ([`AlpmQuestion.cs`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Events/AlpmQuestion.cs)). Questions cannot escape to the managed heap or other threads.

This architecture converts native memory corruption into deterministic, fail-fast [`AlpmLifetimeException`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Exceptions.cs#L195) errors.

```
                              [AlpmSession]
                                    │ owns
                                    ▼
                             [SafeAlpmHandle]
                                    │ owns domain (weak ref)
                                    ▼
                             [RootLifetime] (Generation Counter)
                                    │
         ┌──────────────────────────┼──────────────────────────┬──────────────────────────┐
         ▼                          ▼                          ▼                          ▼
  [ChildLifetime]            [ChildLifetime]            [ChildLifetime]            [ChildLifetime]
  (Local Database)          (Sync DB: extra)             (Transaction)           (LoadedPackage: pkg)
         │                          │                          │                          │
         ├─> [Facet: Servers]       ├─> [Facet: Servers]       ├─> AddedPackages          └─> Disown() on hand-over
         └─> [Facet: CacheServers]  └─> [Facet: CacheServers]  └─> RemovedPackages
         │                          │                          │
         ▼                          ▼                          ▼
   [PackageView]              [PackageView]              [PackageView]
  (LifetimeStamp)            (LifetimeStamp)            (LifetimeStamp)
```

---

## 2. Architectural Infrastructure

### 2.1 The Two-Level Generational Hierarchy

Orson restricts the lifetime token hierarchy to exactly two levels:
* **Root Domain ([`RootLifetime`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Interop/Lifetime.cs#L122)):**
  * One instance exists per [`AlpmSession`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/AlpmSession.cs#L37).
  * Holds a direct reference to the managed owner (`_owner`), keeping it reachable while views exist.
  * Maintains a pointer registry (`_handles`) to deduplicate [`ChildLifetime`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Interop/Lifetime.cs#L215) tokens for sync databases.
* **Child Domain ([`ChildLifetime`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Interop/Lifetime.cs#L215)):**
  * Represents an independently releasable resource (database, transaction, loaded package, or callback frame).
  * Child domains cannot create further child domains.
  * Manages sub-resource facets via [`LifetimeFacet`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Interop/Lifetime.cs#L189).

### 2.2 Stamp Validation Mechanics

A [`LifetimeStamp`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Interop/Lifetime.cs#L290) is an immutable struct:

```csharp
internal readonly struct LifetimeStamp
{
  private readonly Lifetime _resource;
  private readonly long _resourceGeneration;
  private readonly RootLifetime _root;
  private readonly long _rootGeneration;
}
```

* Calling [`ThrowIfStale()`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Interop/Lifetime.cs#L311) compares stored generations against volatile counters on `_resource` and `_root`.
* A single generation increment on a domain invalidates all stamps issued from it in $O(1)$ time.
* It requires no view iteration or registry lock during invalidation.
* Invalidation is thread-safe on finalizer threads.

### 2.3 Lifetime Facets

A sub-resource can be released without destroying the parent resource.
For example, modifying database download servers destroys the server list but preserves cached packages.
Orson represents this relationship using [`LifetimeFacet`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Interop/Lifetime.cs#L189):
* `LifetimeFacet.Servers = 0`
* `LifetimeFacet.CacheServers = 1`

Invalidating the child domain cascades into all its facets.
Invalidating a facet does not touch the parent child domain or other facets.

---

## 3. Comprehensive Taxonomy of Binding Objects

| Type | Native Struct | Ownership Category | Disposal Pattern | Invalidation Trigger |
| :--- | :--- | :--- | :--- | :--- |
| [`SafeAlpmHandle`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Interop/SafeHandles.cs#L10) | `_alpm_handle_t*` | Root Handle | SafeHandle Critical Finalizer | [`AlpmSession.Dispose()`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/AlpmSession.cs#L578) or GC collection |
| [`AlpmSession`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/AlpmSession.cs#L37) | `_alpm_handle_t*` | Root Session | [`IDisposable`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/AlpmSession.cs#L578) | Explicit Dispose |
| [`Database`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Database.cs#L45) (Local) | `_alpm_db_t*` | Borrowed Child | Non-disposable wrapper | [`Transaction.Commit()`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Transaction.cs#L186) or Session Dispose |
| [`Database`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Database.cs#L45) (Sync) | `_alpm_db_t*` | Borrowed Child | Explicit [`Unregister()`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Database.cs#L495) | Database Unregister or Session Dispose |
| [`Transaction`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Transaction.cs#L82) | Native transaction state | Owning Child | [`IDisposable`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Transaction.cs#L326) | Transaction Dispose or Session Dispose |
| [`PackageView`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Package/PackageView.cs#L11) | `_alpm_pkg_t*` | Borrowed View | Non-disposable | Parent Database, Transaction, or Session release |
| [`LoadedPackage`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Package/LoadedPackage.cs#L20) | `_alpm_pkg_t*` | Owning Child | [`IDisposable`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Package/LoadedPackage.cs#L73) or Hand-over | Explicit Dispose, Hand-over, or Session Dispose |
| [`PackageSnapshot`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Package/PackageSnapshot.cs#L28) | None (Managed fields) | Detached Snapshot | Standard GC object | Never invalidated |
| [`FileList`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Package/FileList.cs#L17) | `_alpm_filelist_t*` | Borrowed View | Non-disposable | Owning Package release |
| [`PackageFile`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Package/PackageFile.cs#L10) | None (Struct copy) | Detached Snapshot | Value type (`readonly struct`) | Never invalidated |
| [`PackageChangelogStream`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Package/PackageChangelogStream.cs#L13) | `void*` (archive handle) | Owning Stream | [`IDisposable`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Package/PackageChangelogStream.cs#L75) | Explicit stream Dispose |
| [`PackageMtreeReader`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Package/PackageMtreeReader.cs#L20) | `archive*` | Owning Reader | [`IDisposable`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Package/PackageMtreeReader.cs#L88) | Materialization completion |
| [`AlpmBorrowedList<T>`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/List/AlpmList.cs#L194) | `_alpm_list_t*` | Borrowed View | Non-disposable | Owning Domain invalidation |
| [`AlpmOwnedList<T>`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/List/AlpmList.cs#L203) | `_alpm_list_t*` | Caller-Owned List | [`IDisposable`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/List/AlpmList.cs#L235) | Scope exit via [`Take()`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/List/AlpmList.cs#L228) |
| [`AlpmOptionList<T>`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Options/AlpmOptionList.cs#L21) | `_alpm_list_t*` | Borrowed Facade | Non-disposable | Session Dispose |
| `Question Structs` | `_alpm_question_t*` | Stack Borrow | Stack confinement (`ref struct`) | Native callback return |
| [`AlpmEvent`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Events/AlpmEvent.cs#L47) | `_alpm_event_t*` | Frame View / Snapshot | Non-disposable | Native callback frame return |
| [`Dependency`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Dependencies.cs#L42) | `alpm_depend_t*` | Detached Snapshot | Explicit [`FreeNative()`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Dependencies.cs#L112) | Independent managed lifecycle |
| [`BackupEntry`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/BackupEntry.cs#L10) | None (Copied fields) | Detached Snapshot | Standard GC object | Independent managed lifecycle |
| [`Group`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Groups.cs#L13) | `_alpm_group_t*` | Hybrid Snapshot | Non-disposable | Member views die with Database |

---

## 4. Deep Lifetime Dynamics by Subsystem

### 4.1 Session and Handle Teardown Dynamics

[`AlpmSession`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/AlpmSession.cs#L37) coordinates teardown across unmanaged and managed state.
The disposal sequence follows a strict deterministic pipeline:

```
[AlpmSession.Dispose()]
         │
         ├─ 1. Dispose active Transaction (releases db.lck before alpm_release)
         │
         ├─ 2. Invalidate RootLifetime ("AlpmSession.Dispose()")
         │     └─ Monotonically increments RootLifetime.Generation
         │     └─ Instantly invalidates all databases, transactions, and views
         │
         ├─ 3. Dispose LoadedPackageRegistry
         │     └─ Invokes alpm_pkg_free() on all undisposed file-loaded packages
         │
         ├─ 4. Dispose SafeAlpmHandle
         │     └─ SafeAlpmHandle.ReleaseHandle() executes
         │     └─ Invokes NativeMethods.alpm_release(handle)
         │
         └─ 5. Dispose Callbacks (WeakGCHandle)
               └─ Only after alpm_release succeeds, ensuring no in-flight callbacks
```

#### SafeHandle GC Safety and Callback Context
* [`SafeAlpmHandle`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Interop/SafeHandles.cs#L10) holds a `WeakReference<RootLifetime>` instead of a strong reference.
* A strong reference would root the [`AlpmSession`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/AlpmSession.cs#L37) through the lifetime domain's `_owner`.
* This would keep the callback context alive and prevent normal GC collection of abandoned sessions.
* The weak reference allows collection while enabling `ReleaseHandle()` to invalidate all stamps before calling `alpm_release`.

### 4.2 Database Lifecycles and Mutation Invalidation

`libalpm` distinguishes between the local installation database and remote sync databases:

#### Local Database
* Accessed via [`AlpmSession.GetLocalDatabase()`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/AlpmSession.cs#L318).
* Tied directly to `AlpmSession._localDatabaseLifetime`.
* It is never placed in the `RootLifetime._handles` registry.
* It cannot be unregistered; calling [`Unregister()`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Database.cs#L487) throws `InvalidOperationException`.
* When a transaction commits, `libalpm` rewrites the local database and frees internal package caches.
* [`AlpmSession.InvalidateLocalDatabase()`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/AlpmSession.cs#L560) retires the domain and drops the reference.
* The next call to `GetLocalDatabase()` creates a fresh [`ChildLifetime`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Interop/Lifetime.cs#L215).

#### Sync Databases
* Registered via [`AlpmSession.RegisterSyncDatabase()`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/AlpmSession.cs#L338).
* Deduplicated via `RootLifetime.GetLifetimeTokenForHandle(ptr, target)`.
* Two wrappers for the same `_alpm_db_t*` share one domain.
* Calling [`Unregister()`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Database.cs#L487) calls `alpm_db_unregister`, invalidates the domain, and forgets the handle from the registry.
* Calling [`AlpmSession.UnregisterAllSyncDatabases()`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/AlpmSession.cs#L349) calls `alpm_unregister_all_syncdbs`, invalidates all sync domains, and clears the registry.

#### Partial Invalidation via InvalidateViews
When a sync database updates via [`Database.Update()`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Database.cs#L474):
1. [`InvalidateViews()`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Database.cs#L457) increments `Lifetime.Generation`.
2. Existing package views and cached lists immediately become stale.
3. The database wrapper recaptures its own stamp (`_stamp = Lifetime.Capture()`).
4. The [`Database`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Database.cs#L45) instance remains valid while all its prior views throw.

### 4.3 Transaction Ownership and Hand-Over Dynamics

`libalpm` permits at most one transaction per session at any time.

#### Transaction Join Contract
* Calling [`AlpmSession.BeginTransaction()`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/AlpmSession.cs#L297) joins the existing transaction if flags match.
* Requesting different flags throws `InvalidOperationException` to prevent accidental flag corruption.

#### Dual-Ownership Package Hand-Over
Package addition presents a critical ownership boundary in `libalpm`:

```csharp
// Overload 1: Database package (borrowed)
public void AddPackage(PackageView pkg);

// Overload 2: File-loaded package (ownership hand-over)
public PackageView AddPackage(LoadedPackage pkg);
```

1. **Database Packages:** The transaction borrows the package. Releasing the transaction does not free it.
2. **File-Loaded Packages:** `libalpm` takes ownership and automatically frees the package during `alpm_trans_release`.
3. **The Hand-over Handshake:**
   * [`LoadedPackage.Disown()`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Package/LoadedPackage.cs#L55) exchanges `_rawPackage` to `0` and sets `Disposed = true`.
   * Unregisters the pointer from [`LoadedPackageRegistry`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Package/LoadedPackageRegistry.cs) with `freeNative: false`.
   * Invalidates the [`LoadedPackage`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Package/LoadedPackage.cs#L20) lifetime domain.
   * Returns a new [`PackageView`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Package/PackageView.cs#L11) anchored to `Transaction.Lifetime`.
   * When `Transaction.Dispose()` runs, `alpm_trans_release` frees the native package memory.
   * Subsequent disposal of `LoadedPackage` is a safe no-op. Double-free is impossible.

#### Commit Invalidation Dynamics
In [`Transaction.TryCommit()`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Transaction.Seams.cs#L143):
1. **Pre-commit invalidation:** Calls `_session.RootLifetime.Invalidate("Transaction.Commit()")`. This invalidates every existing stamp across the session before native execution. No view can observe partially torn cache states.
2. **Native commit:** Calls `alpm_trans_commit`.
3. **Post-commit invalidation:** Calls `_session.InvalidateLocalDatabase("Transaction.Commit()")`. Replaces the local database token for newly populated caches.

### 4.4 List Binding Dynamics

`libalpm` uses doubly linked lists (`alpm_list_t*`) extensively with divergent ownership semantics:

#### Borrowed Lists ([`AlpmBorrowedList<T>`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/List/AlpmList.cs#L194))
* Represents lists owned internally by `libalpm` (`alpm_get_syncdbs`, `alpm_db_get_pkgcache`, `alpm_pkg_get_depends`).
* Does not implement `IDisposable`. Never calls `alpm_list_free`.
* Carries `LifetimeStamp?`.
* [`Enumerator.MoveNext()`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/List/AlpmList.cs#L98) checks `ThrowIfStale()` on each iteration step. Advancing through freed nodes throws immediately.

#### Owned Lists ([`AlpmOwnedList<T>`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/List/AlpmList.cs#L203))
* Represents lists allocated by `libalpm` for the caller (`alpm_checkdeps`, `alpm_checkconflicts`, `alpm_trans_prepare`).
* Implements `IDisposable`.
* Requires a function pointer destructor (`_innerFree`) for element payloads (e.g. `depmissing_free`, `conflict_free`).
* [`AlpmOwnedList<T>.Take()`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/List/AlpmList.cs#L228) materializes items into a managed array and deterministically frees native nodes and payloads in `Dispose()`.

### 4.5 Callback Frames and Question Confinement

#### Synchronous Callback Lifecycles
* Native callbacks registered with `libalpm` pass a `void* ctx` pointing to a `WeakGCHandle<AlpmCallbacks>`.
* In [`EventAgent`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Events/AlpmCallbacks.cs#L188), each invocation creates a scoped child domain:
  ```csharp
  var callbackLifetime = callback._rootLifetime.CreateChild("AlpmCallbacks.EventAgent");
  try {
    callback.EventHandler?.Invoke(AlpmEvent.FromUnion(eventT, callbackLifetime));
  } finally {
    callbackLifetime.Invalidate("callback frame ended");
  }
  ```
* Views borrowed inside the callback frame become invalid immediately upon callback return.

#### Stack-Bound Questions
Questions require synchronous answers before the native callback returns:
* All question types are declared as `public unsafe ref struct` ([`InstallIgnorePackageQuestion`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Events/AlpmQuestion.cs#L11), etc.).
* C# enforces at compile time that `ref struct` instances cannot:
  * Escape to heap fields or collections.
  * Be captured in lambdas or async methods.
  * Cross thread boundaries.
* Setting properties directly mutates fields in `_alpm_question_t*`.

---

## 5. Invalidation Cascade Matrix

The following matrix documents every invalidating operation and its systemic cascade:

| Operation | Triggering Method | Invalidated Domains | Retired Views | Re-stamped Wrappers | Native Freeing Agent |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **Session Disposal** | [`AlpmSession.Dispose()`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/AlpmSession.cs#L578) | `RootLifetime` and all child domains | All databases, transactions, packages, and lists | None | `alpm_release` + `alpm_pkg_free` |
| **Local DB Commit** | [`Transaction.Commit()`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Transaction.cs#L186) | `RootLifetime` (pre) + `LocalDatabaseLifetime` (post) | All prior local database package views and caches | `AlpmSession` (survives) | `libalpm` internal cache realloc |
| **Sync DB Update** | [`Database.Update()`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Database.cs#L474) | Database `ChildLifetime` | All package views and lists from this database | Calling [`Database`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Database.cs#L45) instance | `libalpm` internal cache drop |
| **Sync DB Unregister** | [`Database.Unregister()`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Database.cs#L487) | Database `ChildLifetime` | Database wrapper and all views from it | None | `alpm_db_unregister` |
| **Unregister All Sync DBs** | [`AlpmSession.UnregisterAllSyncDatabases()`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/AlpmSession.cs#L349) | All registered Sync DB `ChildLifetime`s | All sync databases and their child views | None | `alpm_unregister_all_syncdbs` |
| **Server List Mutation** | [`Database.AddServer()`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Database.cs#L321) | `LifetimeFacet.Servers` | Prior [`Servers`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Database.cs#L210) list views | Database and Package views survive | `libalpm` re-links server list |
| **Cache Server Mutation** | [`Database.AddCacheServer()`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Database.cs#L377) | `LifetimeFacet.CacheServers` | Prior [`CacheServers`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Database.cs#L224) list views | Database and Package views survive | `libalpm` re-links cache servers |
| **Transaction Release** | [`Transaction.Dispose()`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Transaction.cs#L326) | Transaction `ChildLifetime` | Transaction added/removed views and hand-over views | None | `alpm_trans_release` |
| **Package Hand-Over** | [`Transaction.AddPackage(LoadedPackage)`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Transaction.cs#L143) | `LoadedPackage` child domain | Original [`LoadedPackage`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Package/LoadedPackage.cs#L20) wrapper | Issues new [`PackageView`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Package/PackageView.cs#L11) on transaction | Ownership transferred to transaction |
| **Loaded Package Dispose**| [`LoadedPackage.Dispose()`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Package/LoadedPackage.cs#L73) | `LoadedPackage` child domain | Package wrapper and child file/dep views | None | `alpm_pkg_free` |
| **Callback Frame Return** | End of `EventAgent` | Transient callback `ChildLifetime` | Views created inside that callback invocation | None | Native stack frame pop |

---

## 6. Safety Analysis & Architectural Verification

### 6.1 GC Early Collection Hazards
In .NET, aggressive JIT dead-code elimination can collect an object while its native FFI method executes.
Orson defends against this across three layers:
1. **SafeHandle Marshalling:** Native methods accepting [`SafeAlpmHandle`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Interop/SafeHandles.cs#L10) increment handle reference counts across the P/Invoke stub.
2. **One-Hop Domain Anchoring:** Every [`LifetimeStamp`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Interop/Lifetime.cs#L290) references its [`Lifetime`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Interop/Lifetime.cs#L49) domain, which strongly references `_owner`. A live view anchors the session.
3. **Automated GC Anchor Audit:** Tests in [`GcAnchorAuditTests.cs`](file:///home/shanoaice/Projects/github/Orson/tests/Orson.Alpm.Tests/Unit/GcAnchor/GcAnchorAuditTests.cs) inspect IL control-flow graphs across hundreds of FFI call sites to ensure `GC.KeepAlive(this)` or `SafeHandle` bracketing protects every unmanaged read.

### 6.2 Process-Wide Native Heap Accounting
Tests in [`ListOwnershipTests.cs`](file:///home/shanoaice/Projects/github/Orson/tests/Orson.Alpm.Tests/Unit/Ownership/ListOwnershipTests.cs) verify process-wide native heap usage during repeated list operations.
Borrowed list views never allocate or free native nodes.
Owned list operations release all native memory without heap leaks.

---

## 7. Conclusion

Orson achieves complete memory safety over `libalpm` by combining:
1. Two-level generational domain stamping for $O(1)$ batch invalidation.
2. Distinguishing non-owning borrowed views ([`PackageView`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Package/PackageView.cs#L11)) from owning handles ([`LoadedPackage`](file:///home/shanoaice/Projects/github/Orson/src/Orson.Alpm/Package/LoadedPackage.cs#L20)).
3. Atomic ownership handover during transaction inclusion.
4. Compile-time stack confinement (`ref struct`) for synchronous question callbacks.
5. Critical finalizer protection on root handles with automated GC anchor audits.

This design delivers near-zero-overhead unmanaged reads while maintaining strict managed lifetime invariants.
