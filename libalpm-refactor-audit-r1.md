# Orson Pre-Release Architecture & Refactoring Specification

## 1. Executive Summary of Changes

1. **Critical Bug Fixes in Lifetime Invalidation:**
   * **`Database.Unregister()` Bug:** Remove `root.Invalidate("Database.Unregister()")` from `_onUnregistered` in `Database.Factory` and `AlpmSession.RegisterSyncDatabase` so unregistering a single sync DB does not nuke the root session and sibling databases.
   * **`Transaction.TryCommit()` Over-Invalidation:** Stop invalidating `RootLifetime` during `Transaction.TryCommit()`. Only invalidate `_localDatabaseLifetime` (pre- and post-commit) so sync databases and sync `PackageView`s survive transaction commits.
   * **`RootLifetime._owner` Decoupling:** Change `RootLifetime._owner` from `AlpmSession` to `SafeAlpmHandle` so live views anchor the native handle without creating managed reference cycles that block session finalization.
   * **Single-Threaded Re-Entrance Guard:** Add a thread-ID check on `RootLifetime` to immediately catch illegal multithreaded access to the same `AlpmSession` while permitting same-thread callback re-entrance.
2. **Zero-Allocation Package Architecture (Option 2):**
   * Convert **`PackageView`** into a 40-byte **`readonly unsafe struct`** (`8B` `_alpm_pkg_t*` + `32B` non-nullable `LifetimeStamp`), eliminating 100% of heap allocations when iterating package caches (`db.LoadPackageCache()`) or search results.
   * Remove lazy `field ??=` string caching from live views (which bloated each view by ~80 bytes of null references).
   * Refactor **`LoadedPackage`** to use composition (`public PackageView View { get; }`) and an implicit conversion operator (`implicit operator PackageView(LoadedPackage)`), eliminating code duplication while preserving strict `IDisposable` / `Disown()` semantics.
   * Unify `PackageView`, `LoadedPackage`, and `PackageSnapshot` under **`IPackageMetadata`** (with `IReadOnlyList<PackageFile>? Files { get; }`).
   * Gate `BackupFiles` alongside `Files` in `PackageSnapshot(pkg, includeFiles)` when snapshotting local packages to prevent `alpm_pkg_get_backup` from triggering `libalpm`’s expensive `INFRQ_FILES` disk parse when `includeFiles == false`.
3. **Database Class Hierarchy Split:**
   * Split `Database` into `abstract class Database`, `sealed class LocalDatabase : Database`, and `sealed class SyncDatabase : Database`. Move `Update()`, `Unregister()`, `Servers`, and `CacheServers` onto `SyncDatabase`.
4. **Surgical `ref struct` & API Tightening:**
   * Convert `AlpmEvent` into a `readonly ref struct` (matching `AlpmQuestion`) with `.ToSnapshot()`, deleting the per-callback `ChildLifetime` heap allocation in `EventAgent`.
   * Cache `_currentItem` inside `AlpmList<T>.Enumerator.MoveNext()` instead of re-invoking `Factory` on every `.Current` property read.
   * Remove silent transaction joining from `AlpmSession.BeginTransaction()`.

---

## 2. Package Architecture Refactoring (Option 2)

### 2.1 Topology Diagram

```
                 ┌────────────────────────────────────────────────┐
                 │           interface IPackageMetadata           │
                 │  - Scalar properties (Name, Version, Size...)  │
                 │  - IReadOnlyCollection<T> collections          │
                 │  - IReadOnlyList<PackageFile>? Files { get; }  │
                 └───────────────────────┬────────────────────────┘
                                         │ implements
         ┌───────────────────────────────┼───────────────────────────────┐
         ▼                               ▼                               ▼
[readonly struct PackageView]   [sealed class LoadedPackage]    [sealed class PackageSnapshot]
 - _alpm_pkg_t* _ptr (8B)        - Implements IDisposable        - Pure managed heap properties
 - LifetimeStamp _stamp (32B)    - ChildLifetime _lifetime       - Never expires
 - Zero heap allocation          - public PackageView View       - Files & BackupFiles gated
 - Non-disposable                - implicit operator PackageView   by includeFiles flag
```

### 2.2 `IPackageMetadata.cs`

