using Orson.Alpm.Bindings;

namespace Orson.Alpm;

/// <summary>
/// A package loaded from an archive file. It owns the unmanaged package memory and releases it on
/// <see cref="Dispose" />, or when the owning <see cref="AlpmSession" /> is disposed.
/// </summary>
/// <remarks>
/// Represents a standalone package archive loaded from disk. Callers must deterministically release the package using
/// <see cref="Dispose()" /> (e.g. via a <c>using</c> statement), or transfer ownership to a transaction via
/// <see cref="Transaction.AddPackage(LoadedPackage)" />.
/// <para>
/// Once handed over to a transaction, the transaction assumes ownership of the unmanaged memory and this instance
/// becomes inert; calling <see cref="Dispose()" /> afterwards is a safe no-op. If neither disposal nor transaction
/// transfer occurs, unmanaged resources are retained until the owning <see cref="AlpmSession" /> is disposed.
/// </para>
/// </remarks>
public sealed unsafe class LoadedPackage : Package, IDisposable
{
  private readonly LoadedPackageRegistry _registry;
  private readonly ChildLifetime _lifetime;
  private nint _rawPackage;

  internal LoadedPackage(LoadedPackageRegistry registry, _alpm_pkg_t* backingStruct, ChildLifetime lifetime) : base(backingStruct)
  {
    _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    _rawPackage = (nint)backingStruct;
    _lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
    Lifetime = _lifetime.Capture();
  }

  /// <summary>Whether this instance still owns the package (it stops owning it on dispose or hand-over).</summary>
  internal bool OwnsPackage => !Disposed && _rawPackage != 0;

  /// <summary>Throws when the package was already released or handed to a transaction.</summary>
  /// <remarks>
  /// Checked before invoking native calls to prevent double-free or performing operations on a package whose
  /// ownership has already been transferred to a transaction.
  /// </remarks>
  internal void ThrowIfNotOwned()
  {
    if (!OwnsPackage) throw new ObjectDisposedException(GetType().FullName);
  }

  /// <summary>
  /// Gives up ownership <b>without</b> releasing the package, because somebody else owns the pointer now and
  /// releases it. <see cref="Dispose" /> becomes a no-op.
  /// </summary>
  /// <remarks>
  /// Named "disown" rather than "release" on purpose: nothing is freed here. Handing the package to a transaction
  /// is the only caller, and it must happen after the native call succeeded - the caller keeps ownership when it fails.
  /// </remarks>
  internal void Disown()
  {
    ThrowIfNotOwned();
    var pkg = (_alpm_pkg_t*)Interlocked.Exchange(ref _rawPackage, 0);
    if (pkg == null) return;
    Disposed = true;
    _registry.Unregister(pkg, freeNative: false);
    _lifetime.Invalidate("the hand-over to a transaction");
  }

  /// <summary>Releases the unmanaged resources used by the <see cref="LoadedPackage" />.</summary>
  /// <remarks>
  /// Invalidates the package lifetime token and frees the underlying unmanaged package structure. If the package
  /// was transferred to a transaction via <see cref="Transaction.AddPackage(LoadedPackage)" />, disposal is a no-op because
  /// the transaction assumes ownership.
  /// </remarks>
  public void Dispose()
  {
    if (Disposed) return;
    Disposed = true;
    var pkg = (_alpm_pkg_t*)Interlocked.Exchange(ref _rawPackage, 0);
    if (pkg == null) return;
    _lifetime.Invalidate("LoadedPackage.Dispose()");
    _registry.Unregister(pkg, freeNative: true);
  }
}
