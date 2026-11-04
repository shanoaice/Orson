using System.Diagnostics.CodeAnalysis;
using System.Text;
using Pacpar.Alpm.Bindings;
using Pacpar.Alpm.Interop;

namespace Pacpar.Alpm;

/// <summary>The read-only abstraction for a package, exposing its metadata and dependency information.</summary>
/// <remarks>
/// <para>Concrete implementations are represented by two sibling types:</para>
/// <list type="bullet">
/// <item>
/// <description><see cref="PackageView" />: A borrowed, non-disposable view of a database or transaction package.</description>
/// </item>
/// <item>
/// <description>
/// <see cref="LoadedPackage" />: An independently loaded archive file that owns unmanaged resources and
/// implements <see cref="IDisposable" />.
/// </description>
/// </item>
/// </list>
/// <para>
/// Scalar metadata (such as <see cref="Name" />, <see cref="Version" />, and <see cref="Description" />) is loaded
/// lazily on demand and cached in memory. Accessing properties on an instance whose parent database, transaction, or
/// session has been released throws <see cref="AlpmLifetimeException" />.
/// </para>
/// </remarks>
public abstract unsafe partial class Package
{
  // Only this class and its derived types can see the pointer. A sibling in the same assembly
  // (Transaction, for one) cannot read it, so "reach for the pointer without the guard" is not a
  // shape that can be written any more.
  private protected Package(_alpm_pkg_t* backingStruct)
  {
    BackingStruct = backingStruct;
  }

  /// <summary>The native pointer this wrapper reads.</summary>
  /// <remarks>
  /// The backing field is compiler-generated (C# 14 <c>field</c>), so it has no name any member could read: the
  /// only way to the pointer is this accessor, which runs the guard first. That closes the gap the previous shape left - a
  /// member that read the field directly skipped the guard, and no compiler check could catch it.
  /// </remarks>
  internal _alpm_pkg_t* BackingStruct
  {
    get
    {
      ThrowIfDisposed();
      return field;
    }

    // Assignable from this class's constructor only: a wrapper's pointer is fixed for its lifetime.
    private init;
  }

  /// <summary>
  /// The lifetime token of the native context that owns <see cref="BackingStruct" /> - the database, transaction or
  /// handle this package was borrowed from, or - for a <see cref="LoadedPackage" /> - the domain of the package this
  /// instance owns, which the session that loaded it anchors. <c>null</c> means the wrapper carries no owning context to
  /// check.
  /// </summary>
  /// <remarks>
  /// Not <c>readonly</c> on purpose: a <see cref="LoadedPackage" /> creates its root token in its constructor body,
  /// because <c>this</c> is not available to a base constructor initializer.
  /// </remarks>
  private protected LifetimeStamp? Lifetime;

  /// <summary>
  /// Set by <see cref="LoadedPackage" /> once it has released the package or handed it to a transaction. It retires
  /// the wrapper as a whole, reads included: after a hand-over the pointer stays valid only until the transaction is
  /// released, which this wrapper cannot observe.
  /// </summary>
  private protected bool Disposed;

  private protected void ThrowIfDisposed()
  {
    // The retirement flag wins: a disposed or handed-over wrapper reports ObjectDisposedException
    // even when its owning context is gone too; the token check covers contexts released while
    // this wrapper was still nominally usable.
    if (Disposed) throw new ObjectDisposedException(GetType().FullName);
    Lifetime?.ThrowIfStale();
  }

  internal _alpm_handle_t* LibraryHandle
  {
    get
    {
      var handle = NativeMethods.alpm_pkg_get_handle(BackingStruct);
      GC.KeepAlive(this);
      return handle;
    }
  }

  /// <summary>The package name.</summary>
  public string Name
  {
    get
    {
      // This one keeps an explicit guard: `field ??=` returns the cached name without evaluating its
      // right-hand side, so on every read after the first the guarded accessor never runs. A member
      // that can answer from a cache has to check the stamp itself.
      ThrowIfDisposed();
      return field ??= NativeString.FromNative((nint)NativeMethods.alpm_pkg_get_name(BackingStruct))!;
    }
  }

  /// <summary>Checks whether the package's MD5 checksum matches the expected value.</summary>
  /// <returns><c>true</c> if the checksum matches; <c>false</c> if the package archive is corrupted.</returns>
  /// <exception cref="AlpmException">
  /// An operational error occurred while checking the checksum (for example, the package did
  /// not originate from a sync database).
  /// </exception>
  public bool CheckMd5Sum()
  {
    if (!TryCheckMd5Sum(out var matches, out var failure))
    {
      throw failure.ToException();
    }
    return matches;
  }

