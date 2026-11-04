# Code Inspection: Package Hierarchy, Database, and List Instantiation

This document provides the extracted code for the independent architectural audit. To minimize token overhead while preserving fidelity, boilerplate doc comments and peripheral string/formatting helper methods have been removed.

---

## 1. Package Class Hierarchy

### 1.1 Base Class: `Package.cs`
Abstract base class representing package metadata. Subclassed by borrowed `PackageView` and owning `LoadedPackage`.
* Holds the native `_alpm_pkg_t*` behind a guarded property.
* Manages `LifetimeStamp? Lifetime` and `bool Disposed`.
* Demonstrates lazy caching of string fields (`Name`, `Description`) versus direct unmanaged queries.

```csharp
namespace Orson.Alpm;

public abstract unsafe partial class Package
{
  private protected Package(_alpm_pkg_t* backingStruct)
  {
    BackingStruct = backingStruct;
  }

  internal _alpm_pkg_t* BackingStruct
  {
    get
    {
      ThrowIfDisposed();
      return field;
    }
    private init;
  }

  private protected LifetimeStamp? Lifetime;
  private protected bool Disposed;

  private protected void ThrowIfDisposed()
  {
    ObjectDisposedException.ThrowIf(Disposed, this);
    Lifetime?.ThrowIfStale();
  }

  internal _alpm_handle_t* LibraryHandle
  {
    get
    {
      var handle = NativeMethods.alpm_pkg_get_handle(BackingStruct);
      GC.KeepAlive(this);
      return handle;
    }
  }

  // Cached scalar metadata (guard runs before returning cached string)
  public string Name
  {
    get
    {
      ThrowIfDisposed();
      return field ??= NativeString.FromNative((nint)NativeMethods.alpm_pkg_get_name(BackingStruct))!;
    }
  }

  public PackageVersion Version
  {
    get
    {
      var version = new PackageVersion(NativeMethods.alpm_pkg_get_version(BackingStruct));
      GC.KeepAlive(this);
      return version;
    }
  }

  public PackageOrigin Origin
  {
    get
    {
      var origin = (PackageOrigin)(uint)NativeMethods.alpm_pkg_get_origin(BackingStruct);
      GC.KeepAlive(this);
      return origin;
    }
  }

  public long Size => NativeMethods.alpm_pkg_get_size(BackingStruct).Value;
  public long InstalledSize => NativeMethods.alpm_pkg_get_isize(BackingStruct).Value;

  // Collection properties returning borrowed views stamped with this package's lifetime
  public AlpmStringList Licenses => new(NativeMethods.alpm_pkg_get_licenses(BackingStruct), Lifetime?.Domain);
  public AlpmStringList Groups => new(NativeMethods.alpm_pkg_get_groups(BackingStruct), Lifetime?.Domain);
  public AlpmList<Dependency> Depends => Dependency.ListFactory(NativeMethods.alpm_pkg_get_depends(BackingStruct), Lifetime?.Domain);
  public AlpmList<Dependency> Conflicts => Dependency.ListFactory(NativeMethods.alpm_pkg_get_conflicts(BackingStruct), Lifetime?.Domain);
  public AlpmList<Dependency> Provides => Dependency.ListFactory(NativeMethods.alpm_pkg_get_provides(BackingStruct), Lifetime?.Domain);
  public FileList Files => new(NativeMethods.alpm_pkg_get_files(BackingStruct), Lifetime?.Domain);
  public AlpmList<BackupEntry> BackupFiles => BackupEntry.ListFactory(NativeMethods.alpm_pkg_get_backup(BackingStruct), Lifetime?.Domain);

  // Eager detached copy
  public PackageSnapshot ToSnapshot(bool includeFiles = false) => new(this, includeFiles);
}
```

---

### 1.2 Borrowed View: `PackageView.cs`
Non-owning, non-disposable reference to memory owned by a database, transaction, or group.

