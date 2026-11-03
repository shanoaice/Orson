namespace Pacpar.Alpm;

/// <summary>Specifies where a package originated (local database, sync repository, or loaded archive file).</summary>
public enum PackageOrigin : uint
{
  /// <summary>Loaded from a package file (<c>ALPM_PKG_FROM_FILE</c>).</summary>
  File = 1,

  /// <summary>From the local database (<c>ALPM_PKG_FROM_LOCALDB</c>).</summary>
  LocalDatabase = 2,

  /// <summary>From a sync database (<c>ALPM_PKG_FROM_SYNCDB</c>).</summary>
  SyncDatabase = 3
}

/// <summary>Specifies the installation reason for a package (explicitly requested or installed as a dependency).</summary>
public enum PackageReason : uint
{
  /// <summary>Explicitly installed by the user (<c>ALPM_PKG_REASON_EXPLICIT</c>).</summary>
  Explicit = 0,

  /// <summary>Installed as a dependency (<c>ALPM_PKG_REASON_DEPEND</c>).</summary>
  Dependency = 1,

  /// <summary>
  /// The installation reason could not be determined, for example after a parse failure in the local database (
  /// <c>ALPM_PKG_REASON_UNKNOWN</c>).
  /// </summary>
  Unknown = 2
}

/// <summary>Flags indicating the validation methods used to verify package integrity and authenticity.</summary>
[Flags]
public enum PackageValidation : uint
{
  /// <summary>The package's validation type is unknown (<c>ALPM_PKG_VALIDATION_UNKNOWN</c>).</summary>
  Unknown = 0,

  /// <summary>The package does not have any validation (<c>ALPM_PKG_VALIDATION_NONE</c>).</summary>
  None = 1,

  /// <summary>The package is validated with md5 (<c>ALPM_PKG_VALIDATION_MD5SUM</c>).</summary>
  Md5Sum = 2,

  /// <summary>The package is validated with sha256 (<c>ALPM_PKG_VALIDATION_SHA256SUM</c>).</summary>
  Sha256Sum = 4,

  /// <summary>The package is validated with a PGP signature ( <c>ALPM_PKG_VALIDATION_SIGNATURE</c>).</summary>
  Signature = 8,
}