  /// <summary>Checks whether this package should be ignored according to the library configuration.</summary>
  public bool ShouldIgnore()
  {
    var ignore = NativeMethods.alpm_pkg_should_ignore(LibraryHandle, BackingStruct) != 0;
    GC.KeepAlive(this);
    return ignore;
  }

  private bool _filenameLoaded;

  /// <summary>The archive filename of the package, or <c>null</c> if not applicable (such as for local database packages).</summary>
  public string? Filename
  {
    get
    {
      // See Name: a cache hit returns before the guarded accessor runs, so check the stamp here.
      ThrowIfDisposed();
      if (_filenameLoaded) return field;
      field = NativeString.FromNative((nint)NativeMethods.alpm_pkg_get_filename(BackingStruct));
      _filenameLoaded = true;

      return field;
    }
  }

  private bool _baseLoaded;

  /// <summary>The base package name (pkgbase) if part of a split package, or <c>null</c> if none.</summary>
  public string? Base
  {
    get
    {
      // See Name: a cache hit returns before the guarded accessor runs, so check the stamp here.
      ThrowIfDisposed();
      if (!_baseLoaded)
      {
        field = NativeString.FromNative((nint)NativeMethods.alpm_pkg_get_base(BackingStruct));
        _baseLoaded = true;
      }

      return field;
    }
  }

  /// <summary>The version of the package.</summary>
  public PackageVersion Version
  {
    get
    {
      var version = new PackageVersion(NativeMethods.alpm_pkg_get_version(BackingStruct));
      GC.KeepAlive(this);
      return version;
    }
  }

  /// <summary>The origin indicating where this package was loaded from.</summary>
  public PackageOrigin Origin
  {
    get
    {
      var origin = (PackageOrigin)(uint)NativeMethods.alpm_pkg_get_origin(BackingStruct);
      GC.KeepAlive(this);
      return origin;
    }
  }

  private bool _descriptionLoaded;

  /// <summary>The description of the package.</summary>
  public string? Description
  {
    get
    {
      // See Name: a cache hit returns before the guarded accessor runs, so check the stamp here.
      ThrowIfDisposed();
      if (!_descriptionLoaded)
      {
        field = NativeString.FromNative((nint)NativeMethods.alpm_pkg_get_desc(BackingStruct));
        _descriptionLoaded = true;
      }

      return field;
    }
  }

  private bool _urlLoaded;

  /// <summary>The upstream project website or homepage URL.</summary>
  public string? Url
  {
    get
    {
      // See Name: a cache hit returns before the guarded accessor runs, so check the stamp here.
      ThrowIfDisposed();
      if (!_urlLoaded)
      {
        field = NativeString.FromNative((nint)NativeMethods.alpm_pkg_get_url(BackingStruct));
        _urlLoaded = true;
      }

      return field;
    }
  }

  /// <summary>The date and time when the package was built.</summary>
  public DateTimeOffset BuildDate
  {
    get
    {
      var date = DateTimeOffset.FromUnixTimeSeconds(NativeMethods.alpm_pkg_get_builddate(BackingStruct));
      GC.KeepAlive(this);
      return date;
    }
  }

  /// <summary>The date and time when the package was installed locally, or <c>null</c> if not installed.</summary>
  public DateTimeOffset? InstallDate
  {
    get
    {
      var date = NativeMethods.alpm_pkg_get_installdate(BackingStruct);
      GC.KeepAlive(this);
      return date == 0 ? null : DateTimeOffset.FromUnixTimeSeconds(date);
    }
  }

  private bool _packagerLoaded;

  /// <summary>The name and contact information of the packager.</summary>
  public string? Packager
  {
    get
    {
      // See Name: a cache hit returns before the guarded accessor runs, so check the stamp here.
      ThrowIfDisposed();
      if (!_packagerLoaded)
      {
        field = NativeString.FromNative((nint)NativeMethods.alpm_pkg_get_packager(BackingStruct));
        _packagerLoaded = true;
      }

      return field;
    }
  }

  private bool _md5SumLoaded;

  /// <summary>The MD5 checksum of the package archive, or <c>null</c> if not available.</summary>
  public string? Md5Sum
  {
    get
    {
      // See Name: a cache hit returns before the guarded accessor runs, so check the stamp here.
      ThrowIfDisposed();
      if (!_md5SumLoaded)
      {
        field = NativeString.FromNative((nint)NativeMethods.alpm_pkg_get_md5sum(BackingStruct));
        _md5SumLoaded = true;
      }

      return field;
    }
  }

