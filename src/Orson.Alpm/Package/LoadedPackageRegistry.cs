using Orson.Alpm.Bindings;

namespace Orson.Alpm;

/// <summary>Registry tracking file-loaded packages for session-scoped cleanup.</summary>
/// <remarks>
/// When libalpm loads a package from a file via <c>alpm_pkg_load</c>, the resulting pointer is not tied to any database or
/// handle lifecycle. If the package is never handed to a transaction or explicitly disposed, it would leak upon session
/// end. This registry tracks all active file-loaded package pointers for a session so that <see cref="Dispose" /> can free
/// any undisposed packages before the native session handle is released.
/// <para>
/// The lock is defensive: the registry belongs to the session's thread, like the rest of libalpm, but
/// synchronization protects against concurrent access during registration, unregistration, and teardown.
/// </para>
/// </remarks>
internal sealed unsafe class LoadedPackageRegistry : IDisposable
{
  private readonly HashSet<nint> _loadedPackages = [];
  private readonly Lock _lock = new();

  /// <summary>Registers a newly loaded package pointer for session tracking.</summary>
  /// <param name="pkg">The unmanaged package pointer allocated by <c>alpm_pkg_load</c>.</param>
  internal void Register(_alpm_pkg_t* pkg)
  {
    if (pkg == null) return;
    lock (_lock)
    {
      _loadedPackages.Add((nint)pkg);
    }
  }

  /// <summary>Unregisters a package pointer when disposed or handed over to a transaction.</summary>
  /// <param name="pkg">The unmanaged package pointer.</param>
  /// <param name="freeNative">
  /// <c>true</c> if the native package memory should be freed immediately with <c>alpm_pkg_free</c>
  /// ; <c>false</c> if ownership was transferred to a transaction.
  /// </param>
  internal void Unregister(_alpm_pkg_t* pkg, bool freeNative)
  {
    if (pkg == null) return;

    bool wasTracked;
    lock (_lock)
    {
      wasTracked = _loadedPackages.Remove((nint)pkg);
    }

    if (wasTracked && freeNative)
    {
      _ = NativeMethods.alpm_pkg_free(pkg);
    }
  }

  /// <summary>Frees all remaining file-loaded packages tracked by this session.</summary>
  public void Dispose()
  {
    lock (_lock)
    {
      foreach (var ptr in _loadedPackages)
      {
        _ = NativeMethods.alpm_pkg_free((_alpm_pkg_t*)ptr);
      }

      _loadedPackages.Clear();
    }
  }
}