```csharp
namespace Orson.Alpm;

public sealed unsafe partial class PackageView : Package
{
  internal PackageView(_alpm_pkg_t* backingStruct, ChildLifetime? lifetime) : base(backingStruct)
  {
    Lifetime = lifetime?.Capture();
  }

  // Factory function pointer target for AlpmList<PackageView>
  internal static PackageView Factory(void* ptr, Lifetime? lifetime)
  {
    if (lifetime is RootLifetime)
    {
      throw new InvalidOperationException(
        "PackageView cannot be anchored directly to a root lifetime domain; a child domain is required.");
    }
    return new((_alpm_pkg_t*)ptr, (ChildLifetime?)lifetime);
  }

  public static PackageView? FindSatisfier(IEnumerable<PackageView> packages, string dependency)
  {
    ArgumentNullException.ThrowIfNull(packages);
    var pkgList = packages as IReadOnlyCollection<PackageView> ?? packages.ToList();
    Span<byte> scratch = stackalloc byte[64];
    using var depBuf = new Utf8Buffer(dependency, scratch);
    _alpm_list_t* nativeList = null;
    try
    {
      nativeList = NativeListMarshal.BuildPointerList(pkgList);
      var matchedPtr = NativeMethods.alpm_find_satisfier(nativeList, depBuf.Ptr);
      if (matchedPtr == null) return null;

      // Returns the original PackageView instance to preserve its exact lifetime domain
      foreach (var pkg in pkgList)
      {
        if (pkg.BackingStruct == matchedPtr) return pkg;
      }

      throw new InvalidOperationException("libalpm returned package not in input collection.");
    }
    finally
    {
      NativeListMarshal.Free(nativeList);
    }
  }

  public void SetReason(PackageReason reason)
  {
    ThrowIfDisposed();
    var err = NativeMethods.alpm_pkg_set_reason(BackingStruct, (_alpm_pkgreason_t)reason);
    if (err != 0) throw AlpmFailure.Of(NativeMethods.alpm_errno(LibraryHandle), "set package install reason").ToException();
    GC.KeepAlive(this);
  }

  public PackageReason InstallReason
  {
    get => Reason;
    set => SetReason(value);
  }
}
```

---

### 1.3 Owning Archive Package: `LoadedPackage.cs`
Represents an archive loaded from disk via `alpm_pkg_load`. It owns the native pointer and tracks it in `LoadedPackageRegistry`.
Can be deterministically freed via `Dispose()`, or transferred to a `Transaction` via `Disown()`.

```csharp
namespace Orson.Alpm;

public sealed unsafe class LoadedPackage : Package, IDisposable
{
  private readonly LoadedPackageRegistry _registry;
  private readonly ChildLifetime _lifetime;
  private nint _rawPackage;

  internal LoadedPackage(LoadedPackageRegistry registry, _alpm_pkg_t* backingStruct, ChildLifetime lifetime)
    : base(backingStruct)
  {
    _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    _rawPackage = (nint)backingStruct;
    _lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
    Lifetime = _lifetime.Capture();
  }

  internal bool OwnsPackage => !Disposed && _rawPackage != 0;

  internal void ThrowIfNotOwned()
  {
    ObjectDisposedException.ThrowIf(!OwnsPackage, this);
  }

  // Atomically relinquishes ownership to a transaction.
  // The transaction's alpm_trans_release will free the native memory.
  internal void Disown()
  {
    ThrowIfNotOwned();
    var pkg = (_alpm_pkg_t*)Interlocked.Exchange(ref _rawPackage, 0);
    if (pkg == null) return;
    Disposed = true;
    _registry.Unregister(pkg, freeNative: false);
    _lifetime.Invalidate("the hand-over to a transaction");
  }

  public void Dispose()
  {
    if (Disposed) return;
    Disposed = true;
    var pkg = (_alpm_pkg_t*)Interlocked.Exchange(ref _rawPackage, 0);
    if (pkg == null) return;
    _lifetime.Invalidate("LoadedPackage.Dispose()");
    _registry.Unregister(pkg, freeNative: true); // calls alpm_pkg_free
  }
}
```

---

### 1.4 Detached Snapshot: `PackageSnapshot.cs`
Managed-only representation with zero unmanaged pointers. Survives session disposal and cache drops.

