using System.Diagnostics.CodeAnalysis;
using Orson.Alpm.Bindings;
using Orson.Alpm.Interop;

namespace Orson.Alpm;

public sealed unsafe partial class SyncDatabase
{
  /// <summary>The failure-returning seam for <see cref="Update" />.</summary>
  internal bool TryUpdate(bool force, out bool updated, [NotNullWhen(false)] out AlpmFailure? failure)
  {
    var handlePtr = RawHandle;

    InvalidateViews("Database.Update");

    Span<_alpm_list_t> scratch = stackalloc _alpm_list_t[1];
    using var builder = new TransientAlpmListBuilder(scratch);
    var list = builder.ConstructNative([this]);
    var ret = NativeMethods.alpm_db_update(handlePtr, list, force ? 1 : 0);
    if (ret < 0)
    {
      var rawErrno = (int)NativeMethods.alpm_errno(handlePtr);
      GC.KeepAlive(this);
      updated = false;
      failure = AlpmFailure.Of(rawErrno, "update database");
      return false;
    }

    GC.KeepAlive(this);
    updated = ret == 0;
    failure = null;
    return true;
  }
}