  private bool _sha256SumLoaded;

  /// <summary>The SHA-256 checksum of the package archive, or <c>null</c> if not available.</summary>
  public string? Sha256Sum
  {
    get
    {
      // See Name: a cache hit returns before the guarded accessor runs, so check the stamp here.
      ThrowIfDisposed();
      if (!_sha256SumLoaded)
      {
        field = NativeString.FromNative((nint)NativeMethods.alpm_pkg_get_sha256sum(BackingStruct));
        _sha256SumLoaded = true;
      }

      return field;
    }
  }

  private bool _archLoaded;

  /// <summary>The target CPU architecture for this package (e.g. "x86_64", "any").</summary>
  public string? Architecture
  {
    get
    {
      // See Name: a cache hit returns before the guarded accessor runs, so check the stamp here.
      ThrowIfDisposed();
      if (!_archLoaded)
      {
        field = NativeString.FromNative((nint)NativeMethods.alpm_pkg_get_arch(BackingStruct));
        _archLoaded = true;
      }

      return field;
    }
  }

  /// <summary>The download size of the package archive in bytes.</summary>
  public long Size
  {
    get
    {
      var size = NativeMethods.alpm_pkg_get_size(BackingStruct);
      GC.KeepAlive(this);
      return size.Value;
    }
  }

  /// <summary>The unpacked size of the package when installed on disk, in bytes.</summary>
  public long InstalledSize
  {
    get
    {
      var installedSize = NativeMethods.alpm_pkg_get_isize(BackingStruct);
      GC.KeepAlive(this);
      return installedSize.Value;
    }
  }

  /// <summary>The estimated or calculated download size of the package in bytes.</summary>
  public long DownloadSize
  {
    get
    {
      var size = NativeMethods.alpm_pkg_download_size(BackingStruct);
      GC.KeepAlive(this);
      return size.Value;
    }
  }

  /// <summary>The reason why this package is installed (explicitly requested or installed as a dependency).</summary>
  public PackageReason Reason
  {
    get
    {
      var reason = (PackageReason)(uint)NativeMethods.alpm_pkg_get_reason(BackingStruct);
      GC.KeepAlive(this);
      return reason;
    }
  }

  /// <summary>The validation methods used to verify this package.</summary>
  public PackageValidation Validation
  {
    get
    {
      var val = (PackageValidation)NativeMethods.alpm_pkg_get_validation(BackingStruct);
      GC.KeepAlive(this);
      return val;
    }
  }

  /// <summary>The licenses governing the distribution and use of this package.</summary>
  public AlpmStringList Licenses => new(NativeMethods.alpm_pkg_get_licenses(BackingStruct), Lifetime?.Domain);

  /// <summary>The package groups that this package belongs to.</summary>
  public AlpmStringList Groups => new(NativeMethods.alpm_pkg_get_groups(BackingStruct), Lifetime?.Domain);

  /// <summary>The list of packages required to run this package.</summary>
  public AlpmList<Dependency> Depends => Dependency.ListFactory(NativeMethods.alpm_pkg_get_depends(BackingStruct), Lifetime?.Domain);

  /// <summary>The list of optional packages that provide additional functionality.</summary>
  public AlpmList<Dependency> OptionalDepends => Dependency.ListFactory(NativeMethods.alpm_pkg_get_optdepends(BackingStruct), Lifetime?.Domain);

  /// <summary>The list of dependencies required only to run the test suite when building.</summary>
  public AlpmList<Dependency> CheckDepends => Dependency.ListFactory(NativeMethods.alpm_pkg_get_checkdepends(BackingStruct), Lifetime?.Domain);

  /// <summary>The list of dependencies required only to build the package from source.</summary>
  public AlpmList<Dependency> MakeDepends => Dependency.ListFactory(NativeMethods.alpm_pkg_get_makedepends(BackingStruct), Lifetime?.Domain);

  /// <summary>The list of packages that conflict with this package.</summary>
  public AlpmList<Dependency> Conflicts => Dependency.ListFactory(NativeMethods.alpm_pkg_get_conflicts(BackingStruct), Lifetime?.Domain);

  /// <summary>The virtual provisions or features provided by this package.</summary>
  public AlpmList<Dependency> Provides => Dependency.ListFactory(NativeMethods.alpm_pkg_get_provides(BackingStruct), Lifetime?.Domain);

  /// <summary>The list of packages that this package replaces.</summary>
  public AlpmList<Dependency> Replaces => Dependency.ListFactory(NativeMethods.alpm_pkg_get_replaces(BackingStruct), Lifetime?.Domain);

