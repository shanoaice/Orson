using Orson.Alpm.Bindings;

namespace Orson.Alpm;

/// <summary>A registered sync package database repository.</summary>
/// <remarks>
/// A borrowed handle representing a remote synchronization database repository. In addition to common database
/// queries, it supports remote server administration, synchronization updates, and unregistering from the session.
/// </remarks>
public sealed unsafe partial class SyncDatabase : Database
{
  private readonly Action? _onUnregistered;

  internal SyncDatabase(_alpm_db_t* backingStruct, ChildLifetime lifetime, Action? onUnregistered = null)
    : base(backingStruct, lifetime)
  {
    _onUnregistered = onUnregistered;
  }

  /// <summary>
  /// Element factory for <see cref="AlpmSession.GetSyncDatabases" />: resolves the per-database token from the
  /// handle registry keyed by the native pointer, so two wrappers for the same database - and two enumerations of the same
  /// list - share one token and one invalidation.
  /// </summary>
  /// <remarks>
  /// The name is read only when the pointer is not registered yet: on a fresh list the database is alive and the
  /// read is safe. A list obtained <i>before</i> a registry mutation (<see cref="AlpmSession.RegisterSyncDatabase" />,
  /// <see cref="Unregister" />, <see cref="AlpmSession.UnregisterAllSyncDatabases" />) can hold nodes those calls freed;
  /// re-fetch the list after mutating the registry instead of re-enumerating a stale one.
  /// </remarks>
  internal static SyncDatabase Factory(void* ptr, Lifetime? lifetime)
  {
    ArgumentNullException.ThrowIfNull(lifetime);
    RootLifetime root;
    ChildLifetime? syncListLifetime = null;
    if (lifetime is RootLifetime r)
    {
      root = r;
    }
    else if (lifetime is ChildLifetime c)
    {
      root = c.Root;
      syncListLifetime = c;
    }
    else
    {
      throw new InvalidOperationException("Sync databases must be enumerated against a root or child lifetime domain.");
    }

    var name = NativeString.FromNative((nint)NativeMethods.alpm_db_get_name((_alpm_db_t*)ptr)) ?? "(unknown)";
    var token = root.GetLifetimeTokenForHandle(ptr, $"the sync database {name}");
    return new SyncDatabase((_alpm_db_t*)ptr, token, () =>
    {
      token.Invalidate("Database.Unregister()");
      root.ForgetHandle(ptr);
      syncListLifetime?.Invalidate("Database.Unregister()");
    });
  }

  /// <summary>The tree name of this sync repository.</summary>
  public string TreeName => Name;

  /// <summary>The signature verification level configured for this database.</summary>
  public SigLevel SigLevel
  {
    get
    {
      var sig = (SigLevel)NativeMethods.alpm_db_get_siglevel(ValidatedPtr);
      GC.KeepAlive(this);
      return sig;
    }
  }

  /// <summary>The servers configured for this database; empty when none are set.</summary>
  /// <remarks>
  /// The list is borrowed from the database and is never freed by the view. Adding, removing, or replacing a server
  /// retires the view, because libalpm frees or re-links that list in place; the database's package views are unaffected.
  /// The next read binds a fresh view to the rebuilt list.
  /// </remarks>
  public AlpmStringList Servers
  {
    get
    {
      var servers = NativeMethods.alpm_db_get_servers(ValidatedPtr);
      return new AlpmStringList(servers, Lifetime.Facet(LifetimeFacet.Servers, "the server list"));
    }
  }

  /// <summary>The cache servers configured for this database; empty when none are set.</summary>
  /// <remarks>
  /// The list is borrowed from the database and is never freed by the view. The cache-server operations retire the
  /// view for the same reason the server operations retire <see cref="Servers" />, and the server list is unaffected by
  /// them.
  /// </remarks>
  public AlpmStringList CacheServers
  {
    get
    {
      var servers = NativeMethods.alpm_db_get_cache_servers(ValidatedPtr);
      return new AlpmStringList(servers,
        Lifetime.Facet(LifetimeFacet.CacheServers, "the cache-server list"));
    }
  }

  /// <summary>Adds a download server to this database.</summary>
  public void AddServer(string url)
  {
    ArgumentNullException.ThrowIfNull(url);
    var dbPtr = ValidatedPtr;
    var handlePtr = RawHandle;
    Span<byte> scratch = stackalloc byte[256];
    using var urlBuf = new Utf8Buffer(url, scratch);

    var err = NativeMethods.alpm_db_add_server(dbPtr, urlBuf.Ptr);
    if (err != 0)
    {
      var rawErrno = (int)NativeMethods.alpm_errno(handlePtr);
      GC.KeepAlive(this);
      throw AlpmFailure.Of(rawErrno, "add server").ToException();
    }

    Lifetime.InvalidateFacet(LifetimeFacet.Servers, "Database.AddServer");
    GC.KeepAlive(this);
  }

  /// <summary>Removes a download server from this database.</summary>
  /// <returns><c>true</c> if the server was removed; <c>false</c> if it was not found.</returns>
  public bool RemoveServer(string url)
  {
    ArgumentNullException.ThrowIfNull(url);
    var dbPtr = ValidatedPtr;
    var handlePtr = RawHandle;
    Span<byte> scratch = stackalloc byte[256];
    using var urlBuf = new Utf8Buffer(url, scratch);

    var ret = NativeMethods.alpm_db_remove_server(dbPtr, urlBuf.Ptr);
    if (ret < 0)
    {
      var rawErrno = (int)NativeMethods.alpm_errno(handlePtr);
      GC.KeepAlive(this);
      throw AlpmFailure.Of(rawErrno, "remove server").ToException();
    }

    GC.KeepAlive(this);
    var removed = ret == 0;
    if (removed)
    {
      Lifetime.InvalidateFacet(LifetimeFacet.Servers, "Database.RemoveServer");
    }

    return removed;
  }

