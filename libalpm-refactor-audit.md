# Orson Libalpm Architecture & Unified Refactoring Specification

**Document Status:** Final Consolidated Architecture Specification  
**Consolidation Sources:** `reports/libalpm-refactor-audit-r1.md`, `reports/libalpm-refactor-audit-r2.md`, `reports/scoped-arena-lifetime-evaluation.md`  
**Target Codebase:** `src/Orson.Alpm`, `src/Orson.Alpm.FSharp`, `tests/Orson.Alpm.Tests`, `src/Orson.Benchmarks`  
**Target Framework:** .NET 10.0 (`net10.0`), C# 14  

---

## 1. Executive Summary

This document specifies the unified architecture and refactoring plan for `Orson.Alpm`. It eliminates Use-After-Free (UAF) hazards, cascading lifetime invalidation bugs, and GC allocations on high-frequency paths while preserving idiomatic C# and F# ergonomics.

### Key Architectural Pillars

1. **Process-Wide Concurrency & Single Active Session Lease:**
   - Enforce an Exclusive Process Lease (`AlpmSession.Acquire`): at most one active `AlpmSession` per OS process at any time.
   - Synchronize all P/Invoke boundaries and finalizer execution through an internal recursive re-entrancy lock (`NativeGate`).
   - Align test fixtures via xUnit test collection serialization so parallel test runners execute ALPM fixtures sequentially without native state pollution.
2. **Surgical Lifetime Bug Fixes:**
   - **`Database.Unregister()`:** Remove `root.Invalidate("Database.Unregister()")`. Invalidate only the target database's child lifetime domain.
   - **`Transaction.TryCommit()`:** Remove root session invalidation. Invalidate only `_localDatabaseLifetime` and the transaction domain so sync databases and views remain valid.
   - **`RootLifetime._owner` Decoupling:** Anchor `RootLifetime._owner` to `SafeAlpmHandle` rather than `AlpmSession` to eliminate managed reference cycles that block session finalization.
   - **Strictly Non-Nullable Lifetime & Strict Context:** Enforce strictly non-nullable `Lifetime` across all views (`PackageView`, `AlpmList<T>`, `FileList`). Decouple caller-owned transient list snapshots (`Conflict`, `MissingDependency`, `string`) out of `AlpmList<T>` into static `NativeListMarshal.MaterializeAndFree<T>()`, eliminating `AlpmOwnedList<T>`. Retain direct, separate 4-line traversal loops in `AlpmList.ToArray()` and `NativeListMarshal.MaterializeAndFree()`. Apply strict context to empty lists (no immortal sentinels); remove obsolete unanchored `new AlpmStringList()` constructor. Eliminate unnecessary runtime null-lifetime defensive checks across all element factories.
3. **Zero-Allocation Package Architecture:**
   - Convert `PackageView` into a 40-byte `readonly unsafe struct` (`_alpm_pkg_t*` 8B + `LifetimeStamp` 32B).
   - Eliminate lazy string caching fields (`field ??=`) from live package views.
   - Refactor `LoadedPackage` to compose `PackageView View { get; }` with `implicit operator PackageView(LoadedPackage)`.
   - Unify `PackageView`, `LoadedPackage`, and `PackageSnapshot` under the `IPackageMetadata` interface.
   - Gate `BackupFiles` alongside `Files` in `PackageSnapshot` behind `includeFiles` (aligned with upstream libalpm `INFRQ_FILES` layout).
4. **Database Hierarchy Split:**
   - Split `Database` into `abstract class Database`, `sealed class LocalDatabase : Database`, and `sealed class SyncDatabase : Database`.
   - Move `Update()`, `Unregister()`, `Servers`, and `CacheServers` to `SyncDatabase` to eliminate `IsLocal` branching and runtime capability checks.
5. **Callback & Event System Refinement (Refined Option 4C):**
   - Keep callback properties as delegates while allowing the pre-1.0 log and download payload shapes to change.
   - Cache the 23 stateless unit events as `static readonly` singletons to eliminate their event heap allocations.
   - Maintain a reusable `ChildLifetime _eventLifetime` on `AlpmCallbacks` to eliminate per-event `ChildLifetime` heap allocations.
   - Overhaul high-frequency callbacks:
     - `LogAgent`: add a `LogLevel EnabledLogLevels` filter; pass callback-scoped UTF-8 spans and replace libc `vasprintf` with `vsnprintf` into `stackalloc byte[512]`.
     - `ProgressAgent`: add 1-slot package name string cache (`_lastPkgBytes` / `_lastPkgString`), cutting 101 allocations per package down to 1.
     - `DownloadAgent`: convert `AlpmDownloadEvent` to a readonly value type and cache the active filename string.
   - Defer the 500-package transaction validation until an isolated integration harness is available; track it in `reports/callback-integration-validation-todo.md`.
6. **Scoped Arena Scratch Memory for FFI Marshalling:**
   - Introduce an unmanaged watermark bump scratch arena in `NativeListMarshal` for transient `_alpm_list_t*` chains.
   - Eliminate repetitive per-node native `malloc`/`free` calls during dependency, conflict, and satisfier queries.
   - Retain the ADR 0010 same-thread ~8.5 ns validation check on `AlpmList<T>.Enumerator.MoveNext` to prevent Use-After-Free during loop execution.

---

## 2. Process-Global State & Concurrency Architecture

### 2.1 The Nature of `libalpm` Process Pollution

