using System.Diagnostics.CodeAnalysis;
using Pacpar.Alpm.Bindings;

namespace Pacpar.Alpm;

public sealed unsafe partial class Database
{
  /// <summary>The failure-returning seam for <see cref="Search" />.</summary>
  internal bool TrySearch(
    IEnumerable<string> needles,
    [NotNullWhen(true)] out IReadOnlyList<PackageView>? results,
    [NotNullWhen(false)] out AlpmFailure? failure)
  {
    ArgumentNullException.ThrowIfNull(needles);
    var dbPtr = ValidatedPtr;
    var handlePtr = RawHandle;

    var needleList = AlpmNativeList.BuildStringList(needles, out var buffers);
    _alpm_list_t* retList = null;
    try
    {
      var ret = NativeMethods.alpm_db_search(dbPtr, needleList, &retList);
      if (ret < 0)
      {
        AlpmNativeList.Free(retList);
        var rawErrno = (int)NativeMethods.alpm_errno(handlePtr);
        GC.KeepAlive(this);
        results = null;
        failure = NativeCall.Failure(rawErrno, "search database");
        return false;
      }

      GC.KeepAlive(this);
      results = retList != null
        ? AlpmOwnedList<PackageView>.Take(retList, &PackageView.Factory, null, Lifetime)
        : Array.Empty<PackageView>();
      failure = null;
      return true;
    }
    finally
    {
      AlpmNativeList.FreeStringListBuffers(buffers, needleList);
    }
  }

  /// <summary>The failure-returning seam for <see cref="LoadPackageCache" />.</summary>
  /// <param name="cache">The borrowed view, or <c>null</c> when the call failed.</param>
  /// <param name="failure">The failure, or <c>null</c> when the call succeeded.</param>
  /// <returns><c>true</c> when the cache was read.</returns>
  /// <remarks>
  /// The first of the seam's two shapes: an entry point libalpm resets <c>pm_errno</c> in, so the handle's errno
  /// after the call is this call's own and no return value says failure (ADR 0007). An ok errno means "empty cache", not "no
  /// failure to report".
  /// </remarks>
  internal bool TryLoadPackageCache([NotNullWhen(true)] out AlpmList<PackageView>? cache,
    [NotNullWhen(false)] out AlpmFailure? failure)
  {
    var handlePtr = RawHandle;
    var pkgCache = NativeMethods.alpm_db_get_pkgcache(ValidatedPtr);

    if (!LastCallFailed(handlePtr, "load package cache", out failure))
    {
      cache = null;
      return false;
    }

    cache = AlpmList<PackageView>.Borrow(pkgCache, &PackageView.Factory, Lifetime);
    return true;
  }

  /// <summary>
  /// The failure-returning seam for <see cref="LoadGroupCache" />. See <see cref="TryLoadPackageCache" /> for the
  /// shape and for why this entry point may read the errno.
  /// </summary>
  internal bool TryLoadGroupCache([NotNullWhen(true)] out AlpmList<Group>? cache,
    [NotNullWhen(false)] out AlpmFailure? failure)
  {
    var handlePtr = RawHandle;
    var groupCache = NativeMethods.alpm_db_get_groupcache(ValidatedPtr);

    if (!LastCallFailed(handlePtr, "load group cache", out failure))
    {
      cache = null;
      return false;
    }

    cache = AlpmList<Group>.Borrow(groupCache, &Group.Factory, Lifetime);
    return true;
  }

  /// <summary>Reports the handle's errno as a failure when the call that just returned left one there.</summary>
  /// <param name="handlePtr">The handle, captured before the call being checked.</param>
  /// <param name="operation">What failed, e.g. <c>"load package cache"</c>.</param>
  /// <param name="failure">The failure, or <c>null</c> when the call left no errno.</param>
  /// <returns><c>true</c> when the handle reports no error.</returns>
  /// <remarks>
  /// This is only valid after an entry point that resets <c>pm_errno</c> at entry, because only then is a non-ok value
  /// guaranteed to come from that call. <c>alpm_db_get_pkgcache</c> and <c>alpm_db_get_groupcache</c> both do; the two
  /// server getters deliberately do not, which is why <see cref="Servers" /> and <see cref="CacheServers" /> cannot use this
  /// and must treat a null list as "none configured" whatever the handle's errno happens to say. Anything that reports
  /// failure through its return value goes through <see cref="NativeCall" /> instead.
  /// <para>
  /// An ok errno means the call succeeded, and here that includes "the cache is legitimately empty". It is not the
  /// same reading as <see cref="NativeCall.Failure(int, string)" /> gives the same value: there, the return value already
  /// said the call failed, so a missing errno is a failure libalpm cannot describe and is thrown.
  /// </para>
  /// </remarks>
  private bool LastCallFailed(_alpm_handle_t* handlePtr, string operation,
    [NotNullWhen(false)] out AlpmFailure? failure)
  {
    var errno = (int)NativeMethods.alpm_errno(handlePtr);
    GC.KeepAlive(this);

    if (errno == (int)_alpm_errno_t.ALPM_ERR_OK)
    {
      failure = null;
      return true;
    }

    failure = AlpmFailure.Of(errno, operation);
    return false;
  }


  /// <summary>The failure-returning seam for <see cref="Update" />.</summary>
  internal bool TryUpdate(bool force, out bool updated, [NotNullWhen(false)] out AlpmFailure? failure)
  {
    var dbPtr = ValidatedPtr;
    var handlePtr = RawHandle;

    InvalidateViews("Database.Update");

    _alpm_list_t* list = null;
    try
    {
      list = AlpmNativeList.BuildPointerList([(nint)dbPtr]);
      var ret = NativeMethods.alpm_db_update(handlePtr, list, force ? 1 : 0);
      if (ret < 0)
      {
        var rawErrno = (int)NativeMethods.alpm_errno(handlePtr);
        GC.KeepAlive(this);
        updated = false;
        failure = NativeCall.Failure(rawErrno, "update database");
        return false;
      }

      GC.KeepAlive(this);
      updated = ret == 0;
      failure = null;
      return true;
    }
    finally
    {
      AlpmNativeList.Free(list);
    }
  }


  /// <summary>The failure-returning seam for <see cref="Validate" />.</summary>
  internal bool TryValidate([NotNullWhen(false)] out AlpmFailure? failure)
  {
    var handlePtr = RawHandle;
    if (IsValid)
    {
      failure = null;
      return true;
    }

    var rawErrno = (int)NativeMethods.alpm_errno(handlePtr);
    GC.KeepAlive(this);
    failure = NativeCall.Failure(rawErrno, "validate database");
    return false;
  }
}