```csharp
namespace Orson.Alpm;

public sealed class PackageSnapshot
{
  public PackageSnapshot(Package package, bool includeFiles = false)
  {
    ArgumentNullException.ThrowIfNull(package);

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
    Arch = package.Architecture;
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
    BackupFiles = [.. package.BackupFiles];
    CheckDepends = [.. package.CheckDepends];
    MakeDepends = [.. package.MakeDepends];
    DownloadSize = package.DownloadSize;
    HasScriptlet = package.HasScriptlet;
    Base64Signature = package.Base64Signature;

    if (includeFiles) Files = [.. package.Files];
  }

  public string Name { get; }
  public PackageVersion Version { get; }
  public string? Filename { get; }
  public string? Base { get; }
  public string? Description { get; }
  public string? Url { get; }
  public DateTimeOffset BuildDate { get; }
  public DateTimeOffset? InstallDate { get; }
  public string? Packager { get; }
  public string? Md5Sum { get; }
  public string? Sha256Sum { get; }
  public string? Arch { get; }
  public long Size { get; }
  public long InstalledSize { get; }
  public long DownloadSize { get; }
  public PackageOrigin Origin { get; }
  public PackageReason Reason { get; }
  public PackageValidation Validation { get; }
  public bool HasScriptlet { get; }
  public string? Base64Signature { get; }

  public IReadOnlyList<string> Licenses { get; }
  public IReadOnlyList<string> Groups { get; }
  public IReadOnlyList<Dependency> Depends { get; }
  public IReadOnlyList<Dependency> OptionalDepends { get; }
  public IReadOnlyList<Dependency> Conflicts { get; }
  public IReadOnlyList<Dependency> Provides { get; }
  public IReadOnlyList<Dependency> Replaces { get; }
  public IReadOnlyList<BackupEntry> BackupFiles { get; }
  public IReadOnlyList<Dependency> CheckDepends { get; }
  public IReadOnlyList<Dependency> MakeDepends { get; }
  public IReadOnlyList<PackageFile>? Files { get; }
}
```

---

## 2. Database Structure: `Database.cs`

Shows how Local vs. Sync databases are structured, internal lifetime tracking, facet domains, and cache operations.

```csharp
namespace Orson.Alpm;

public sealed unsafe partial class Database
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

  private _alpm_handle_t* RawHandle => NativeMethods.alpm_db_get_handle(ValidatedPtr);

  // Lifetime domain guarding this database and all views issued from it
  internal readonly ChildLifetime Lifetime;

  // Local vs Sync distinction
  private readonly bool _isLocal;
  private readonly Action? _onUnregistered;

  private LifetimeStamp _stamp;

  internal Database(_alpm_db_t* backingStruct, ChildLifetime lifetime, Action? onUnregistered = null, bool isLocal = false)
  {
    ValidatedPtr = backingStruct;
    Lifetime = lifetime;
    _isLocal = isLocal;
    _onUnregistered = onUnregistered;
    _stamp = lifetime.Capture();
  }

  // Factory used when enumerating AlpmSession.GetSyncDatabases()
  internal static Database Factory(void* ptr, Lifetime? lifetime)
  {
    ArgumentNullException.ThrowIfNull(lifetime);
    if (lifetime is not RootLifetime root)
    {
      throw new InvalidOperationException("Sync databases must be enumerated against a root session lifetime domain.");
    }

    var name = NativeString.FromNative((nint)NativeMethods.alpm_db_get_name((_alpm_db_t*)ptr)) ?? "(unknown)";
    return new Database((_alpm_db_t*)ptr, root.GetLifetimeTokenForHandle(ptr, $"the sync database {name}"), () =>
    {
      root.ForgetHandle(ptr);
      root.Invalidate("Database.Unregister()");
    }, isLocal: false);
  }

  // Resolves the ChildLifetime from the caller's existing Database instances
  internal static ChildLifetime ResolveLifetime(IReadOnlyList<Database> databases, _alpm_db_t* dbPtr)
  {
    foreach (var database in databases)
    {
      if (database.ValidatedPtr == dbPtr) return database.Lifetime;
    }

    throw new InvalidOperationException("libalpm returned package owned by database not in parameter list.");
  }

  public string Name
  {
    get
    {
      ThrowIfInvalidated();
      return field ??= NativeString.FromNative((nint)NativeMethods.alpm_db_get_name(ValidatedPtr))!;
    }
  }

  // Package Lookup: directly constructs a PackageView
  public PackageView? GetPackage(string name)
  {
    Span<byte> scratch = stackalloc byte[64];
    using var nameBuf = new Utf8Buffer(name, scratch);
    var pkg = NativeMethods.alpm_db_get_pkg(ValidatedPtr, nameBuf.Ptr);
    if ((nint)pkg == IntPtr.Zero) return null;
    return new PackageView(pkg, Lifetime);
  }

  // Load Package Cache: borrows pkgcache via AlpmList<PackageView> using &PackageView.Factory
  public AlpmList<PackageView> LoadPackageCache()
  {
    var handlePtr = RawHandle;
    var pkgCache = NativeMethods.alpm_db_get_pkgcache(ValidatedPtr);
    // Error handling omitted for brevity
    return AlpmList<PackageView>.Borrow(pkgCache, &PackageView.Factory, Lifetime);
  }

  // Sub-resource Facets: mutating servers invalidates only the server list views
  public AlpmStringList Servers
  {
    get
    {
      var servers = NativeMethods.alpm_db_get_servers(ValidatedPtr);
      return new AlpmStringList(servers, Lifetime.Facet(LifetimeFacet.Servers, "the server list"));
    }
  }

  public void AddServer(string url)
  {
    // Native call omitted for brevity
    Lifetime.InvalidateFacet(LifetimeFacet.Servers, "Database.AddServer");
    GC.KeepAlive(this);
  }

  // Database Update: drops all package views while keeping the Database wrapper valid
  internal void InvalidateViews(string reason)
  {
    Lifetime.Invalidate(reason);
    _stamp = Lifetime.Capture(); // re-stamps wrapper with fresh generation
  }

  public void Unregister()
  {
    if (_isLocal)
    {
      throw new InvalidOperationException("The local database cannot be unregistered.");
    }

    var err = NativeMethods.alpm_db_unregister(ValidatedPtr);
    if (err != 0) throw AlpmFailure.Of(RawHandle, "unregister database").ToException();

    Lifetime.Invalidate("Database.Unregister()");
    _onUnregistered?.Invoke();
  }

  private void ThrowIfInvalidated() => _stamp.ThrowIfStale();
}
```