`libalpm` contains global, unsynchronized C state that cannot be safely virtualized across multiple simultaneous sessions within a single OS process:

1. **GPGME Global Keyring Anchoring:**
   `libalpm` configures PGP verification via `signing.c:144`. GPGME initializes an un-synchronized static flag on first use, permanently anchoring to that handle's `gpgdir`. Subsequent sessions with custom keyrings (such as test sandboxes or chroots) check signatures against the first session's keyring.
2. **Process Working Directory & Umask Mutation:**
   Package extraction calls `chdir(handle->root)` (`add.c:537`) and mutates the process `umask`. Multiple concurrent sessions corrupt each other's filesystem operations and working paths.
3. **Process-Wide Signal Handlers & Syslog:**
   Transaction execution sets global `sigaction()` handlers for `SIGINT` and `SIGQUIT`. Additionally, `alpm_release` closes the process syslog socket via `closelog()`.
4. **`curl_global_init()` / `curl_global_cleanup()`:**
   Shared libcurl global state destabilizes if multiple sessions initialize or teardown concurrently.

### 2.2 Calling Thread Concurrency Model

Inspection and disassembly of `/usr/lib/libalpm.so` (upstream `pacman 7.1.0.r9.g54d9411-6`, `dload.c`) confirms that:
- `ParallelDownloads` executes `curl_multi_init`, `curl_multi_perform`, `curl_multi_wait`, and `curl_multi_info_read` synchronously on the thread that initiated the ALPM operation.
- No background worker PThreads invoke progress, download, or log callbacks into managed code.
- All callback invocations occur sequentially on the calling thread.

### 2.3 Exclusive Process Lease Pattern

Orson enforces at most one active `AlpmSession` per OS process at any point in time. This is implemented via an Exclusive Process Lease (`AlpmSession.Acquire`):

```csharp
namespace Orson.Alpm;

public sealed partial class AlpmSession : IDisposable
{
    private static int s_activeLease;
    private static uint s_sessionEpoch;
    private static AlpmSession? s_current;

    public static AlpmSession Acquire(string root, string dbpath)
    {
        if (Interlocked.CompareExchange(ref s_activeLease, 1, 0) != 0)
        {
            throw new InvalidOperationException(
                "An AlpmSession is already active in this process. Dispose the active session before opening another.");
        }

        try
        {
            var session = new AlpmSession(root, dbpath);
            s_current = session;
            Interlocked.Increment(ref s_sessionEpoch);
            return session;
        }
        catch
        {
            Interlocked.Exchange(ref s_activeLease, 0);
            throw;
        }
    }

    private void ReleaseProcessLease()
    {
        if (Interlocked.Exchange(ref s_activeLease, 0) == 1)
        {
            s_current = null;
            Interlocked.Increment(ref s_sessionEpoch);
        }
    }
}
```

### 2.4 Internal NativeGate Synchronization

To prevent race conditions between active worker threads and the GC finalizer thread running `SafeAlpmHandle.ReleaseHandle()`, all P/Invoke operations and handle teardown pass through an internal recursive synchronization primitive:

```csharp
namespace Orson.Alpm.Interop;

internal static class NativeGate
{
    private static readonly Lock s_lock = new();

    public static void Enter() => s_lock.Enter();
    public static void Exit() => s_lock.Exit();

    public static Scope Hold()
    {
        s_lock.Enter();
        return new Scope();
    }

    public readonly ref struct Scope
    {
        public void Dispose() => s_lock.Exit();
    }
}
```

### 2.5 Test Harness Serialization & Alignment

The xUnit test harness (`tests/Orson.Alpm.Tests`) executes test classes concurrently by default. To align with the Exclusive Process Lease without disabling all parallelism:

1. Apply an xUnit test collection definition across all fixture-backed tests:
   ```csharp
   [CollectionDefinition("AlpmSessionSerial", DisableParallelization = true)]
   public class AlpmSessionSerialCollection : ICollectionFixture<IsolatedAlpmEnvironmentFixture> { }
   ```
2. Annotate all test classes that instantiate `IsolatedAlpmEnvironment` or `AlpmSession` with `[Collection("AlpmSessionSerial")]`. Tests that verify pure managed algorithms (IL decoders, parsers, UTF-8 formatting) remain fully parallelized.
3. Relax or retire overly defensive intentional handle-leak tests (`CallbackContextLifetimeTests.cs:150-194`) that previously bypassed `alpm_trans_release` to deliberately leak `db.lck`.

---

## 3. Lifetime Domains & Memory Safety Bug Fixes

### 3.1 Fix Cascading Invalidation in `Database.Unregister()`

#### The Defect
In `src/Orson.Alpm/Database.cs:112` and `src/Orson.Alpm/AlpmSession.Seams.cs:121`, the unregister completion callback executes:
```csharp
root.ForgetHandle(ptr);
root.Invalidate("Database.Unregister()"); // DEFECT: invalidates the entire session!
```
Calling `root.Invalidate()` increments `RootLifetime.Generation`. This immediately invalidates `AlpmSession`, `LocalDatabase`, and all sibling sync databases, causing subsequent operations to throw `AlpmLifetimeException`.

#### The Fix
1. Remove `root.Invalidate("Database.Unregister()")` from `Database.cs` and `AlpmSession.Seams.cs`.
2. Invalidate only the child database lifetime domain:
   ```csharp
   databaseLifetime.Invalidate("Database.Unregister()");
   root.ForgetHandle(ptr);
   ```

