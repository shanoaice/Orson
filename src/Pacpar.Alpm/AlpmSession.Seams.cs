using System.Diagnostics.CodeAnalysis;
using Pacpar.Alpm.Bindings;
using Pacpar.Alpm.Interop;

namespace Pacpar.Alpm;

public sealed partial class AlpmSession
{
  /// <summary>The failure-returning seam for creating an <see cref="AlpmSession" /> session.</summary>
  /// <param name="root">The root directory of the installation (e.g. <c>"/"</c>).</param>
  /// <param name="dbpath">The path to the pacman database directory (e.g. <c>"/var/lib/pacman"</c>).</param>
  /// <param name="session">The newly created session, or <c>null</c> when initialization failed.</param>
  /// <param name="failure">The failure, or <c>null</c> when initialization succeeded.</param>
  /// <returns><c>true</c> if initialization succeeded; otherwise <c>false</c>.</returns>
  internal static bool TryCreate(string root, string dbpath,
    [NotNullWhen(true)] out AlpmSession? session,
    [NotNullWhen(false)] out AlpmFailure? failure)
  {
    if (!TryInitializeHandle(root, dbpath, out var handle, out failure))
    {
      session = null;
      return false;
    }

    session = new AlpmSession(handle);
    return true;
  }

  /// <summary>The failure-returning seam for <see cref="LoadPackage" />.</summary>
  internal unsafe bool TryLoadPackage(string filename, bool full, SigLevel level,
    [NotNullWhen(true)] out LoadedPackage? package,
    [NotNullWhen(false)] out AlpmFailure? failure)
  {
    ThrowIfDisposed();
    ArgumentNullException.ThrowIfNull(filename);

    // Paths are long, hence the 256-byte scratch; alpm_pkg_load only reads the string.
    Span<byte> scratch = stackalloc byte[256];
    using var filenameBuf = new Utf8Buffer(filename, scratch);
    // Pass the address of a stack local where libalpm will write the package handle.
    _alpm_pkg_t* rawPkg = null;
    // The SafeHandle marshaller refcounts the handle for the whole call, so the failure path
    // below cannot leak a refcount and no keep-alive is needed here.
    var err = NativeMethods.alpm_pkg_load(Handle, filenameBuf.Ptr, full ? 1 : 0, (int)level, &rawPkg);
    if (err != 0)
    {
      package = null;
      failure = NativeCall.Failure(Handle, "load package");
      return false;
    }

    _loadedPackages.Register(rawPkg);

    var pkgLifetime = _lifetime.CreateChild("a loaded package");
    package = new LoadedPackage(_loadedPackages, rawPkg, pkgLifetime);
    failure = null;
    return true;
  }

  /// <summary>The failure-returning seam for <see cref="BeginTransaction" />.</summary>
  internal bool TryBeginTransaction(TransactionFlags flags,
    [NotNullWhen(true)] out Transaction? transaction,
    [NotNullWhen(false)] out AlpmFailure? failure)
  {
    ThrowIfDisposed();

    if (CurrentTransaction is { } active)
    {
      if (active.Flags != flags)
      {
        throw new InvalidOperationException(
          $"A transaction with flags {active.Flags} is already active on this handle; dispose it " +
          $"before starting one with flags {flags}.");
      }

      transaction = active;
      failure = null;
      return true;
    }

    if (!Transaction.TryCreate(this, flags, out transaction, out failure))
    {
      return false;
    }

    CurrentTransaction = transaction;
    return true;
  }

  /// <summary>The failure-returning seam for <see cref="RegisterSyncDatabase" />.</summary>
  internal unsafe bool TryRegisterSyncDatabase(string treename, SigLevel level,
    [NotNullWhen(true)] out Database? database,
    [NotNullWhen(false)] out AlpmFailure? failure)
  {
    ThrowIfDisposed();
    ArgumentNullException.ThrowIfNull(treename);

    Span<byte> scratch = stackalloc byte[64];
    using var treeNameBuf = new Utf8Buffer(treename, scratch);
    var dbPtr = NativeMethods.alpm_register_syncdb(Handle, treeNameBuf.Ptr, (int)level);
    if (dbPtr is null)
    {
      database = null;
      failure = NativeCall.Failure(Handle, "register sync database");
      return false;
    }

    var token = _lifetime.GetLifetimeTokenForHandle(dbPtr, $"the sync database {treename}");
    database = new Database(dbPtr, token, () =>
    {
      _lifetime.ForgetHandle(dbPtr);
      _lifetime.Invalidate("Database.Unregister()");
    }, isLocal: false);
    failure = null;
    return true;
  }


  /// <summary>The failure-returning seam for <see cref="UpdateDatabases" />.</summary>
  internal unsafe bool TryUpdateDatabases(
    IEnumerable<Database> databases,
    bool force,
    out bool updated,
    [NotNullWhen(false)] out AlpmFailure? failure)
  {
    ThrowIfDisposed();
    ArgumentNullException.ThrowIfNull(databases);

    var dbList = databases.ToList();
    if (dbList.Count == 0)
    {
      throw new ArgumentException("At least one database must be supplied.", nameof(databases));
    }

    foreach (var db in dbList)
    {
      db.InvalidateViews("Database.Update");
    }

    _alpm_list_t* nativeList = null;
    try
    {
      nativeList = NativeListMarshal.BuildPointerList(dbList);
      var ret = NativeMethods.alpm_db_update(Handle, nativeList, force ? 1 : 0);
      if (ret < 0)
      {
        updated = false;
        failure = NativeCall.Failure(Handle, "update databases");
        return false;
      }

      updated = ret == 0;
      failure = null;
      return true;
    }
    finally
    {
      NativeListMarshal.Free(nativeList);
    }
  }


  /// <summary>The failure-returning seam for <see cref="FetchPackageUrls" />.</summary>
  internal unsafe bool TryFetchPackageUrls(IEnumerable<string> urls,
    [NotNullWhen(true)] out IReadOnlyList<string>? fetchedUrls,
    [NotNullWhen(false)] out AlpmFailure? failure)
  {
    ThrowIfDisposed();
    ArgumentNullException.ThrowIfNull(urls);

    var nativeUrls = NativeListMarshal.BuildStringList(urls, out var buffers);
    _alpm_list_t* fetchedList = null;
    try
    {
      var err = NativeMethods.alpm_fetch_pkgurl(Handle, nativeUrls, &fetchedList);
      if (err != 0)
      {
        // alpm_fetch_pkgurl's failure paths free *fetched with FREELIST and leave it NULL
        // (dload.c), so this branch normally sees null. The guard covers only a native path that
        // returns an error while still populating the out parameter.
        if (fetchedList != null)
        {
          NativeListMarshal.Free(fetchedList, &MemoryManagement.CFreeExtern);
        }

        fetchedUrls = null;
        failure = NativeCall.Failure(Handle, "fetch package URLs");
        return false;
      }

      fetchedUrls = AlpmStringList.TakeOwned(fetchedList, &MemoryManagement.CFreeExtern);
      failure = null;
      return true;
    }
    finally
    {
      NativeListMarshal.FreeStringListBuffers(buffers, nativeUrls);
    }
  }

}