```csharp
namespace Orson.Alpm;

public interface IPackageMetadata
{
  string Name { get; }
  PackageVersion Version { get; }
  string? Filename { get; }
  string? Base { get; }
  string? Description { get; }
  string? Url { get; }
  DateTimeOffset BuildDate { get; }
  DateTimeOffset? InstallDate { get; }
  string? Packager { get; }
  string? Md5Sum { get; }
  string? Sha256Sum { get; }
  string? Architecture { get; }
  long Size { get; }
  long InstalledSize { get; }
  long DownloadSize { get; }
  PackageOrigin Origin { get; }
  PackageReason Reason { get; }
  PackageValidation Validation { get; }
  bool HasScriptlet { get; }
  string? Base64Signature { get; }

  IReadOnlyCollection<string> Licenses { get; }
  IReadOnlyCollection<string> Groups { get; }
  IReadOnlyCollection<Dependency> Depends { get; }
  IReadOnlyCollection<Dependency> OptionalDepends { get; }
  IReadOnlyCollection<Dependency> Conflicts { get; }
  IReadOnlyCollection<Dependency> Provides { get; }
  IReadOnlyCollection<Dependency> Replaces { get; }
  IReadOnlyCollection<Dependency> CheckDepends { get; }
  IReadOnlyCollection<Dependency> MakeDepends { get; }

  /// <summary>
  /// Package file list. Always available (lazily loaded by libalpm) on live views;
  /// null on <see cref="PackageSnapshot"/> unless captured with <c>includeFiles: true</c>.
  /// </summary>
  IReadOnlyList<PackageFile>? Files { get; }

  /// <summary>
  /// Package backup entries. Null on <see cref="PackageSnapshot"/> if skipped via <c>includeFiles: false</c>.
  /// </summary>
  IReadOnlyCollection<BackupEntry>? BackupFiles { get; }

  PackageSnapshot ToSnapshot(bool includeFiles = false);
}
```

---

### 2.3 `PackageView.cs` (`readonly struct`)

By making `PackageView` a `readonly struct` with a non-nullable `LifetimeStamp` and no cached string fields, its size is fixed at **40 bytes** (`8B` pointer + `32B` stamp).