#### Dependencies & Prerequisites
- Coordinate with Phase 3 (Database Hierarchy Split). `Unregister()` moves to `SyncDatabase`.
- Introduce a child lifetime domain for the sync database list (`_syncDatabasesLifetime`) on `AlpmSession`. Invalidating this domain safely retires retained `AlpmList<Database>` instances on unregister without bumping `RootLifetime`.
- Update `LifetimeInvalidationTests.SyncDatabaseUnregister_Succeeds_AndRetiresTheRetainedSyncDatabaseList` to expect the sync list domain target instead of `"the ALPM handle"`.

### 3.2 Fix Over-Invalidation in `Transaction.TryCommit()`

#### The Defect
In `src/Orson.Alpm/Transaction.Seams.cs:135`:
```csharp
_session.RootLifetime.Invalidate("Transaction.Commit()"); // DEFECT: invalidates all sync DBs and views
```
A transaction commit mutates only local filesystem packages and the local database (`/var/lib/pacman/local`). Invalidating `RootLifetime` unnecessarily invalidates remote sync databases and remote package views.

#### The Fix
1. Remove `_session.RootLifetime.Invalidate(...)` from line 135.
2. Retain `_session.InvalidateLocalDatabase("Transaction.Commit()")` on line 153 to invalidate only the local database cache and local views.
3. Invalidate the transaction's own domain (`Lifetime.Invalidate("Transaction.Commit()")`).

#### Dependencies & Prerequisites
- Standalone. Execute in Phase 0 without waiting for other phases.
- Unblocks concurrent read-only queries during transactions in `Orson.Alpm.FSharp`.

### 3.3 Decouple `RootLifetime._owner` from `AlpmSession`

#### The Defect
`RootLifetime.Create(this, "the ALPM handle")` anchors `_owner` to `AlpmSession`. Every `LifetimeStamp` holds a reference to `RootLifetime`. This forms a reference cycle:
`PackageView` $\rightarrow$ `LifetimeStamp` $\rightarrow$ `RootLifetime` $\rightarrow$ `AlpmSession`.
If a consumer holds or caches a `PackageView`, the entire `AlpmSession` is prevented from being garbage collected.

#### The Fix
1. Pass `SafeAlpmHandle` as the owner:
   ```csharp
   _lifetime = RootLifetime.Create(_handle, "the ALPM handle");
   ```
2. Views keep `SafeAlpmHandle` alive without rooting `AlpmSession`.
3. In `tests/Orson.Alpm.Tests/Unit/ObjectLifetime/LifetimeGcPathTests.cs`, update test assertions to check that the handle remains valid and view operations succeed, rather than asserting weak-reference liveness of the outer facade class.

#### Dependencies & Prerequisites
- Coordinate with Phase 1 (Process Concurrency Guard & Lease Management).
- Update `LifetimeGcPathTests.ALiveView_KeepsTheSessionAlive_SoTheGcPathCannotFreeUnderIt` to assert `_handle.IsClosed == false` and verify view read access rather than checking outer facade liveness.

### 3.4 Enforce Strictly Non-Nullable Lifetime & Apply Strict Context

#### The Defect
1. `AlpmList<T>` permitted `Lifetime?` to be nullable because internal `AlpmOwnedList<T>` subclassed it to reuse `ToArray()` for eager snapshots (`string`, `Conflict`, `MissingDependency`).
2. This forced `AlpmList<T>.Factory` to accept `delegate*<void*, Lifetime?, T>`, compelling all element factories across the codebase to accept `Lifetime?`.
3. Consequently, `GroupPackageFactory` in `src/Orson.Alpm/Groups.cs` returned unanchored `new PackageView((_alpm_pkg_t*)ptr, null)` when `sessionDomain is null` or `db == null`. Passing a null lifetime causes `ThrowIfStale()` to be a silent no-op, allowing views to access freed unmanaged memory without detection.
4. Element factories had to introduce redundant runtime defensive guards (`if (lifetime is null) throw ...`).

#### The Fix
1. **Decouple Eager Snapshots & Remove `AlpmOwnedList<T>`:**
   - Add static `NativeListMarshal.MaterializeAndFree<T>()` overloads to marshal caller-owned native lists and execute deallocation inside `try ... finally`.
   - Remove `AlpmOwnedList<T>` entirely.
   - Retain direct, separate 4-line C pointer traversal loops in `AlpmList<T>.ToArray()` and `NativeListMarshal.MaterializeAndFree<T>()` for clean inlining without indirection debt.
2. **Make `AlpmList<T>` and `FileList` Strictly Non-Nullable:**
   - Make `AlpmList<T>.Lifetime` and `FileList._lifetime` strictly non-nullable `LifetimeStamp`.
   - Make `AlpmList<T>.Factory` strictly non-nullable `delegate*<void*, Lifetime, T>`.
   - Add `ArgumentNullException.ThrowIfNull(lifetime)` to constructors; drop all `Lifetime?.` null-conditional access in traversal and validation methods.
3. **Strip Unnecessary Defensive Null Checks from Element Factories:**
   - Update `PackageView.Factory`, `SyncDatabase.Factory`, `Group.Factory`, `Dependencies.Factory`, `BackupEntry.Factory` to accept non-nullable `Lifetime`.
   - Remove all `if (lifetime is null) throw ...` guards.
   - For self-contained snapshot records (`Conflict`, `MissingDependency`, `PackageFile`), drop the unused `Lifetime` parameter completely (`delegate*<void*, T>`).
