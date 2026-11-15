namespace Orson.Alpm;

/// <summary>
/// Exposes common read-only package metadata and dependency information across live views,
/// loaded package archives, and detached snapshots.
/// </summary>
public interface IPackageMetadata
{
  /// <summary>Gets the package name.</summary>
  string Name { get; }

  /// <summary>Gets the package version.</summary>
  PackageVersion Version { get; }

  /// <summary>Gets the archive filename of the package, or <c>null</c> if not applicable.</summary>
  string? Filename { get; }

  /// <summary>Gets the base package name (pkgbase) if part of a split package, or <c>null</c> if none.</summary>
  string? Base { get; }

  /// <summary>Gets the description of the package, or <c>null</c> if none was provided.</summary>
  string? Description { get; }

  /// <summary>Gets the upstream project website or homepage URL, or <c>null</c> if not specified.</summary>
  string? Url { get; }

  /// <summary>Gets the date and time when the package was built.</summary>
  DateTimeOffset BuildDate { get; }

  /// <summary>Gets the date and time when the package was installed locally, or <c>null</c> if not installed.</summary>
  DateTimeOffset? InstallDate { get; }

  /// <summary>Gets the name and contact information of the packager, or <c>null</c> if not specified.</summary>
  string? Packager { get; }

  /// <summary>Gets the MD5 checksum of the package archive, or <c>null</c> if not available.</summary>
  string? Md5Sum { get; }

  /// <summary>Gets the SHA-256 checksum of the package archive, or <c>null</c> if not available.</summary>
  string? Sha256Sum { get; }

  /// <summary>Gets the target CPU architecture for this package (e.g. "x86_64", "any"), or <c>null</c> if not specified.</summary>
  string? Architecture { get; }

  /// <summary>Gets the download size of the package archive in bytes.</summary>
  long Size { get; }

  /// <summary>Gets the unpacked size of the package when installed on disk, in bytes.</summary>
  long InstalledSize { get; }

  /// <summary>Gets the estimated or calculated download size of the package in bytes.</summary>
  long DownloadSize { get; }

  /// <summary>Gets the origin indicating where this package was loaded from.</summary>
  PackageOrigin Origin { get; }

  /// <summary>Gets the reason why this package is installed.</summary>
  PackageReason Reason { get; }

  /// <summary>Gets the validation methods used to verify this package.</summary>
  PackageValidation Validation { get; }

  /// <summary>Gets the licenses governing the distribution and use of this package.</summary>
  IReadOnlyCollection<string> Licenses { get; }

  /// <summary>Gets the package groups that this package belongs to.</summary>
  IReadOnlyCollection<string> Groups { get; }

  /// <summary>Gets the list of packages required to run this package.</summary>
  IReadOnlyCollection<Dependency> Depends { get; }

  /// <summary>Gets the list of optional packages that provide additional functionality.</summary>
  IReadOnlyCollection<Dependency> OptionalDepends { get; }

  /// <summary>Gets the list of dependencies required only to run the test suite when building.</summary>
  IReadOnlyCollection<Dependency> CheckDepends { get; }

  /// <summary>Gets the list of dependencies required only to build the package from source.</summary>
  IReadOnlyCollection<Dependency> MakeDepends { get; }

  /// <summary>Gets the list of packages that conflict with this package.</summary>
  IReadOnlyCollection<Dependency> Conflicts { get; }

  /// <summary>Gets the virtual provisions or features provided by this package.</summary>
  IReadOnlyCollection<Dependency> Provides { get; }

  /// <summary>Gets the list of packages that this package replaces.</summary>
  IReadOnlyCollection<Dependency> Replaces { get; }

  /// <summary>Gets the package's file list, or <c>null</c> if not loaded.</summary>
  IReadOnlyCollection<PackageFile>? Files { get; }

  /// <summary>Gets the list of configuration files marked for backup, or <c>null</c> if not loaded.</summary>
  IReadOnlyCollection<BackupEntry>? BackupFiles { get; }

  /// <summary>Gets a value indicating whether the package contains an install or upgrade scriptlet.</summary>
  bool HasScriptlet { get; }

  /// <summary>Gets the base64-encoded PGP signature of the package, or <c>null</c> if none is present.</summary>
  string? Base64Signature { get; }
}