---

## 3. List Infrastructure: `AlpmList.cs`

Shows how `AlpmBorrowedList<T>` and `AlpmOwnedList<T>` instantiate items `T` using unmanaged function pointers (`delegate*<void*, Lifetime?, T>`).

```csharp
namespace Orson.Alpm;

public abstract class AlpmList<T> : IReadOnlyCollection<T>
{
  private protected readonly unsafe _alpm_list_t* _native;

  // Unmanaged function pointer factory used to construct T from raw void* node data
  internal readonly unsafe delegate*<void*, Lifetime?, T> Factory;

  // Stamped against the originating domain (e.g. Database.Lifetime)
  internal readonly LifetimeStamp? Lifetime;

  private protected unsafe AlpmList(_alpm_list_t* list, delegate*<void*, Lifetime?, T> factory, Lifetime? lifetime)
  {
    _native = list;
    Factory = factory;
    Lifetime = lifetime?.Capture();
  }

  internal unsafe _alpm_list_t* ValidatedNative()
  {
    Lifetime?.ThrowIfStale();
    return _native;
  }

  public unsafe int Count => (int)NativeMethods.alpm_list_count(ValidatedNative());

  // Factory helper for borrowed views
  internal static unsafe AlpmList<T> Borrow(
    _alpm_list_t* list,
    delegate*<void*, Lifetime?, T> factory,
    Lifetime? lifetime)
    => new AlpmBorrowedList<T>(list, factory, lifetime);

  public unsafe struct Enumerator : IEnumerator<T>
  {
    private readonly AlpmList<T> _list;
    private _alpm_list_t* _current;
    private bool _started;
    private bool _disposed;

    internal Enumerator(AlpmList<T> list)
    {
      _list = list;
      _current = null;
      _started = false;
      _disposed = false;
    }

    private _alpm_list_t* Advance()
    {
      _list.Lifetime?.ThrowIfStale();
      return _current is null ? null : _current->next;
    }

    private bool TryGetCurrentData(out void* data)
    {
      _list.Lifetime?.ThrowIfStale();
      if (_current is null)
      {
        data = null;
        return false;
      }
      data = _current->data;
      return true;
    }

    // Instantiates T on-demand during enumeration via Factory function pointer
    public T Current
    {
      get
      {
        if (_disposed) throw new ObjectDisposedException(GetType().FullName);
        if (!_started || !TryGetCurrentData(out var data)) throw new InvalidOperationException();

        // Passes raw void* pointer and originating Lifetime domain to Factory
        var item = _list.Factory(data, _list.Lifetime?.Domain);
        GC.KeepAlive(_list);
        return item;
      }
    }

    public bool MoveNext()
    {
      if (_disposed) throw new ObjectDisposedException(GetType().FullName);
      _current = _started ? Advance() : _list.ValidatedNative();
      _started = true;
      return _current != null;
    }

    public void Dispose() => _disposed = true;
    object IEnumerator.Current => Current!;
    public void Reset() => throw new NotSupportedException();
  }

  public Enumerator GetEnumerator()
  {
    Lifetime?.ThrowIfStale();
    return new Enumerator(this);
  }

  IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();
  IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

  // Materializes view into managed array in single pass
  public unsafe T[] ToArray()
  {
    Lifetime?.ThrowIfStale();
    if (_native == null) return [];

    var count = (int)NativeMethods.alpm_list_count(_native);
    var result = new T[count];
    var i = 0;
    for (var node = _native; node != null; node = node->next)
    {
      result[i++] = Factory(node->data, Lifetime?.Domain);
    }
    return result;
  }
}

// -----------------------------------------------------------------------------
// Concrete Subclasses
// -----------------------------------------------------------------------------

internal sealed class AlpmBorrowedList<T> : AlpmList<T>
{
  internal unsafe AlpmBorrowedList(_alpm_list_t* list, delegate*<void*, Lifetime?, T> factory, Lifetime? lifetime)
    : base(list, factory, lifetime)
  {
  }
}

internal sealed class AlpmOwnedList<T> : AlpmList<T>, IDisposable
{
  private readonly unsafe delegate* unmanaged[Cdecl]<void*, void> _innerFree;
  private bool _disposed;

  internal unsafe AlpmOwnedList(
    _alpm_list_t* list,
    delegate*<void*, Lifetime?, T> factory,
    delegate* unmanaged[Cdecl]<void*, void> innerFree,
    Lifetime? lifetime) : base(list, factory, lifetime)
  {
    _innerFree = innerFree;
  }

  // Materializes items and immediately frees native list + element payloads
  internal static unsafe IReadOnlyList<T> Take(
    _alpm_list_t* list,
    delegate*<void*, Lifetime?, T> factory,
    delegate* unmanaged[Cdecl]<void*, void> innerFree,
    Lifetime? lifetime = null)
  {
    using var owned = new AlpmOwnedList<T>(list, factory, innerFree, lifetime);
    return owned.ToArray();
  }

  public unsafe void Dispose()
  {
    if (_disposed) return;
    NativeListMarshal.Free(_native, _innerFree); // frees node memory & executes payload destructor
    _disposed = true;
  }
}
```