```csharp
namespace Orson.Alpm;

public readonly unsafe partial struct PackageView : IPackageMetadata, IEquatable<PackageView>
{
  private readonly _alpm_pkg_t* _backingStruct;
  private readonly LifetimeStamp _stamp;

  internal PackageView(_alpm_pkg_t* backingStruct, ChildLifetime lifetime)
  {
    ArgumentNullException.ThrowIfNull(lifetime);
    _backingStruct = backingStruct;
    _stamp = lifetime.Capture();
  }

  internal _alpm_pkg_t* BackingStruct
  {
    get
    {
      ThrowIfStale();
      return _backingStruct;
    }
  }

  internal Lifetime Domain => _stamp.Domain
    ?? throw new AlpmLifetimeException("PackageView is uninitialized (default struct).");

  private void ThrowIfStale()
  {
    if (_backingStruct == null)
      throw new AlpmLifetimeException("PackageView is uninitialized (default struct).");
    _stamp.ThrowIfStale();
  }

  internal _alpm_handle_t* LibraryHandle
  {
    get
    {
      var handle = NativeMethods.alpm_pkg_get_handle(BackingStruct);
      GC.KeepAlive(_stamp.Domain);
      return handle;
    }
  }

  // Factory function pointer target for AlpmList<PackageView>
  internal static PackageView Factory(void* ptr, Lifetime? lifetime)
  {
    if (lifetime is not ChildLifetime childLifetime)
    {
      throw new InvalidOperationException(
        "PackageView must be anchored to a ChildLifetime domain.");
    }
    return new PackageView((_alpm_pkg_t*)ptr, childLifetime);
  }

  // Direct, zero-overhead unmanaged queries (no hidden string? backing fields)
  public string Name
  {
    get
    {
      var str = NativeString.FromNative((nint)NativeMethods.alpm_pkg_get_name(BackingStruct))!;
      GC.KeepAlive(_stamp.Domain);
      return str;
    }
  }

  public PackageVersion Version
  {
    get
    {
      var version = new PackageVersion(NativeMethods.alpm_pkg_get_version(BackingStruct));
      GC.KeepAlive(_stamp.Domain);
      return version;
    }
  }

  public PackageOrigin Origin
  {
    get
    {
      var origin = (PackageOrigin)(uint)NativeMethods.alpm_pkg_get_origin(BackingStruct);
      GC.KeepAlive(_stamp.Domain);
      return origin;
    }
  }

  public long Size
  {
    get
    {
      var size = NativeMethods.alpm_pkg_get_size(BackingStruct).Value;
      GC.KeepAlive(_stamp.Domain);
      return size;
    }
  }

  public long InstalledSize
  {
    get
    {
      var isize = NativeMethods.alpm_pkg_get_isize(BackingStruct).Value;
      GC.KeepAlive(_stamp.Domain);
      return isize;
    }
  }

  // Concrete high-efficiency collection getters
  public AlpmStringList Licenses => new(NativeMethods.alpm_pkg_get_licenses(BackingStruct), Domain);
  public AlpmStringList Groups => new(NativeMethods.alpm_pkg_get_groups(BackingStruct), Domain);
  public AlpmList<Dependency> Depends => Dependency.ListFactory(NativeMethods.alpm_pkg_get_depends(BackingStruct), Domain);
  public AlpmList<Dependency> OptionalDepends => Dependency.ListFactory(NativeMethods.alpm_pkg_get_optdepends(BackingStruct), Domain);
  public AlpmList<Dependency> Conflicts => Dependency.ListFactory(NativeMethods.alpm_pkg_get_conflicts(BackingStruct), Domain);
  public AlpmList<Dependency> Provides => Dependency.ListFactory(NativeMethods.alpm_pkg_get_provides(BackingStruct), Domain);
  public AlpmList<Dependency> Replaces => Dependency.ListFactory(NativeMethods.alpm_pkg_get_replaces(BackingStruct), Domain);
  public AlpmList<Dependency> CheckDepends => Dependency.ListFactory(NativeMethods.alpm_pkg_get_checkdepends(BackingStruct), Domain);
  public AlpmList<Dependency> MakeDepends => Dependency.ListFactory(NativeMethods.alpm_pkg_get_makedepends(BackingStruct), Domain);
  public FileList Files => new(NativeMethods.alpm_pkg_get_files(BackingStruct), Domain);
  public AlpmList<BackupEntry> BackupFiles => BackupEntry.ListFactory(NativeMethods.alpm_pkg_get_backup(BackingStruct), Domain);

  // Explicit IPackageMetadata interface implementations for collection covariance
  IReadOnlyCollection<string> IPackageMetadata.Licenses => Licenses;
  IReadOnlyCollection<string> IPackageMetadata.Groups => Groups;
  IReadOnlyCollection<Dependency> IPackageMetadata.Depends => Depends;
  IReadOnlyCollection<Dependency> IPackageMetadata.OptionalDepends => OptionalDepends;
  IReadOnlyCollection<Dependency> IPackageMetadata.Conflicts => Conflicts;
  IReadOnlyCollection<Dependency> IPackageMetadata.Provides => Provides;
  IReadOnlyCollection<Dependency> IPackageMetadata.Replaces => Replaces;
  IReadOnlyCollection<Dependency> IPackageMetadata.CheckDepends => CheckDepends;
  IReadOnlyCollection<Dependency> IPackageMetadata.MakeDepends => MakeDepends;
  IReadOnlyList<PackageFile>? IPackageMetadata.Files => Files;
  IReadOnlyCollection<BackupEntry>? IPackageMetadata.BackupFiles => BackupFiles;

  public PackageSnapshot ToSnapshot(bool includeFiles = false) => new(this, includeFiles);

  public bool Equals(PackageView other) => _backingStruct == other._backingStruct;
  public override bool Equals(object? obj) => obj is PackageView other && Equals(other);
  public override int GetHashCode() => ((nint)_backingStruct).GetHashCode();
}
```
*(Note: Notice how `GC.KeepAlive(this)` in `Package.cs` becomes `GC.KeepAlive(_stamp.Domain)` inside `PackageView`, because `this` is now a value type on the stack, whereas `_stamp.Domain` is the reference object rooting `SafeAlpmHandle`!)*

---

### 2.4 `LoadedPackage.cs` (Composition + Implicit `PackageView` Conversion)

Instead of inheriting from a base `Package` class, `LoadedPackage` wraps a `PackageView` stamped with its own `ChildLifetime`. When `LoadedPackage.Dispose()` or `LoadedPackage.Disown()` runs, `_lifetime.Invalidate(...)` automatically invalidates both `LoadedPackage` and any `PackageView` converted from it!