  /// <summary>Sets the complete list of download servers for this database.</summary>
  public void SetServers(IEnumerable<string> servers)
  {
    ArgumentNullException.ThrowIfNull(servers);
    var dbPtr = ValidatedPtr;
    var handlePtr = RawHandle;
    var nativeList = NativeListMarshal.BuildStringList(servers, out var buffers);
    try
    {
      var err = NativeMethods.alpm_db_set_servers(dbPtr, nativeList);
      if (err != 0)
      {
        var rawErrno = (int)NativeMethods.alpm_errno(handlePtr);
        GC.KeepAlive(this);
        throw AlpmFailure.Of(rawErrno, "set servers").ToException();
      }

      Lifetime.InvalidateFacet(LifetimeFacet.Servers, "Database.SetServers");
      GC.KeepAlive(this);
    }
    finally
    {
      NativeListMarshal.FreeStringListBuffers(buffers, nativeList);
    }
  }

  /// <summary>Adds a download cache server to this database.</summary>
  public void AddCacheServer(string url)
  {
    ArgumentNullException.ThrowIfNull(url);
    var dbPtr = ValidatedPtr;
    var handlePtr = RawHandle;
    Span<byte> scratch = stackalloc byte[256];
    using var urlBuf = new Utf8Buffer(url, scratch);

    var err = NativeMethods.alpm_db_add_cache_server(dbPtr, urlBuf.Ptr);
    if (err != 0)
    {
      var rawErrno = (int)NativeMethods.alpm_errno(handlePtr);
      GC.KeepAlive(this);
      throw AlpmFailure.Of(rawErrno, "add cache server").ToException();
    }

    Lifetime.InvalidateFacet(LifetimeFacet.CacheServers, "Database.AddCacheServer");
    GC.KeepAlive(this);
  }

  /// <summary>Removes a download cache server from this database.</summary>
  /// <returns><c>true</c> if the cache server was removed; <c>false</c> if it was not found.</returns>
  public bool RemoveCacheServer(string url)
  {
    ArgumentNullException.ThrowIfNull(url);
    var dbPtr = ValidatedPtr;
    var handlePtr = RawHandle;
    Span<byte> scratch = stackalloc byte[256];
    using var urlBuf = new Utf8Buffer(url, scratch);

    var ret = NativeMethods.alpm_db_remove_cache_server(dbPtr, urlBuf.Ptr);
    if (ret < 0)
    {
      var rawErrno = (int)NativeMethods.alpm_errno(handlePtr);
      GC.KeepAlive(this);
      throw AlpmFailure.Of(rawErrno, "remove cache server").ToException();
    }

    GC.KeepAlive(this);
    var removed = ret == 0;
    if (removed)
    {
      Lifetime.InvalidateFacet(LifetimeFacet.CacheServers, "Database.RemoveCacheServer");
    }

    return removed;
  }

  /// <summary>Sets the complete list of download cache servers for this database.</summary>
  public void SetCacheServers(IEnumerable<string> servers)
  {
    ArgumentNullException.ThrowIfNull(servers);
    var dbPtr = ValidatedPtr;
    var handlePtr = RawHandle;
    var nativeList = NativeListMarshal.BuildStringList(servers, out var buffers);
    try
    {
      var err = NativeMethods.alpm_db_set_cache_servers(dbPtr, nativeList);
      if (err != 0)
      {
        var rawErrno = (int)NativeMethods.alpm_errno(handlePtr);
        GC.KeepAlive(this);
        throw AlpmFailure.Of(rawErrno, "set cache servers").ToException();
      }

      Lifetime.InvalidateFacet(LifetimeFacet.CacheServers, "Database.SetCacheServers");
      GC.KeepAlive(this);
    }
    finally
    {
      NativeListMarshal.FreeStringListBuffers(buffers, nativeList);
    }
  }

  /// <summary>Updates this database from its configured download servers.</summary>
  /// <param name="force">
  /// If <c>true</c>, forces the download even if the local copy is current, as in <c>pacman -Syy</c>
  /// </param>
  /// <returns>
  /// <c>true</c> if at least one file was downloaded, including a forced download of content that did not change;
  /// <c>false</c> if every file was already current.
  /// </returns>
  /// <remarks>
  /// Existing views of this database are invalidated before the download starts, so they are invalid even when the
  /// call fails. A forced update only downloads the files again; it does not promise that their contents changed.
  /// </remarks>
  public bool Update(bool force = false)
  {
    if (!TryUpdate(force, out var updated, out var failure))
    {
      throw failure.ToException();
    }

    return updated;
  }

  /// <summary>Unregisters this database from its handle, releasing it.</summary>
  /// <remarks>
  /// On success every wrapper and view for this database starts throwing <see cref="AlpmLifetimeException" />. A
  /// failed unregister leaves the database usable.
  /// </remarks>
  public void Unregister()
  {
    var dbPtr = ValidatedPtr;
    var handlePtr = NativeMethods.alpm_db_get_handle(dbPtr);
    var err = NativeMethods.alpm_db_unregister(dbPtr);
    if (err != 0)
    {
      var rawErrno = (int)NativeMethods.alpm_errno(handlePtr);
      GC.KeepAlive(this);
      throw AlpmFailure.Of(rawErrno, "unregister database").ToException();
    }

    Lifetime.Invalidate("Database.Unregister()");
    _onUnregistered?.Invoke();
  }
}
