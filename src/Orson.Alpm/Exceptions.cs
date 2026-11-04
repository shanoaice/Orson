using Orson.Alpm.Bindings;

namespace Orson.Alpm;

/// <summary>Base exception type for errors reported by libalpm operations.</summary>
/// <remarks>
/// Encapsulates the native error code (<see cref="Errno" />) and libalpm's error description (<see cref="StrError" />).
/// Specific failure categories derive from this class (e.g. <see cref="AlpmDatabaseException" />,
/// <see cref="AlpmTransactionException" />).
/// <para>
/// Native memory allocation failures (<c>ALPM_ERR_MEMORY</c>) are mapped to standard
/// <see cref="OutOfMemoryException" /> rather than deriving from this type.
/// </para>
/// </remarks>
public class AlpmException : Exception
{
  /// <summary>Creates the exception for <paramref name="errno" />.</summary>
  /// <param name="errno">The raw libalpm error code; consult <c>alpm.h</c> for its meaning.</param>
  /// <param name="strError">libalpm's message; defaults to <c>alpm_strerror(errno)</c> when omitted.</param>
  /// <param name="context">Optional operation description, e.g. "Failed to add package: foo".</param>
  /// <param name="inner">Optional inner exception.</param>
  public AlpmException(int errno, string? strError = null, string? context = null, Exception? inner = null)
    : base(BuildMessage(errno, strError ?? ErrnoMapper.StrError(errno), context), inner)
  {
    Errno = errno;
    StrError = strError ?? ErrnoMapper.StrError(errno);
  }

  /// <summary>Gets the raw native libalpm error code (<c>pm_errno</c>).</summary>
  /// <remarks>
  /// Callers should generally catch or pattern-match on specific exception types. This property provides the
  /// underlying error code for logging, diagnostics, or granular inspection.
  /// </remarks>
  public int Errno { get; }

  /// <summary>libalpm's own description of <see cref="Errno" /> ( <c>alpm_strerror</c>), if any.</summary>
  public string? StrError { get; }

  private static string BuildMessage(int errno, string? strError, string? context)
  {
    var name = ErrnoMapper.NameOf(errno);
    var detail = string.IsNullOrEmpty(strError) ? name : $"{name}: {strError}";

    return string.IsNullOrEmpty(context) ? detail : $"{context} ({detail})";
  }
}

/// <summary>An error from libalpm's database handling (<c>ALPM_ERR_DB_*</c>).</summary>
public sealed class AlpmDatabaseException(int errno, string? strError = null, string? context = null)
  : AlpmException(errno, strError, context);

/// <summary>An error concerning a package (<c>ALPM_ERR_PKG_*</c>).</summary>
/// <remarks>
/// <see cref="AlpmPackageException.Package" /> is set when the failing call already knew the package it was operating on;
/// the errno-driven factory cannot fill it in. It is typed as the shared read-only surface, because a failing call may
/// have been operating on either kind of package.
/// <para>
/// Not every <c>ALPM_ERR_PKG_*</c> errno is reported as this type: <c>ALPM_ERR_PKG_INVALID</c>,
/// <c>_INVALID_CHECKSUM</c>, <c>_INVALID_SIG</c> and <c>_INVALID_ARCH</c> are transaction failures with a payload, and are
/// reported as <see cref="AlpmTransactionException" /> from every entry point - see that type's remarks.
/// </para>
/// </remarks>
public sealed class AlpmPackageException(
  int errno,
  string? strError = null,
  Package? package = null,
  string? context = null)
  : AlpmException(errno, strError, context)
{
  /// <summary>The package the failed call was operating on, when known.</summary>
  public Package? Package { get; } = package;
}

/// <summary>A signature or keyring error (<c>ALPM_ERR_SIG_*</c>, missing signature support).</summary>
public sealed class AlpmSignatureException(int errno, string? strError = null, string? context = null)
  : AlpmException(errno, strError, context);