```csharp
namespace Orson.Alpm;

public sealed unsafe class LoadedPackage : IPackageMetadata, IDisposable
{
  private readonly LoadedPackageRegistry _registry;
  private readonly ChildLifetime _lifetime;
  private readonly PackageView _view;
  private nint _rawPackage;
  private bool _disposed;

  internal LoadedPackage(LoadedPackageRegistry registry, _alpm_pkg_t* backingStruct, ChildLifetime lifetime)
  {
    _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    _lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
    _rawPackage = (nint)backingStruct;
    _view = new PackageView(backingStruct, lifetime);
  }

  internal bool OwnsPackage => !_disposed && _rawPackage != 0;

  internal _alpm_pkg_t* BackingStruct
  {
    get
    {
      ThrowIfNotOwned();
      return _view.BackingStruct;
    }
  }

  /// <summary>
  /// Returns a borrowed <see cref="PackageView"/> tied to this <see cref="LoadedPackage"/>'s lifetime.
  /// If this <see cref="LoadedPackage"/> is disposed or handed over to a transaction, this view becomes stale.
  /// </summary>
  public PackageView View
  {
    get
    {
      ThrowIfNotOwned();
      return _view;
    }
  }

  public static implicit operator PackageView(LoadedPackage loaded)
  {
    ArgumentNullException.ThrowIfNull(loaded);
    return loaded.View;
  }

  internal void ThrowIfNotOwned()
  {
    ObjectDisposedException.ThrowIf(!OwnsPackage, this);
    _lifetime.ThrowIfStale();
  }

  internal void Disown()
  {
    ThrowIfNotOwned();
    var pkg = (_alpm_pkg_t*)Interlocked.Exchange(ref _rawPackage, 0);
    if (pkg == null) return;
    _disposed = true;
    _registry.Unregister(pkg, freeNative: false);
    _lifetime.Invalidate("the hand-over to a transaction");
  }

  public void Dispose()
  {
    if (_disposed) return;
    _disposed = true;
    var pkg = (_alpm_pkg_t*)Interlocked.Exchange(ref _rawPackage, 0);
    if (pkg == null) return;
    _lifetime.Invalidate("LoadedPackage.Dispose()");
    _registry.Unregister(pkg, freeNative: true);
  }

  // Forwarded properties to View (zero duplication of native P/Invoke logic)
  public string Name => View.Name;
  public PackageVersion Version => View.Version;
  public string? Filename => View.Filename;
  public string? Base => View.Base;
  public string? Description => View.Description;
  public string? Url => View.Url;
  public DateTimeOffset BuildDate => View.BuildDate;
  public DateTimeOffset? InstallDate => View.InstallDate;
  public string? Packager => View.Packager;
  public string? Md5Sum => View.Md5Sum;
  public string? Sha256Sum => View.Sha256Sum;
  public string? Architecture => View.Architecture;
  public long Size => View.Size;
  public long InstalledSize => View.InstalledSize;
  public long DownloadSize => View.DownloadSize;
  public PackageOrigin Origin => View.Origin;
  public PackageReason Reason => View.Reason;
  public PackageValidation Validation => View.Validation;
  public bool HasScriptlet => View.HasScriptlet;
  public string? Base64Signature => View.Base64Signature;

  public AlpmStringList Licenses => View.Licenses;
  public AlpmStringList Groups => View.Groups;
  public AlpmList<Dependency> Depends => View.Depends;
  public AlpmList<Dependency> OptionalDepends => View.OptionalDepends;
  public AlpmList<Dependency> Conflicts => View.Conflicts;
  public AlpmList<Dependency> Provides => View.Provides;
  public AlpmList<Dependency> Replaces => View.Replaces;
  public AlpmList<Dependency> CheckDepends => View.CheckDepends;
  public AlpmList<Dependency> MakeDepends => View.MakeDepends;
  public FileList Files => View.Files;
  public AlpmList<BackupEntry> BackupFiles => View.BackupFiles;

  IReadOnlyCollection<string> IPackageMetadata.Licenses => Licenses;
  IReadOnlyCollection<string> IPackageMetadata.Groups => Groups;
  IReadOnlyCollection<Dependency> IPackageMetadata.Depends => Depends;
  IReadOnlyCollection<Dependency> IPackageMetadata.OptionalDepends => OptionalDepends;
  IReadOnlyCollection<Dependency> IPackageMetadata.Conflicts => Conflicts;
  IReadOnlyCollection<Dependency> IPackageMetadata.Provides => Provides;
  IReadOnlyCollection<Dependency> IPackageMetadata.Replaces => Replaces;
  IReadOnlyCollection<Dependency> IPackageMetadata.CheckDepends => CheckDepends;
  IReadOnlyCollection<Dependency> IPackageMetadata.MakeDepends => MakeDepends;
  IReadOnlyList<PackageFile>? IPackageMetadata.Files => Files;
  IReadOnlyCollection<BackupEntry>? IPackageMetadata.BackupFiles => BackupFiles;

  public PackageSnapshot ToSnapshot(bool includeFiles = false) => new(View, includeFiles);
}
```