4. **Apply Strict Context to Empty Lists (No Immortal Sentinels):**
   - When libalpm returns an empty list (`_alpm_list_t* == null`), the view still captures the parent's non-nullable `Lifetime`. Iteration immediately observes `_native == null` and yields 0 elements while alive, but throws `AlpmLifetimeException` if the owner was invalidated (preserving ADR 0010 without immortal sentinels).
   - Remove the obsolete unanchored `public AlpmStringList()` parameterless constructor.

#### Dependencies & Prerequisites
- Landed as Phase 3.5 (Immediate Refactoring) following Database Hierarchy Split.

### 3.5 Stream & Archive Reader Resource Safety

#### Architectural Contract
`PackageChangelogStream` (`PackageChangelogStream.cs:12-81`) and `PackageMtreeReader` (`PackageMtreeReader.cs:19-86`) are internal, single-owner unmanaged resource wrappers.
- They must **not** declare GC finalizers (`~PackageChangelogStream()`, `~PackageMtreeReader()`).
- The native close functions (`alpm_pkg_changelog_close`, `alpm_pkg_mtree_close`) dereference `_package.BackingStruct` and write to `pkg->handle->pm_errno`. If a finalizer runs after `SafeAlpmHandle` has already been finalized and released, closing the stream results in an unmapped-memory crash or heap corruption.
- Leaking an unmanaged handle upon an abandoned, un-disposed stream is strictly preferable to crashing the runtime with Use-After-Free during out-of-order finalization.
- Deterministic disposal via `using` blocks is required. `SafeAlpmHandle` remains the sole finalizable boundary in the assembly.

---

## 4. Zero-Allocation Package Architecture

### 4.1 Topology and Struct Layout

`PackageView` is converted from a managed class into a 40-byte `readonly unsafe struct`.

```
┌────────────────────────────────────────────────────────┐
│ PackageView (readonly unsafe struct, exactly 40 bytes)  │
├────────────────────────────┬───────────────────────────┤
│ _alpm_pkg_t* _rawPackage   │ 8 bytes unmanaged pointer │
│ LifetimeStamp _stamp       │ 32 bytes lifetime token   │
│   ├── Lifetime _resource   │   ├── 8 bytes reference   │
│   ├── long _resourceGen    │   ├── 8 bytes int64       │
│   ├── RootLifetime _root   │   ├── 8 bytes reference   │
│   └── long _rootGen        │   └── 8 bytes int64       │
└────────────────────────────┴───────────────────────────┘
```

- **Allocation Elimination:** Iterating over 2,500 packages in `db.LoadPackageCache()` allocates **0 bytes** on the managed heap.
- **Cache Optimization:** Eliminates lazy string caching fields (`field ??=`), removing 80+ bytes of object header and reference overhead per view.
- **Lifetime Safety:** Every property read continues to invoke `_stamp.ThrowIfStale()`.

### 4.2 Shared Metadata Interface (`IPackageMetadata`)

To allow consumers to write polymorphic code across live views, owned packages, and disconnected snapshots, all three package types implement `IPackageMetadata`:

```csharp
namespace Orson.Alpm;

public interface IPackageMetadata
{
    string Name { get; }
    string Version { get; }
    string? Description { get; }
    string? Architecture { get; }
    string? Url { get; }
    long Size { get; }
    long InstalledSize { get; }
    PackageFrom Origin { get; }
    PackageReason Reason { get; }
    IReadOnlyList<PackageFile>? Files { get; }
    IReadOnlyList<BackupEntry>? BackupFiles { get; }
    IReadOnlyList<Dependency> Depends { get; }
    IReadOnlyList<Dependency> OptionalDepends { get; }
    IReadOnlyList<Dependency> Provides { get; }
    IReadOnlyList<Dependency> Conflicts { get; }
    IReadOnlyList<Dependency> Replaces { get; }
}
```

### 4.3 Composition Model for `LoadedPackage`

`LoadedPackage` represents an unmanaged package archive loaded from disk via `alpm_pkg_load`. It remains a managed class because it owns unmanaged memory and implements `IDisposable`:

```csharp
namespace Orson.Alpm;

public sealed unsafe class LoadedPackage : IDisposable, IPackageMetadata
{
    private _alpm_pkg_t* _rawPackage;
    private readonly ChildLifetime _lifetime;
    private readonly LoadedPackageRegistry _registry;

    internal LoadedPackage(LoadedPackageRegistry registry, _alpm_pkg_t* raw, ChildLifetime lifetime)
    {
        _registry = registry;
        _rawPackage = raw;
        _lifetime = lifetime;
        View = new PackageView(raw, lifetime);
    }

    public PackageView View { get; }

    public static implicit operator PackageView(LoadedPackage pkg) => pkg.View;

    // Delegate all IPackageMetadata properties to View
    public string Name => View.Name;
    public string Version => View.Version;
    // ...

    public void Dispose()
    {
        var ptr = (_alpm_pkg_t*)Interlocked.Exchange(ref *(nint*)&_rawPackage, 0);
        if (ptr == null) return;

        _lifetime.Invalidate("LoadedPackage.Dispose()");
        _registry.Unregister(ptr, freeNative: true);
    }

    internal void Disown()
    {
        var ptr = (_alpm_pkg_t*)Interlocked.Exchange(ref *(nint*)&_rawPackage, 0);
        if (ptr == null) return;

        _registry.Unregister(ptr, freeNative: false);
        _lifetime.Invalidate("Handed over to Transaction");
    }
}
```

