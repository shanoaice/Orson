using System.Diagnostics.CodeAnalysis;
using Orson.Alpm.Bindings;

namespace Orson.Alpm;

public readonly unsafe partial struct PackageView
{
  /// <summary>The failure-returning seam for <see cref="SetReason" />.</summary>
  internal bool TrySetReason(PackageReason reason, [NotNullWhen(false)] out AlpmFailure? failure)
  {
    var err = NativeMethods.alpm_pkg_set_reason(BackingStruct, (_alpm_pkgreason_t)reason);
    if (err != 0)
    {
      var dbPtr = NativeMethods.alpm_pkg_get_db(BackingStruct);
      var handlePtr = dbPtr != null ? NativeMethods.alpm_db_get_handle(dbPtr) : null;
      var rawErrno = handlePtr != null ? (int)NativeMethods.alpm_errno(handlePtr) : 0;
      GC.KeepAlive(_stamp.Domain);
      failure = AlpmFailure.Of(rawErrno, "set package install reason");
      return false;
    }

    GC.KeepAlive(_stamp.Domain);
    failure = null;
    return true;
  }

  /// <summary>The failure-returning seam for <see cref="CheckMd5Sum" />.</summary>
  internal bool TryCheckMd5Sum(out bool matches, [NotNullWhen(false)] out AlpmFailure? failure)
  {
    var ret = NativeMethods.alpm_pkg_checkmd5sum(BackingStruct);
    if (ret == 0)
    {
      GC.KeepAlive(_stamp.Domain);
      matches = true;
      failure = null;
      return true;
    }

    var rawErrno = (int)NativeMethods.alpm_errno(LibraryHandle);
    GC.KeepAlive(_stamp.Domain);

    if (rawErrno == (int)_alpm_errno_t.ALPM_ERR_PKG_INVALID ||
        rawErrno == (int)_alpm_errno_t.ALPM_ERR_PKG_INVALID_CHECKSUM)
    {
      matches = false;
      failure = null;
      return true;
    }

    matches = false;
    failure = AlpmFailure.Of(rawErrno, "check md5 sum");
    return false;
  }

  /// <summary>The failure-returning seam for <see cref="GetMtreeEntries" />.</summary>
  internal bool TryGetMtree(out PackageMtreeReader? mtree, [NotNullWhen(false)] out AlpmFailure? failure)
  {
    var handlePtr = LibraryHandle;
    var archive = NativeMethods.alpm_pkg_mtree_open(BackingStruct);
    if (archive == null)
    {
      var rawErrno = (int)NativeMethods.alpm_errno(handlePtr);
      GC.KeepAlive(_stamp.Domain);
      if (rawErrno == 0)
      {
        mtree = null;
        failure = null;
        return true;
      }
      mtree = null;
      failure = AlpmFailure.Of(rawErrno, "open package mtree");
      return false;
    }

    GC.KeepAlive(_stamp.Domain);
    mtree = new PackageMtreeReader(this, archive);
    failure = null;
    return true;
  }
}