/// <summary>A download or retrieval error (<c>ALPM_ERR_RETRIEVE*</c>, libcurl, external downloader).</summary>
public sealed class AlpmRetrieveException(int errno, string? strError = null, string? context = null)
  : AlpmException(errno, strError, context);

/// <summary>An error encountered during transaction operations or transaction-related package validations.</summary>
/// <remarks>
/// Specialized nested classes provide structured details for failures that report payloads (such as unsatisfied
/// dependencies or conflicting files). When no specific payload is available, the base instance represents the failure:
/// <code>
/// catch (AlpmTransactionException ex)
/// {
///   switch (ex)
///   {
///     case AlpmTransactionException.MissingDependencies missing: ...
///     case AlpmTransactionException.ConflictingDependencies conflicts: ...
///     case AlpmTransactionException.ConflictingFiles files: ...
///     default: ...   // base transaction error without structured payload
///   }
/// }
/// </code>
/// </remarks>
public class AlpmTransactionException(int errno, string? strError = null, string? context = null)
  : AlpmException(errno, strError, context)
{
  /// <summary>Dependencies the transaction could not satisfy ( <c>ALPM_ERR_UNSATISFIED_DEPS</c>).</summary>
  public sealed class MissingDependencies(IReadOnlyList<MissingDependency> dependencies, string? context = null)
    : AlpmTransactionException((int)_alpm_errno_t.ALPM_ERR_UNSATISFIED_DEPS, context: context)
  {
    /// <summary>The unsatisfied dependencies, snapshotted out of libalpm's list.</summary>
    public IReadOnlyList<MissingDependency> Dependencies { get; } = dependencies;
  }

  /// <summary>Dependencies that conflict with each other (<c>ALPM_ERR_CONFLICTING_DEPS</c>).</summary>
  public sealed class ConflictingDependencies(IReadOnlyList<Conflict> conflicts, string? context = null)
    : AlpmTransactionException((int)_alpm_errno_t.ALPM_ERR_CONFLICTING_DEPS, context: context)
  {
    /// <summary>The conflicting pairs, snapshotted out of libalpm's list.</summary>
    public IReadOnlyList<Conflict> Conflicts { get; } = conflicts;
  }

  /// <summary>Files already on disk that a package in the transaction would overwrite (<c>ALPM_ERR_FILE_CONFLICTS</c>).</summary>
  public sealed class ConflictingFiles(IReadOnlyList<FileConflict> conflicts, string? context = null)
    : AlpmTransactionException((int)_alpm_errno_t.ALPM_ERR_FILE_CONFLICTS, context: context)
  {
    /// <summary>The file conflicts, snapshotted out of libalpm's list.</summary>
    public IReadOnlyList<FileConflict> Conflicts { get; } = conflicts;
  }

  /// <summary>Packages whose architecture the handle's architecture list does not accept (<c>ALPM_ERR_PKG_INVALID_ARCH</c>).</summary>
  /// <remarks>
  /// The strings are libalpm's own rendering of the offending packages ( <c>pkgname-pkgver-pkgarch</c>) rather than
  /// the archive filenames or paths passed to <see cref="AlpmSession.LoadPackage" />, so callers cannot map them back to
  /// input file paths.
  /// </remarks>
  public sealed class InvalidPackageArchitecture(IReadOnlyList<string> packages, string? context = null)
    : AlpmTransactionException((int)_alpm_errno_t.ALPM_ERR_PKG_INVALID_ARCH, context: context)
  {
    /// <summary>The rejected packages, as libalpm named them.</summary>
    public IReadOnlyList<string> Packages { get; } = packages;
  }

  /// <summary>Packages libalpm rejected as invalid (<c>ALPM_ERR_PKG_INVALID</c>).</summary>
  public sealed class InvalidPackage(IReadOnlyList<string> packages, string? context = null)
    : AlpmTransactionException((int)_alpm_errno_t.ALPM_ERR_PKG_INVALID, context: context)
  {
    /// <summary>The rejected packages, as libalpm named them.</summary>
    public IReadOnlyList<string> Packages { get; } = packages;
  }

  /// <summary>Packages whose checksum did not match (<c>ALPM_ERR_PKG_INVALID_CHECKSUM</c>).</summary>
  public sealed class InvalidPackageChecksum(IReadOnlyList<string> packages, string? context = null)
    : AlpmTransactionException((int)_alpm_errno_t.ALPM_ERR_PKG_INVALID_CHECKSUM, context: context)
  {
    /// <summary>The rejected packages, as libalpm named them.</summary>
    public IReadOnlyList<string> Packages { get; } = packages;
  }

  /// <summary>Packages whose signature did not verify (<c>ALPM_ERR_PKG_INVALID_SIG</c>).</summary>
  public sealed class InvalidPackageSignature(IReadOnlyList<string> packages, string? context = null)
    : AlpmTransactionException((int)_alpm_errno_t.ALPM_ERR_PKG_INVALID_SIG, context: context)
  {
    /// <summary>The rejected packages, as libalpm named them.</summary>
    public IReadOnlyList<string> Packages { get; } = packages;
  }

}