### 4.4 `PackageSnapshot` Gating & Upstream Symbol Verification

#### Upstream libalpm Verification
- **Symbol Anchor:** `libalpm/be_local.c:_alpm_local_db_read`
- **Flag Definition:** `INFRQ_FILES = 0x02`, `INFRQ_DESC = 0x01`
- **Verified Behavior:** In `be_local.c`, reading package information from `/var/lib/pacman/local/<pkg>/` splits parsing across two primary files:
  1. The `desc` file contains `%NAME%`, `%VERSION%`, `%DESC%`, `%URL%`, `%ARCH%`, `%BUILDDATE%`, `%INSTALLDATE%`, `%PACKAGER%`, `%SIZE%`, `%REASON%`, `%LICENSE%`, `%VALIDATION%`.
  2. The `files` file contains `%FILES%` and `%BACKUP%`.
  Verification across the filesystem confirms that `%BACKUP%` entries reside strictly inside `files` files, parsed only under `INFRQ_FILES`.

#### Snapshot Gating Contract
When materializing a `PackageSnapshot`, reading `pkg.Files` or `pkg.BackupFiles` triggers `libalpm`'s disk parser for the `files` file. Therefore, `PackageSnapshot` must gate both collections behind the `includeFiles` parameter:

```csharp
public sealed class PackageSnapshot : IPackageMetadata
{
    public PackageSnapshot(PackageView pkg, bool includeFiles = false)
    {
        Name = pkg.Name;
        Version = pkg.Version;
        Description = pkg.Description;
        Architecture = pkg.Architecture;
        Url = pkg.Url;
        Size = pkg.Size;
        InstalledSize = pkg.InstalledSize;
        Origin = pkg.Origin;
        Reason = pkg.Reason;

        Depends = pkg.Depends.ToSnapshot();
        OptionalDepends = pkg.OptionalDepends.ToSnapshot();
        Provides = pkg.Provides.ToSnapshot();
        Conflicts = pkg.Conflicts.ToSnapshot();
        Replaces = pkg.Replaces.ToSnapshot();

        if (includeFiles)
        {
            Files = pkg.Files.ToSnapshot();
            BackupFiles = pkg.BackupFiles.ToSnapshot();
        }
        else
        {
            Files = null;
            BackupFiles = null;
        }
    }

    public IReadOnlyList<PackageFile>? Files { get; }
    public IReadOnlyList<BackupEntry>? BackupFiles { get; }
    // ...
}
```

### 4.5 Zero-Copy UTF-8 Fast Paths

Following `reports/utf_8_native_interop_wrapper_design_guide.md`, `PackageView` exposes direct UTF-8 spans to eliminate string transcoding when feeding high-throughput CLI output or hashing routines:

```csharp
public readonly unsafe struct PackageView : IPackageMetadata
{
    public ReadOnlySpan<byte> NameUtf8
    {
        get
        {
            _stamp.ThrowIfStale();
            byte* ptr = NativeMethods.alpm_pkg_get_name(_rawPackage);
            var span = ptr == null ? default : MemoryMarshal.CreateReadOnlySpanFromNullTerminated(ptr);
            GC.KeepAlive(_stamp.Domain);
            return span;
        }
    }

    public ReadOnlySpan<byte> VersionUtf8
    {
        get
        {
            _stamp.ThrowIfStale();
            byte* ptr = NativeMethods.alpm_pkg_get_version(_rawPackage);
            var span = ptr == null ? default : MemoryMarshal.CreateReadOnlySpanFromNullTerminated(ptr);
            GC.KeepAlive(_stamp.Domain);
            return span;
        }
    }

    public ReadOnlySpan<byte> ArchitectureUtf8
    {
        get
        {
            _stamp.ThrowIfStale();
            byte* ptr = NativeMethods.alpm_pkg_get_arch(_rawPackage);
            var span = ptr == null ? default : MemoryMarshal.CreateReadOnlySpanFromNullTerminated(ptr);
            GC.KeepAlive(_stamp.Domain);
            return span;
        }
    }
}
```

Additionally, `PackageVersion` implements `IUtf8SpanFormattable` to format `"pkgname-1.0.0-1"` directly into stack buffers or CLI standard output streams without intermediate `string` allocations.

---

## 5. Database Class Hierarchy Split

### 5.1 Motivation

The existing unified `Database` class branches on an internal `bool _isLocal` flag. Methods like `Update()`, `Unregister()`, and server list properties throw `InvalidOperationException` at runtime when invoked on a local database instance. Splitting the hierarchy makes capabilities explicit in the type system.

### 5.2 Type Hierarchy

```
               ┌───────────────────────────┐
               │     abstract Database     │
               └─────────────┬─────────────┘
                             │
              ┌──────────────┴──────────────┐
              ▼                             ▼
┌───────────────────────────┐ ┌───────────────────────────┐
│       LocalDatabase       │ │       SyncDatabase        │
├───────────────────────────┤ ├───────────────────────────┤
│ Read-only local cache     │ │ Servers, CacheServers     │
│ InvalidateViews()         │ │ Update(), Unregister()    │
│ No network/server members │ │ TreeName, SigLevel        │
└───────────────────────────┘ └───────────────────────────┘
```

