using System.Diagnostics.CodeAnalysis;
using Pacpar.Alpm.Bindings;

namespace Pacpar.Alpm;

/// <summary>
/// An internal forward-only reader over an installed package's mtree entries (<c>.MTREE</c>).
/// </summary>
/// <remarks>
/// <para>
/// The reader is single-use and is owned by <see cref="PackageBase.GetMtreeEntries"/>, which
/// materializes the entries into a managed list and disposes the reader before returning. It is not
/// part of the public API.
/// </para>
/// <para>
/// Dispose the reader before the owning <see cref="AlpmSession"/> ends. When the session has
/// already ended, <see cref="Dispose"/> throws <see cref="AlpmLifetimeException"/> before it can
/// close the native archive reader, and that reader then leaks. The reader has no finalizer, so a
/// forgotten <see cref="Dispose"/> leaks the native archive reader too.
/// </para>
/// </remarks>
internal sealed unsafe class PackageMtreeReader : IDisposable
{
  private readonly PackageBase _package;
  private readonly archive* _archive;
  private MtreeEntry? _current;
  private bool _disposed;

  internal PackageMtreeReader(PackageBase package, archive* archive)
  {
    _package = package;
    _archive = archive;
  }

  /// <summary>
  /// Gets the current <see cref="MtreeEntry"/> in the mtree stream.
  /// </summary>
  internal MtreeEntry Current => _current ?? throw new InvalidOperationException("Enumeration has not started or has already completed.");

  /// <summary>
  /// Advances the reader to the next entry in the mtree stream.
  /// </summary>
  /// <returns><c>true</c> if the reader successfully advanced to the next entry; <c>false</c> if the end was reached.</returns>
  /// <exception cref="AlpmNativeFailureException">
  /// libalpm reported a read failure without setting an error code, e.g. a malformed entry.
  /// </exception>
  /// <exception cref="AlpmException">The read failed with an <c>ALPM_ERR_*</c> code.</exception>
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
      // alpm_pkg_mtree_next returns -1 without setting pm_errno when an entry fails to
      // read, so this throws AlpmNativeFailureException. The errno is read here, as the
      // first statement of the failure branch, before another libalpm call can overwrite it.
      var rawErrno = (int)NativeMethods.alpm_errno(handlePtr);
      GC.KeepAlive(_package);
      ThrowMtreeReadFailure(rawErrno);
    }

    _current = MtreeEntry.FromNative(entry);
    GC.KeepAlive(_package);
    return true;
  }

  /// <summary>
  /// Closes the native archive reader and releases unmanaged resources.
  /// </summary>
  /// <exception cref="AlpmLifetimeException">
  /// The session that owns the package has already ended. The native archive reader then leaks.
  /// </exception>
  public void Dispose()
  {
    if (_disposed) return;
    _disposed = true;
    _current = null;
    if (_archive != null)
    {
      NativeMethods.alpm_pkg_mtree_close(_package.BackingStruct, _archive);
    }
    GC.KeepAlive(_package);
  }

  [DoesNotReturn]
  private static void ThrowMtreeReadFailure(int rawErrno)
  {
    throw NativeCall.Failure(rawErrno, "read package mtree").ToException();
  }
}