/// <summary>Exception thrown when a libalpm operation fails without setting a native error code on the library handle.</summary>
/// <remarks>
/// Occurs when an unmanaged call indicates failure (e.g. returning a non-zero status or null pointer), but
/// libalpm leaves <c>pm_errno</c> unset (<c>0</c>).
/// </remarks>
public sealed class AlpmNativeFailureException : AlpmException
{
  /// <summary>Creates the exception for the operation that failed.</summary>
  /// <param name="operation">What failed, e.g. <c>"unregister database"</c>.</param>
  /// <param name="strError">Overrides the default explanation.</param>
  /// <param name="inner">Optional inner exception.</param>
  public AlpmNativeFailureException(string operation, string? strError = null, Exception? inner = null)
    : base(0, strError ?? "libalpm reported a failure without setting an error code.",
      context: operation, inner: inner)
  {
    Operation = operation;
  }

  /// <summary>The operation that failed, e.g. <c>"unregister database"</c>.</summary>
  public string Operation { get; }
}

/// <summary>
/// Thrown when an operation attempts to access a borrowed view, database, or resource whose underlying unmanaged
/// memory has been invalidated or deallocated.
/// </summary>
/// <remarks>
/// Derives from <see cref="InvalidOperationException" />: the object itself is intact, but the operation is not
/// valid in its current (released) state.
/// </remarks>
public sealed class AlpmLifetimeException : InvalidOperationException
{
  /// <summary>Creates the exception for the invalidated resource <paramref name="target" />.</summary>
  /// <param name="target">The resource that was invalidated, e.g. <c>"the local database"</c>.</param>
  /// <param name="invalidatedBy">
  /// The operation that released it, e.g. <c>"Database.Unregister()"</c>; <c>null</c> when
  /// unknown.
  /// </param>
  public AlpmLifetimeException(string target, string? invalidatedBy)
    : base(BuildMessage(target, invalidatedBy))
  {
    Target = target;
    InvalidatedBy = invalidatedBy;
  }

  /// <summary>The target resource that was invalidated (e.g. 'the local database', 'the sync database core').</summary>
  public string Target { get; }

  /// <summary>The operation that caused the invalidation (e.g. 'Database.Unregister()', 'Transaction.Commit()').</summary>
  public string? InvalidatedBy { get; }

  private static string BuildMessage(string target, string? invalidatedBy)
  {
    var reason = invalidatedBy is null
      ? $"{target} is no longer valid"
      : $"{target} was released by {invalidatedBy}";

    return $"{reason}, so this object points into unmanaged memory that has been deallocated. "
           + "A borrowed resource or view remains valid only while its owning context is active: "
           + "use it within the active scope, or call ToSnapshot() before releasing the owner.";
  }
}