---

## 4. Key Wiring Takeaways for the Auditor

1. **Instantiation of `PackageView` in Lists:**
   * Lists do not use reflection or `new T()`.
   * They use an unmanaged function pointer: `delegate*<void*, Lifetime?, T> Factory`.
   * For package views, `&PackageView.Factory` (`(void* ptr, Lifetime? lifetime) => new PackageView((_alpm_pkg_t*)ptr, (ChildLifetime?)lifetime)`) is passed.
   * Every step of `Enumerator.Current` or `ToArray()` calls this pointer, allocating a new `PackageView` object and capturing a 32-byte `LifetimeStamp`.

2. **Heap Churn Impact:**
   * Iterating a 10,000-package database (`db.LoadPackageCache()`) via `foreach` currently allocates 10,000 `PackageView` class instances on the managed heap.
   * If `PackageView` were converted to a `readonly struct`, `Factory` would need to return the struct value directly without box allocation, or the list enumerator would need a struct-specialized path.

3. **`Database` State:**
   * `Database` is a reference type wrapping `_alpm_db_t* ValidatedPtr`.
   * It holds a `ChildLifetime Lifetime` field and a `LifetimeStamp _stamp`.
   * Local vs. Sync is differentiated by the `_isLocal` boolean flag.
   * Sync databases register their `ChildLifetime` in `RootLifetime._handles` (deduplicated by `_alpm_db_t*`). Local database lifetime is held exclusively by `AlpmSession._localDatabaseLifetime`.
   * Mutating servers uses sub-facets (`Lifetime.Facet(LifetimeFacet.Servers)`), invalidating server list views without bumping package views.