---

### 2.5 `PackageSnapshot.cs` (Gating `Files` and `BackupFiles` on `includeFiles`)

```csharp
namespace Orson.Alpm;

public sealed class PackageSnapshot : IPackageMetadata
{
  public PackageSnapshot(PackageView package, bool includeFiles = false)
  {
    Name = package.Name;
    Version = package.Version;
    Filename = package.Filename;
    Base = package.Base;
    Description = package.Description;
    Url = package.Url;
    BuildDate = package.BuildDate;
    InstallDate = package.InstallDate;
    Packager = package.Packager;
    Md5Sum = package.Md5Sum;
    Sha256Sum = package.Sha256Sum;
    Architecture = package.Architecture;
    Size = package.Size;
    InstalledSize = package.InstalledSize;
    Origin = package.Origin;
    Reason = package.Reason;
    Validation = package.Validation;
    Licenses = [.. package.Licenses];
    Groups = [.. package.Groups];
    Depends = [.. package.Depends];
    OptionalDepends = [.. package.OptionalDepends];
    Conflicts = [.. package.Conflicts];
    Provides = [.. package.Provides];
    Replaces = [.. package.Replaces];
    CheckDepends = [.. package.CheckDepends];
    MakeDepends = [.. package.MakeDepends];
    DownloadSize = package.DownloadSize;
    HasScriptlet = package.HasScriptlet;
    Base64Signature = package.Base64Signature;

    // Both %FILES% and %BACKUP% reside in /var/lib/pacman/local/<pkg>/files (INFRQ_FILES).
    // Only trigger libalpm's INFRQ_FILES disk read when includeFiles is requested!
    if (includeFiles)
    {
      Files = [.. package.Files];
      BackupFiles = [.. package.BackupFiles];
    }
  }

  // ... (All properties matching IPackageMetadata) ...
  public IReadOnlyList<PackageFile>? Files { get; }
  public IReadOnlyList<BackupEntry>? BackupFiles { get; }

  IReadOnlyCollection<string> IPackageMetadata.Licenses => Licenses;
  IReadOnlyCollection<string> IPackageMetadata.Groups => Groups;
  IReadOnlyCollection<Dependency> IPackageMetadata.Depends => Depends;
  IReadOnlyCollection<Dependency> IPackageMetadata.OptionalDepends => OptionalDepends;
  IReadOnlyCollection<Dependency> IPackageMetadata.Conflicts => Conflicts;
  IReadOnlyCollection<Dependency> IPackageMetadata.Provides => Provides;
  IReadOnlyCollection<Dependency> IPackageMetadata.Replaces => Replaces;
  IReadOnlyCollection<Dependency> IPackageMetadata.CheckDepends => CheckDepends;
  IReadOnlyCollection<Dependency> IPackageMetadata.MakeDepends => MakeDepends;
  IReadOnlyCollection<BackupEntry>? IPackageMetadata.BackupFiles => BackupFiles;

  public PackageSnapshot ToSnapshot(bool includeFiles = false) => this;
}
```

---

## 3. Database Class Hierarchy & Bug Fix

Split `Database` into `abstract class Database`, `LocalDatabase`, and `SyncDatabase`, while fixing the `root.Invalidate("Database.Unregister()")` bug in `SyncDatabase`:

```csharp
namespace Orson.Alpm;

public abstract unsafe partial class Database
{
  internal _alpm_db_t* ValidatedPtr
  {
    get
    {
      ThrowIfInvalidated();
      return field;
    }
    private init;
  }

  private protected _alpm_handle_t* RawHandle => NativeMethods.alpm_db_get_handle(ValidatedPtr);
  internal readonly ChildLifetime Lifetime;
  private protected LifetimeStamp Stamp;

  private protected Database(_alpm_db_t* backingStruct, ChildLifetime lifetime)
  {
    ValidatedPtr = backingStruct;
    Lifetime = lifetime;
    Stamp = lifetime.Capture();
  }

  public string Name
  {
    get
    {
      var str = NativeString.FromNative((nint)NativeMethods.alpm_db_get_name(ValidatedPtr))!;
      GC.KeepAlive(this);
      return str;
    }
  }

  public PackageView? GetPackage(string name)
  {
    Span<byte> scratch = stackalloc byte[64];
    using var nameBuf = new Utf8Buffer(name, scratch);
    var pkg = NativeMethods.alpm_db_get_pkg(ValidatedPtr, nameBuf.Ptr);
    GC.KeepAlive(this);
    if (pkg == null) return null;
    return new PackageView(pkg, Lifetime);
  }

  public AlpmList<PackageView> LoadPackageCache()
  {
    var pkgCache = NativeMethods.alpm_db_get_pkgcache(ValidatedPtr);
    GC.KeepAlive(this);
    return AlpmList<PackageView>.Borrow(pkgCache, &PackageView.Factory, Lifetime);
  }

  internal static ChildLifetime ResolveLifetime(IReadOnlyList<Database> databases, _alpm_db_t* dbPtr)
  {
    foreach (var database in databases)
    {
      if (database.ValidatedPtr == dbPtr) return database.Lifetime;
    }
    throw new InvalidOperationException("libalpm returned package owned by database not in parameter list.");
  }

  private protected void ThrowIfInvalidated() => Stamp.ThrowIfStale();
}

// -----------------------------------------------------------------------------
// LocalDatabase: Cannot be unregistered, updated, or have servers configured
// -----------------------------------------------------------------------------
public sealed unsafe class LocalDatabase : Database
{
  internal LocalDatabase(_alpm_db_t* backingStruct, ChildLifetime lifetime)
    : base(backingStruct, lifetime)
  {
  }
}

// -----------------------------------------------------------------------------
// SyncDatabase: Supports Update, Unregister, Servers, and CacheServers
// -----------------------------------------------------------------------------
public sealed unsafe partial class SyncDatabase : Database
{
  private readonly Action? _onUnregistered;

  internal SyncDatabase(_alpm_db_t* backingStruct, ChildLifetime lifetime, Action? onUnregistered = null)
    : base(backingStruct, lifetime)
  {
    _onUnregistered = onUnregistered;
  }

  internal static SyncDatabase Factory(void* ptr, Lifetime? lifetime)
  {
    ArgumentNullException.ThrowIfNull(lifetime);
    if (lifetime is not RootLifetime root)
    {
      throw new InvalidOperationException("Sync databases must be enumerated against a root session lifetime domain.");
    }

    var name = NativeString.FromNative((nint)NativeMethods.alpm_db_get_name((_alpm_db_t*)ptr)) ?? "(unknown)";
    return new SyncDatabase(
      (_alpm_db_t*)ptr,
      root.GetLifetimeTokenForHandle(ptr, $"the sync database {name}"),
      onUnregistered: () =>
      {
        // FIX: Only forget the handle from the registry!
        // Do NOT call root.Invalidate(), which would kill the entire session and sibling DBs!
        root.ForgetHandle(ptr);
      });
  }

  public AlpmStringList Servers
  {
    get
    {
      var servers = NativeMethods.alpm_db_get_servers(ValidatedPtr);
      GC.KeepAlive(this);
      return new AlpmStringList(servers, Lifetime.Facet(LifetimeFacet.Servers, "the server list"));
    }
  }

  public void AddServer(string url)
  {
    // ... native call ...
    Lifetime.InvalidateFacet(LifetimeFacet.Servers, "SyncDatabase.AddServer");
    GC.KeepAlive(this);
  }

  internal void InvalidateViews(string reason)
  {
    Lifetime.Invalidate(reason);
    Stamp = Lifetime.Capture();
  }

  public void Unregister()
  {
    var err = NativeMethods.alpm_db_unregister(ValidatedPtr);
    if (err != 0) throw AlpmFailure.Of(RawHandle, "unregister database").ToException();

    Lifetime.Invalidate("SyncDatabase.Unregister()");
    _onUnregistered?.Invoke();
    GC.KeepAlive(this);
  }
}
```