1. **`abstract class Database`:**
   Exposes core query capabilities valid for any ALPM database: `Name`, `PkgCache`, `GrpcCache`, `FindPackage()`, `Search()`, and `IsValid`.
2. **`sealed class LocalDatabase : Database`:**
   Obtained via `session.GetLocalDatabase()`. Anchored to `_session.LocalDatabaseLifetime`. Represents `/var/lib/pacman/local`.
3. **`sealed class SyncDatabase : Database`:**
   Obtained via `session.GetSyncDatabases()` or `session.RegisterSyncDatabase()`. Adds remote sync capabilities:
   - `void Update(bool force = false)`
   - `void Unregister()`
   - `AlpmOptionList<string> Servers { get; }`
   - `AlpmOptionList<string> CacheServers { get; }`
   - `SigLevel SigLevel { get; set; }`

---

## 6. Callback & Event System (Refined Option 4C)

### 6.1 Public Callback Shapes

All callback slots remain delegate properties with direct C# lambda assignment. The pre-1.0 API
uses a custom span delegate for logs so accepted messages do not allocate a string:

```csharp
public delegate void LogMessageHandler(LogLevel level, ReadOnlySpan<byte> messageUtf8);

public Action<AlpmEvent>? EventHandler { get; set; }
public Func<string, string, bool, FetchResult>? FetchHandler { get; set; }
public Action<string, AlpmDownloadEvent>? DownloadHandler { get; set; }
public Action<AlpmProgressEvent>? ProgressHandler { get; set; }
public LogMessageHandler? LogHandler { get; set; }
public LogLevel EnabledLogLevels { get; set; }
```

The default `EnabledLogLevels` value is `Error | Warning`. A log span is valid only for the
synchronous handler call. F# synchronous stream consumers can use it directly; F# actor and UI
adapters must copy the bytes before crossing an asynchronous boundary.

### 6.2 Eliminating Event Heap Allocations (Option 4C)

`EventAgent` previously allocated a new `ChildLifetime` instance and a new event object for every callback. The implemented design is:

1. **Static Singleton Caching for Unit Events:**
   23 of the 37 event subclasses carry no data payload (e.g., `CheckDepsStart`, `CheckDepsDone`, `TransactionStart`, `TransactionDone`). They are `static readonly` singletons:
   ```csharp
   public sealed class CheckDepsStart : AlpmEvent
   {
       internal static readonly CheckDepsStart Instance = new();
       private CheckDepsStart() { }
   }
   ```
2. **Reusable Callback Lifetime Scope:**
   `AlpmCallbacks` maintains one reusable `ChildLifetime _eventLifetime`. A normal event captures
   its current generation and invalidates it in `finally`; the next event captures the incremented
   generation. A nested event gets a separate child lifetime so it cannot retire an outer frame:
   ```csharp
   var frame = _invokeDepth == 0
       ? _eventLifetime
       : _rootLifetime.CreateChild("a nested ALPM event callback");
   try
   {
       EventHandler?.Invoke(AlpmEvent.FromUnion(eventT, frame));
   }
   finally
   {
       frame.Invalidate("the callback returned");
   }
   ```
3. **Closure-Free `SafeInvoke`:**
   `SafeInvoke` takes a generic state parameter and an invocation method. Call sites use static
   uncaptured lambdas, so no managed closure is created for each dispatch.

### 6.3 Overhaul of High-Frequency Hot Callbacks

`LogAgent`, `DownloadAgent`, and `ProgressAgent` fire thousands of times per transaction. They receive the following zero-allocation optimizations:

| Callback | Problem | Solution |
| :--- | :--- | :--- |
| **`LogAgent`** | 1. Fires unconditionally even when handlers ignore debug messages.<br>2. Calls libc `vasprintf` (`malloc` + `free` + string) on every line. | 1. Expose `LogLevel EnabledLogLevels`, defaulting to `Error | Warning`; filter before formatting.<br>2. Pass `ReadOnlySpan<byte>` to the handler.<br>3. Format with `vsnprintf` into `stackalloc byte[512]` and use `ArrayPool<byte>` for longer messages. Copy the one-shot `va_list` before each formatting attempt. |
| **`ProgressAgent`** | Re-allocates the exact same package name `string` 101 times (0% $\to$ 100%) during each package installation. | Maintain a 1-slot string cache (`_lastPkgBytes` / `_lastPkgString`) on `AlpmCallbacks`. If `pkgnameSpan.SequenceEqual(_lastPkgBytes)`, return `_lastPkgString`. Reduces allocations from 101 to 1 per package. |
| **`DownloadAgent`** | Allocates new `AlpmDownloadEvent.Progress` instances and re-allocates URL/filename strings on every curl tick. | Make `AlpmDownloadEvent` a readonly value type with a `DownloadEventType` discriminator, and cache the filename string for the active download slot. |

---

## 7. Scoped-Arena Pattern Integration

Following the investigation in `reports/scoped-arena-lifetime-evaluation.md`:

### 7.1 Evaluated Targets

