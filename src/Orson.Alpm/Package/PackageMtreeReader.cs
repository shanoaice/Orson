using System.Diagnostics.CodeAnalysis;
using Orson.Alpm.Bindings;

namespace Orson.Alpm;

/// <summary>An internal forward-only reader over an installed package's mtree entries ( <c>.MTREE</c>).</summary>
/// <remarks>
/// <para>
/// The reader is single-use and is owned by <see cref="PackageView.GetMtreeEntries" />, which materializes the
/// entries into a managed list and disposes the reader before returning. It is not part of the public API.
/// </para>
/// <para>
/// Dispose the reader before the owning <see cref="AlpmSession" /> ends. When the session has already ended,
/// <see cref="Dispose" /> throws <see cref="AlpmLifetimeException" /> before it can close the native archive reader, and
/// that reader then leaks. The reader has no finalizer, so a forgotten <see cref="Dispose" /> leaks the native archive
/// reader too.
/// </para>
/// </remarks>
internal sealed unsafe class PackageMtreeReader : IDisposable
{
  private readonly PackageView _package;
  private readonly archive* _archive;
  private MtreeEntry? _current;
  private bool _disposed;

  internal PackageMtreeReader(PackageView package, archive* archive)
  {
    _package = package;
    _archive = archive;
  }

  /// <summary>Gets the current <see cref="MtreeEntry" /> in the mtree stream.</summary>
  internal MtreeEntry Current => _current ?? throw new InvalidOperationException("Enumeration has not started or has already completed.");

  /// <summary>Advances the reader to the next entry in the mtree stream.</summary>
  internal bool MoveNext()
  {
    if (_disposed || _archive == null) return false;

    var handlePtr = _package.LibraryHandle;
    archive_entry* entry = null;
    var ret = NativeMethods.alpm_pkg_mtree_next(_package.BackingStruct, _archive, &entry);
    if (ret > 0)
    {
      _current = null;
      return false;
    }

    if (ret < 0 || entry == null)
    {
      var rawErrno = (int)NativeMethods.alpm_errno(handlePtr);
      GC.KeepAlive(_package.LifetimeDomain);
      throw AlpmFailure.Of(rawErrno, "read package mtree").ToException();
    }

    _current = MtreeEntry.FromNative(entry);
    GC.KeepAlive(_package.LifetimeDomain);
    return true;
  }

  /// <summary>Closes the native archive reader and releases unmanaged resources.</summary>
  public void Dispose()
  {
    if (_disposed) return;
    _disposed = true;
    _current = null;
    if (_archive != null)
    {
      _ = NativeMethods.alpm_pkg_mtree_close(_package.BackingStruct, _archive);
    }
    GC.KeepAlive(_package.LifetimeDomain);
  }
}