---

## 4. `AlpmList<T>.Enumerator` Fix

In `AlpmList.cs`, update `Enumerator` so that `Factory` is invoked once inside `MoveNext()` rather than repeatedly on every `Current` property read:

```csharp
public unsafe struct Enumerator : IEnumerator<T>
{
  private readonly AlpmList<T> _list;
  private _alpm_list_t* _current;
  private T? _currentItem;
  private bool _started;
  private bool _disposed;

  internal Enumerator(AlpmList<T> list)
  {
    _list = list;
    _current = null;
    _currentItem = default;
    _started = false;
    _disposed = false;
  }

  public T Current
  {
    get
    {
      if (_disposed) throw new ObjectDisposedException(GetType().FullName);
      if (!_started || _current is null) throw new InvalidOperationException();
      _list.Lifetime?.ThrowIfStale();
      return _currentItem!;
    }
  }

  public bool MoveNext()
  {
    if (_disposed) throw new ObjectDisposedException(GetType().FullName);
    _current = _started
      ? (_list.Lifetime?.ThrowIfStale(), _current is null ? null : _current->next).Item2
      : _list.ValidatedNative();
    _started = true;

    if (_current != null)
    {
      _currentItem = _list.Factory(_current->data, _list.Lifetime?.Domain);
      GC.KeepAlive(_list);
      return true;
    }

    _currentItem = default;
    return false;
  }

  public void Dispose()
  {
    _currentItem = default;
    _disposed = true;
  }

  object IEnumerator.Current => Current!;
  public void Reset() => throw new NotSupportedException();
}
```

---

## 5. Master Implementation Checklist

- [ ] **Step 1: Fix Lifetime Invalidation Bugs**
  - [ ] In `Database.Factory` and `AlpmSession.RegisterSyncDatabase`, remove `root.Invalidate("Database.Unregister()")` from the `_onUnregistered` callback (keep `root.ForgetHandle(ptr)`).
  - [ ] In `Transaction.TryCommit()`, replace `_session.RootLifetime.Invalidate("Transaction.Commit()")` with `_session.InvalidateLocalDatabase("Transaction.Commit() [Pre]")` so sync databases and sync `PackageView`s survive commits.
  - [ ] Change `RootLifetime._owner` from `AlpmSession` to `SafeAlpmHandle`.
  - [ ] Add a thread-ID re-entrancy guard on `RootLifetime` to throw `InvalidOperationException` on concurrent multi-threaded access.
- [ ] **Step 2: Package Hierarchy Refactoring (Option 2)**
  - [ ] Create `IPackageMetadata` with `IReadOnlyList<PackageFile>? Files { get; }` and `IReadOnlyCollection<BackupEntry>? BackupFiles { get; }`.
  - [ ] Convert `PackageView` into a 40-byte `public readonly unsafe partial struct PackageView : IPackageMetadata, IEquatable<PackageView>` (merging `Package.cs` methods into `PackageView.cs`, removing `field ??=` string caches, and replacing `GC.KeepAlive(this)` with `GC.KeepAlive(_stamp.Domain)`).
  - [ ] Update `GcAnchorAuditTests` if needed to recognize `GC.KeepAlive(_stamp.Domain)` on `struct PackageView`.
  - [ ] Refactor `LoadedPackage` to compose `PackageView View` and provide `public static implicit operator PackageView(LoadedPackage)`.
  - [ ] Update `PackageSnapshot` to implement `IPackageMetadata` and gate both `Files` and `BackupFiles` inside `if (includeFiles)`.
- [ ] **Step 3: Database Hierarchy Split & List Enumerator**
  - [ ] Split `Database` into `abstract class Database`, `sealed class LocalDatabase : Database`, and `sealed class SyncDatabase : Database`.
  - [ ] Update `AlpmList<T>.Enumerator` to construct `_currentItem` inside `MoveNext()` instead of `Current.get`.
- [ ] **Step 4: API & Callback Cleanup**
  - [ ] Convert `AlpmEvent` to `readonly ref struct` (with `.ToSnapshot()`) and remove per-callback `ChildLifetime` creation in `EventAgent`.
  - [ ] Remove silent transaction joining from `AlpmSession.BeginTransaction()` and expose `AlpmSession.CurrentTransaction`.
  - [ ] Remove `Dependency.FreeNative()`.
