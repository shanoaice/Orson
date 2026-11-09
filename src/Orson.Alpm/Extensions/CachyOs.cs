using JetBrains.Annotations;
using Orson.Alpm.Bindings;
using Orson.Alpm.Interop;

namespace Orson.Alpm.Extensions;

/// <summary>
/// Provides extension methods and capability checks for CachyOS downstream extensions in libalpm.
/// </summary>
[PublicAPI]
public static class CachyOs
{
  /// <summary>
  /// Gets a value indicating whether the current runtime environment provides CachyOS extensions in libalpm.
  /// </summary>
  [PublicAPI]
  public static bool IsCachyOsEnvironment => IsPhysicalArchitectureSupported || IsInstalledDatabaseSupported;

  /// <summary>
  /// Gets a value indicating whether physical architecture detection is supported.
  /// </summary>
  [PublicAPI]
  public static bool IsPhysicalArchitectureSupported => CachyOsMethods.IsPhysicalArchitectureSupported;

  /// <summary>
  /// Gets a value indicating whether querying the package origin database is supported.
  /// </summary>
  [PublicAPI]
  public static bool IsInstalledDatabaseSupported => CachyOsMethods.IsInstalledDatabaseSupported;

  /// <summary>
  /// Gets the CPU physical architectures and microarchitecture levels detected for the host system.
  /// </summary>
  /// <param name="options">The ALPM session options.</param>
  /// <returns>A read-only list of architecture identifiers supported by the host CPU.</returns>
  /// <exception cref="ArgumentNullException"><paramref name="options" /> is <see langword="null" />.</exception>
  /// <exception cref="AlpmLifetimeException">The underlying ALPM handle has been released or is invalid.</exception>
  /// <exception cref="PlatformNotSupportedException">The installed libalpm does not export <c>alpm_option_get_physical_architectures</c>.</exception>
  [PublicAPI]
  public static unsafe IReadOnlyList<string> GetPhysicalArchitecture(this AlpmOptions options)
  {
    ArgumentNullException.ThrowIfNull(options);
    options.Handle.ThrowIfDisposed();

    var list = CachyOsMethods.alpm_option_get_physical_architectures(options.Handle);
    GC.KeepAlive(options);
    return AlpmStringList.TakeOwned(list, &UnmanagedDestructors.CFreeExtern);
  }

  /// <summary>
  /// Gets the name of the sync database from which this installed package originated.
  /// </summary>
  /// <param name="package">The package to inspect.</param>
  /// <returns>
  /// The name of the sync repository recorded when the package was installed,
  /// or <see langword="null" /> if the package was not installed from a sync database.
  /// </returns>
  /// <exception cref="ArgumentNullException"><paramref name="package" /> is <see langword="null" />.</exception>
  /// <exception cref="ObjectDisposedException">The package is disposed.</exception>
  /// <exception cref="AlpmLifetimeException">The lifetime domain that owns the package has been invalidated.</exception>
  /// <exception cref="PlatformNotSupportedException">The installed libalpm does not export <c>alpm_pkg_get_installed_db</c>.</exception>
  [PublicAPI]
  public static unsafe string? GetInstalledDatabase(this Package package)
  {
    ArgumentNullException.ThrowIfNull(package);
    var ptr = CachyOsMethods.alpm_pkg_get_installed_db(package.BackingStruct);
    var result = NativeString.FromNative((nint)ptr);
    GC.KeepAlive(package);
    return result;
  }
}