  /// <summary>The package's file list, read on demand.</summary>
  public FileList Files => new(NativeMethods.alpm_pkg_get_files(BackingStruct), Lifetime?.Domain);

  /// <summary>The list of configuration files marked for backup (subject to .pacnew / .pacsave preservation rules).</summary>
  public AlpmList<BackupEntry> BackupFiles => BackupEntry.ListFactory(NativeMethods.alpm_pkg_get_backup(BackingStruct), Lifetime?.Domain);

  /// <summary>Gets the names of packages that depend on this package.</summary>
  public IReadOnlyList<string> GetRequiredBy()
  {
    var list = AlpmStringList.TakeOwned(NativeMethods.alpm_pkg_compute_requiredby(BackingStruct),
      &UnmanagedDestructors.CFreeExtern);
    GC.KeepAlive(this);
    return list;
  }

  /// <summary>Gets the names of packages that optionally depend on this package.</summary>
  public IReadOnlyList<string> GetOptionalFor()
  {
    var list = AlpmStringList.TakeOwned(NativeMethods.alpm_pkg_compute_optionalfor(BackingStruct),
      &UnmanagedDestructors.CFreeExtern);
    GC.KeepAlive(this);
    return list;
  }

  private bool _base64SignatureLoaded;

  /// <summary>Gets the base64-encoded PGP signature of the package, or <c>null</c> if none is present.</summary>
  public string? Base64Signature
  {
    get
    {
      // See Name: a cache hit returns before the guarded accessor runs, so check the stamp here.
      ThrowIfDisposed();
      if (_base64SignatureLoaded) return field;
      field = NativeString.FromNative((nint)NativeMethods.alpm_pkg_get_base64_sig(BackingStruct));
      _base64SignatureLoaded = true;

      return field;
    }
  }

  /// <summary>Gets a value indicating whether the package contains an install or upgrade scriptlet.</summary>
  public bool HasScriptlet
  {
    get
    {
      var has = NativeMethods.alpm_pkg_has_scriptlet(BackingStruct) != 0;
      GC.KeepAlive(this);
      return has;
    }
  }
  private byte[]? _signature;
  private bool _signatureLoaded;

  /// <summary>Gets the package's embedded binary PGP signature, or <c>null</c> if none is present.</summary>
  /// <remarks>
  /// Returns the raw signature bytes, cached in memory on first access. For the base64-encoded string
  /// representation, see <see cref="Base64Signature" />.
  /// </remarks>
  public ReadOnlyMemory<byte>? GetSignature()
  {
    // See Name: a cache hit returns before the guarded accessor runs, so check the stamp here.
    ThrowIfDisposed();

    if (!_signatureLoaded)
    {
      // Captured before the call: on the failure path the errno read must be the next native
      // interaction, and this property is itself a native call (alpm_pkg_get_handle).
      var handlePtr = LibraryHandle;
      byte* buffer = null;
      nuint len;
      var result = NativeMethods.alpm_pkg_get_sig(BackingStruct, &buffer, &len);
      if (result != 0)
      {
        // Errno first, anchor second: KeepAlive keeps this alive only up to its own instruction, so
        // it has to come after the last native read on this path.
        var rawErrno = (int)NativeMethods.alpm_errno(handlePtr);
        GC.KeepAlive(this);
        ThrowSignatureFailure(rawErrno);
      }
      if (buffer != null)
      {
        _signature = new Span<byte>(buffer, (int)len).ToArray();
        UnmanagedDestructors.CFree(buffer);
      }

      _signatureLoaded = true;
    }

    return _signature is null ? null : new ReadOnlyMemory<byte>(_signature);
  }

  /// <summary>
  /// Copies everything this package reads into a <see cref="PackageSnapshot" /> with no ties to libalpm, so the
  /// result stays valid after the database, transaction or handle it came from is gone.
  /// </summary>
  /// <param name="includeFiles">
  /// Also copies the file list. It is off by default because it is by far the most expensive part
  /// of the copy and most callers do not need it.
  /// </param>
  public PackageSnapshot ToSnapshot(bool includeFiles = false) => new(this, includeFiles);

  /// <summary>Opens a stream for reading the package changelog, or returns <c>null</c> if no changelog is available.</summary>
  /// <remarks>
  /// Internal: the public surface exposes <see cref="ReadChangelog" /> and the copy methods, so no caller can hold
  /// the native stream after the package or its session is released. The returned stream owns a native archive handle.
  /// Dispose it before the package or its session is released: the stream has no finalizer by design, because closing the
  /// handle after <c>alpm_release</c> would touch a freed handle, and libalpm is not thread-safe. A stream that is never
  /// disposed leaks the archive handle until the process exits.
  /// </remarks>
  internal Stream? OpenChangelogStream()
  {
    var fp = NativeMethods.alpm_pkg_changelog_open(BackingStruct);
    if (fp == null) return null;
    return new PackageChangelogStream(this, fp);
  }

