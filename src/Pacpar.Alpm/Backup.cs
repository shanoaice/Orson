using Pacpar.Alpm.Bindings;

namespace Pacpar.Alpm;

/// <summary>
/// A backup entry (<c>alpm_backup_t</c>).
/// </summary>
/// <remarks>
/// A managed snapshot of an ALPM package backup file entry. The name is copied on construction,
/// so the entry remains accessible even after the originating package is released.
/// </remarks>
public sealed class Backup
{
  internal unsafe Backup(_alpm_backup_t* backingStruct)
  {
    Name = NativeString.FromNative((nint)backingStruct->name);
    Hash = backingStruct->hash != null ? NativeString.FromNative((nint)backingStruct->hash) : null;
  }

  // The token parameter matches the element-factory delegate signature; a Backup is an eager
  // snapshot, so an issued element retains no native pointer and needs no element token.
  internal static unsafe Backup Factory(void* ptr, Lifetime? lifetime) => new((_alpm_backup_t*)ptr);

  /// <summary>
  /// Borrowed view over a backup-entry list owned by libalpm (for example
  /// <c>alpm_pkg_get_backup</c>).
  /// </summary>
  /// <param name="ptr">The borrowed list. May be <c>null</c>.</param>
  /// <param name="lifetime">Token of the package that owns the list; guards traversal.</param>
  internal static unsafe AlpmList<Backup> ListFactory(_alpm_list_t* ptr, Lifetime? lifetime)
    => AlpmList<Backup>.Borrow(ptr, &Factory, lifetime);

  /// <summary>
  /// The relative file path of the backup configuration file.
  /// </summary>
  public string? Name { get; }

  /// <summary>
  /// The MD5 hash recorded for the file when the package was installed, or <c>null</c> if no hash is
  /// recorded (for example for a package loaded from a file rather than from the local database).
  /// </summary>
  /// <remarks>
  /// pacman compares this hash with the file on disk to decide whether the file is modified. libalpm
  /// may leave both the hash pointer and the string itself <c>null</c>; both cases map to <c>null</c>.
  /// </remarks>
  public string? Hash { get; }
}
