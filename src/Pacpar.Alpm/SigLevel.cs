namespace Pacpar.Alpm;

/// <summary>PGP signature verification options</summary>
/// <remarks>
/// A bitmask controlling PGP signature verification for databases and packages.
/// <para>
/// The member names drop the native <c>AlpmSig</c> prefix, and each member records the native constant it projects.
/// </para>
/// </remarks>
[Flags]
public enum SigLevel : uint
{
  /// <summary>No signature verification; all signature checks are disabled (<c>Never</c> in pacman configuration) (0).</summary>
  None = 0,

  /// <summary>Packages require a signature (<c>ALPM_SIG_PACKAGE</c>).</summary>
  Package = 1,

  /// <summary>
  /// Packages do not require a signature, but check packages that do have signatures (
  /// <c>ALPM_SIG_PACKAGE_OPTIONAL</c>).
  /// </summary>
  PackageOptional = 2,

  /// <summary>Allow packages with signatures that have marginal trust ( <c>ALPM_SIG_PACKAGE_MARGINAL_OK</c>).</summary>
  PackageMarginalOk = 4,

  /// <summary>Allow packages with signatures that are unknown trust ( <c>ALPM_SIG_PACKAGE_UNKNOWN_OK</c>).</summary>
  PackageUnknownOk = 8,

  /// <summary>Databases require a signature (<c>ALPM_SIG_DATABASE</c>).</summary>
  Database = 1024,

  /// <summary>
  /// Databases do not require a signature, but check databases that do have signatures (
  /// <c>ALPM_SIG_DATABASE_OPTIONAL</c> ).
  /// </summary>
  DatabaseOptional = 2048,

  /// <summary>Allow databases with signatures that are marginal trust ( <c>ALPM_SIG_DATABASE_MARGINAL_OK</c>).</summary>
  DatabaseMarginalOk = 4096,

  /// <summary>Allow databases with signatures that are unknown trust ( <c>ALPM_SIG_DATABASE_UNKNOWN_OK</c>).</summary>
  DatabaseUnknownOk = 8192,

  /// <summary>The Default siglevel (<c>ALPM_SIG_USE_DEFAULT</c>).</summary>
  UseDefault = 1073741824,
}