  /// <summary>Reads the complete changelog text as a UTF-8 string, or returns <c>null</c> if none is available.</summary>
  /// <remarks>
  /// The native changelog is decoded from UTF-8 into a .NET string. To copy the original bytes without the UTF-8 to
  /// UTF-16 conversion, use <see cref="TryCopyChangelogTo" /> or <see cref="CopyChangelogTo" />.
  /// </remarks>
  public string? ReadChangelog()
  {
    using var stream = OpenChangelogStream();
    if (stream is null) return null;

    using var reader = new StreamReader(stream, Encoding.UTF8);
    return reader.ReadToEnd();
  }

  /// <summary>
  /// Copies the package changelog's raw UTF-8 bytes to <paramref name="destination" />, or returns <c>false</c> if
  /// the package has no changelog.
  /// </summary>
  /// <param name="destination">The stream that receives the bytes. It is not disposed.</param>
  /// <returns><c>true</c> if the changelog was copied; <c>false</c> if the package carries no changelog.</returns>
  /// <remarks>
  /// The bytes are copied without a UTF-8 to UTF-16 to UTF-8 round trip, so the destination receives exactly what
  /// libalpm stores. The native stream is disposed before this method returns. A <c>false</c> result means only that the
  /// package has no changelog.
  /// </remarks>
  /// <exception cref="ArgumentNullException"><paramref name="destination" /> is <c>null</c>.</exception>
  /// <exception cref="AlpmException">The changelog could not be opened.</exception>
  /// <exception cref="AlpmLifetimeException">The package's session has already been released.</exception>
  public bool TryCopyChangelogTo(Stream destination)
  {
    ArgumentNullException.ThrowIfNull(destination);
    using var stream = OpenChangelogStream();
    if (stream is null) return false;

    stream.CopyTo(destination);
    return true;
  }

  /// <summary>Copies the package changelog's raw UTF-8 bytes to <paramref name="destination" />.</summary>
  /// <param name="destination">The stream that receives the bytes. It is not disposed.</param>
  /// <remarks>
  /// Use <see cref="TryCopyChangelogTo" /> when the package may have no changelog. The bytes are copied without a
  /// UTF-8 to UTF-16 to UTF-8 round trip. The native stream is disposed before this method returns.
  /// </remarks>
  /// <exception cref="ArgumentNullException"><paramref name="destination" /> is <c>null</c>.</exception>
  /// <exception cref="InvalidOperationException">The package carries no changelog.</exception>
  /// <exception cref="AlpmException">The changelog could not be opened.</exception>
  /// <exception cref="AlpmLifetimeException">The package's session has already been released.</exception>
  public void CopyChangelogTo(Stream destination)
  {
    if (!TryCopyChangelogTo(destination))
    {
      throw new InvalidOperationException("The package does not carry a changelog.");
    }
  }

  /// <summary>
  /// Reads the package's mtree manifest (<c>.MTREE</c>) into a detached list, or returns <c>null</c> if the package
  /// has no mtree file (such as for sync database packages).
  /// </summary>
  /// <remarks>
  /// Only packages from the local database carry an mtree manifest. The returned list is a managed snapshot: it
  /// stays valid after the package or its session is released and can be enumerated repeatedly. Reading the mtree manifest
  /// is a low-frequency operation, so the entries are materialized rather than exposed through a live native reader.
  /// </remarks>
  /// <exception cref="AlpmNativeFailureException">
  /// libalpm reported a read failure without setting an error code, e.g. a
  /// malformed entry.
  /// </exception>
  /// <exception cref="AlpmException">The mtree entries could not be read.</exception>
  /// <exception cref="AlpmLifetimeException">The package's session has already been released.</exception>
  public IReadOnlyList<MtreeEntry>? GetMtreeEntries()
  {
    if (!TryGetMtree(out var mtree, out var failure))
    {
      throw failure.ToException();
    }
    if (mtree is null) return null;

    using var reader = mtree;
    var entries = new List<MtreeEntry>();
    while (reader.MoveNext())
    {
      entries.Add(reader.Current);
    }
    return entries;
  }

  [DoesNotReturn]
  private static void ThrowSignatureFailure(int rawErrno)
  {
    throw AlpmFailure.Of(rawErrno, "read package signature").ToException();
  }
}