| Target | Assessment | Decision & Design |
| :--- | :--- | :--- |
| **Target A: Callback Scopes** | Ideal | **Adopted.** Reusable `ChildLifetime` scope in `AlpmCallbacks` and stack-bound `ref struct`s in `AlpmQuestion`. |
| **Target B: Transaction Arena** | Clarified | **Adopted with boundary.** `LoadedPackageRegistry` remains on `AlpmSession` for packages inspected outside transactions. When handed to `Transaction.AddPackage(LoadedPackage)`, ownership is transferred to the transaction arena via `pkg.Disown()`. |
| **Target C: FFI Scratch Arena** | Ideal | **Adopted.** Unmanaged watermark bump scratch allocator in `NativeListMarshal` for transient P/Invoke calls. |
| **Target D: Bulk Traversal Lease** | Rejected | **Rejected per ADR 0010.** Retain the ~8.5 ns per-element same-thread validation check in `AlpmList<T>.Enumerator.MoveNext` to prevent Use-After-Free if a loop body disposes the session. |

### 7.2 Scratch Arena in `NativeListMarshal` (Target C)

Methods such as `CheckDependencies`, `CheckConflicts`, `ResolveDependency`, and `UpdateDatabases` construct transient `_alpm_list_t*` chains from `IEnumerable<PackageView>` or `IEnumerable<Database>`.
Instead of calling `alpm_list_add` (which performs individual native `malloc` allocations for every node), use a thread-local or session-scoped unmanaged bump allocator:

```csharp
namespace Orson.Alpm.Interop;

internal static unsafe class NativeListMarshal
{
    [ThreadStatic]
    private static UnmanagedScratchArena? t_scratchArena;

    internal static _alpm_list_t* BuildPointerList(ReadOnlySpan<nint> pointers)
    {
        var arena = t_scratchArena ??= new UnmanagedScratchArena(capacityBytes: 64 * 1024);
        var watermark = arena.SaveWatermark();

        _alpm_list_t* head = null;
        _alpm_list_t* tail = null;

        for (int i = 0; i < pointers.Length; i++)
        {
            var node = (_alpm_list_t*)arena.Allocate(sizeof(_alpm_list_t));
            node->data = (void*)pointers[i];
            node->next = null;
            node->prev = tail;

            if (tail != null) tail->next = node;
            else head = node;

            tail = node;
        }

        return head;
    }

    internal static void ResetScratch(int watermark)
    {
        t_scratchArena?.RestoreWatermark(watermark);
    }
}
```

Calls pass the head pointer to `libalpm` and reset the bump watermark in a `finally` block, completely avoiding heap fragmentation and native allocator lock overhead.

---

## 8. Transaction & API Refinements

1. **Remove Silent Transaction Joining:**
   `AlpmSession.BeginTransaction()` currently joins an existing active transaction if flags match (`AlpmSession.Seams.cs:72-84`). This obscures transaction ownership. Modify `BeginTransaction()` to throw `InvalidOperationException` if an active transaction already exists on the session.
   - *Dependencies & Prerequisites:* Standalone. Update `TransactionLifecycleTests.BeginTransaction_JoinsTheActiveTransaction_WhenTheFlagsMatch` to assert `InvalidOperationException`.
2. **Refactor `AssumeInstalled.Add`:**
   In `src/Orson.Alpm/Options/AssumeInstalled.cs`, implement stack-allocated UTF-8 string encoding without violating `AlpmOptionList<T>.AcquireForAdd` lifetime semantics.
3. **Enumerator Optimization:**
   In `AlpmList<T>.Enumerator.MoveNext()`, cache the resolved item in a private `_currentItem` field during advancement instead of re-executing `_factory(_current->data, _stamp)` on every `.Current` property access.
   - *Dependencies & Prerequisites:* Standalone. Retains ADR 0010 same-thread validation checks.
4. **Remove Dead Unmanaged Destructors:**
   Remove dead or duplicate destructors (`Dependency.FreeNative()`) and route all unmanaged resource reclamation through `src/Orson.Alpm/Interop/UnmanagedDestructors.cs`.
   - *Dependencies & Prerequisites:* Standalone.

---

## 9. Phased Implementation Plan

### 9.1 Dependency Matrix

| Phase       | Status  | Core Deliverables                                                        | Prerequisites & Blockers | Downstream Impact                                                  |
|:------------|---------|:-------------------------------------------------------------------------|:-------------------------|:-------------------------------------------------------------------|
| **Phase 0** | Done    | Standalone Bug Fixes & Refinements (§3.2, §8.1, §8.3, §8.4)              | None                     | Unblocks transactional inspection queries                          |
| **Phase 1** | Done    | Process Concurrency Guard & Lifetime Decoupling (§2.3, §2.4, §2.5, §3.3) | None                     | Unblocks safe parallel test runner execution and async workers     |
| **Phase 2** | Done    | Zero-Allocation Package Architecture (§3.4, §4.1–§4.5)                   | None                     | Prerequisite for FFI scratch arena and zero-allocation scans       |
| **Phase 3** | Done    | Database Hierarchy Split & List Lifetime Isolation (§3.1, §5.1–§5.2)     | None                     | Eliminates runtime capability checks and cascading unregister bugs |
| **Phase 3.5** | Done  | Strictly Non-Nullable Lifetime & List Decoupling (§3.4)                  | Phase 2, Phase 3         | Eliminates AlpmOwnedList, strips defensive null checks from factories|
| **Phase 4** | Done    | Callback & Event System Overhaul 4C (§6.1–§6.3)                          | None                     | Eliminates high-frequency hot path GC churn                        |
| **Phase 5** | Done | Unmanaged Scratch Arena (§7.1–§7.2)                                      | Phase 2                  | Eliminates native list malloc/free overhead in P/Invoke calls      |
| **Phase 6** | Pending | Verification & Acceptance Testing (§10.1–§10.4)                          | Phases 0–5               | Validates test pass and benchmark allocations                      |

### 9.2 Execution Order

```
┌────────────────────────────────────────────────────────┐
│ Phase 0: Standalone Bug Fixes & Refinements            │
│ - Fix Transaction.TryCommit() over-invalidation (§3.2) │
│ - Remove silent transaction joining (§8.1)             │
│ - Cache _currentItem in AlpmList.Enumerator (§8.3)     │
│ - Remove dead unmanaged destructors (§8.4)             │
│ - Update TransactionLifecycleTests (§8.1)              │
└───────────────────────────┬────────────────────────────┘
                            │
┌───────────────────────────▼────────────────────────────┐
│ Phase 1: Process Concurrency Guard & Safe Lifetime     │
│ - Implement AlpmSession.Acquire process lease (§2.3)   │
│ - Implement NativeGate re-entrancy lock (§2.4)         │
│ - Decouple RootLifetime._owner to SafeAlpmHandle (§3.3)│
│ - Annotate xUnit fixtures with [Collection] (§2.5)     │
│ - Update LifetimeGcPathTests to assert handle liveness │
└───────────────────────────┬────────────────────────────┘
                            │
┌───────────────────────────▼────────────────────────────┐
│ Phase 2: Zero-Allocation Package Architecture          │
│ - Convert PackageView to 40-byte readonly struct (§4.1)│
│ - Enforce non-nullable embedded LifetimeStamp (§3.4)   │
│ - Fix unanchored group package views in Groups.cs(§3.4)│
│ - Implement IPackageMetadata interface (§4.2)          │
│ - Refactor LoadedPackage to composition model (§4.3)   │
│ - Gate Files and BackupFiles in PackageSnapshot (§4.4) │
│ - Implement zero-copy UTF-8 spans on PackageView (§4.5)│
└───────────────────────────┬────────────────────────────┘
                            │
┌───────────────────────────▼────────────────────────────┐
│ Phase 3: Database Hierarchy Split & List Domain        │
│ - Split Database into LocalDatabase and SyncDatabase   │
│ - Add ChildLifetime _syncDatabasesLifetime domain      │
│ - Fix Database.Unregister() cascading invalidation(§3.1│
│ - Move Update(), Unregister(), Servers to SyncDatabase │
│ - Update LifetimeInvalidationTests for sync list target│
└───────────────────────────┬────────────────────────────┘
                            │
┌───────────────────────────▼────────────────────────────┐
│ Phase 3.5: Strict Non-Nullable Lifetime & Decoupling   │
│ - Replace AlpmOwnedList with NativeListMarshal helper  │
│ - Enforce strictly non-nullable Lifetime in AlpmList   │
│ - Strip runtime null checks from element factories     │
│ - Apply strict context to empty lists (no sentinels)   │
│ - Remove unanchored parameterless AlpmStringList ctor  │
└───────────────────────────┬────────────────────────────┘
                            │
┌───────────────────────────▼────────────────────────────┐
│ Phase 4: Callback & Event System Overhaul (4C)         │
│ - Cache 23 stateless unit events as singletons (§6.2)  │
│ - Implement reusable _eventLifetime scope (§6.2)       │
│ - Implement LogLevel EnabledLogLevels filter (§6.3)    │
│ - Implement vsnprintf stack buffer in LogAgent (§6.3)  │
│ - Implement 1-slot string cache in ProgressAgent (§6.3)│
│ - Pass callback-scoped UTF-8 spans for logs (§6.3)     │
│ - Make AlpmDownloadEvent a readonly value type (§6.3)  │
└───────────────────────────┬────────────────────────────┘
                            │
┌───────────────────────────▼────────────────────────────┐
│ Phase 5: Unmanaged Scratch Arena (NativeListMarshal)   │
│ - Implement UnmanagedScratchArena bump allocator (§7.2)│
│ - Route transient P/Invoke pointer lists through arena │
└───────────────────────────┬────────────────────────────┘
                            │
┌───────────────────────────▼────────────────────────────┐
│ Phase 6: Verification & Acceptance Criteria            │
│ - Run full xUnit test suite hermetically (§10.1)       │
│ - Run BenchmarkDotNet suite to prove zero alloc (§10.2)│
│ - Verify no cascading invalidation across sync DBs     │
└────────────────────────────────────────────────────────┘
```

---

## 10. Verification & Acceptance Criteria

1. **Hermetic Test Suite Pass:**
   Execute `dotnet test tests/Orson.Alpm.Tests/Orson.Alpm.Tests.csproj`. All unit and integration tests must pass without deadlocks, lease refusal errors, or access violations.
2. **Zero Heap Allocation in Package Scans:**
   BenchmarkDotNet benchmarks in `src/Orson.Benchmarks` must measure `0 B` allocated memory per element during package cache enumeration (`db.LoadPackageCache()`) and search when reading UTF-8 metadata spans.
3. **No Cascading Invalidation:**
   Unregistering a sync database must leave all other registered databases and their package views fully operational.
   Committing a transaction must invalidate only the local database domain while remote sync databases remain valid.
4. **No Callback Churn:**
   Progress, download, and log callback execution during a 500-package simulation must produce negligible Gen-0 collections.
