using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Orson.Alpm.Bindings;
using Orson.Alpm.Interop;

namespace Orson.Alpm;

/// <summary>
/// A package libalpm owns: a database package, a transaction member, or one reached through a group.
/// A 40-byte non-allocating value type directly borrowing unmanaged memory.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly unsafe partial struct PackageView : IPackageMetadata, IEquatable<PackageView>, INativeWrapper
{
  private readonly _alpm_pkg_t* _rawPackage;
  private readonly LifetimeStamp _stamp;

  unsafe void* INativeWrapper.BackingStruct
  {
    get
    {
      if (this == default)
      {
        throw new ArgumentException("The collection contains an uninitialized package view.");
      }

      return BackingStruct;
    }
  }

  internal PackageView(_alpm_pkg_t* backingStruct, ChildLifetime lifetime)
  {
    ArgumentNullException.ThrowIfNull(lifetime);
    _rawPackage = backingStruct;
    _stamp = lifetime.Capture();
  }

  internal static PackageView Factory(void* ptr, Lifetime lifetime)
  {
    if (lifetime is not ChildLifetime child)
    {
      throw new InvalidOperationException("PackageView cannot be anchored directly to a root lifetime domain; a child domain is required.");
    }

    return new PackageView((_alpm_pkg_t*)ptr, child);
  }

  internal _alpm_pkg_t* BackingStruct
  {
    get
    {
      ThrowIfStale();
      return _rawPackage;
    }
  }

  internal Lifetime LifetimeDomain => _stamp.Domain;

  internal _alpm_handle_t* LibraryHandle
  {
    get
    {
      var handle = NativeMethods.alpm_pkg_get_handle(BackingStruct);
      GC.KeepAlive(_stamp.Domain);
      return handle;
    }
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private void ThrowIfStale()
  {
    if (_rawPackage == null)
    {
      throw new InvalidOperationException("The PackageView is uninitialized.");
    }

    _stamp.ThrowIfStale();
  }

  /// <summary>Gets the raw UTF-8 bytes of the package name directly from native memory.</summary>
  public ReadOnlySpan<byte> NameUtf8
  {
    get
    {
      ThrowIfStale();
      var ptr = NativeMethods.alpm_pkg_get_name(_rawPackage);
      var span = ptr == null ? default : MemoryMarshal.CreateReadOnlySpanFromNullTerminated(ptr);
      GC.KeepAlive(_stamp.Domain);
      return span;
    }
  }

  /// <summary>Gets the raw UTF-8 bytes of the package version directly from native memory.</summary>
  public ReadOnlySpan<byte> VersionUtf8
  {
    get
    {
      ThrowIfStale();
      var ptr = NativeMethods.alpm_pkg_get_version(_rawPackage);
      var span = ptr == null ? default : MemoryMarshal.CreateReadOnlySpanFromNullTerminated(ptr);
      GC.KeepAlive(_stamp.Domain);
      return span;
    }
  }

  /// <summary>Gets the raw UTF-8 bytes of the package description directly from native memory.</summary>
  public ReadOnlySpan<byte> DescriptionUtf8
  {
    get
    {
      ThrowIfStale();
      var ptr = NativeMethods.alpm_pkg_get_desc(_rawPackage);
      var span = ptr == null ? default : MemoryMarshal.CreateReadOnlySpanFromNullTerminated(ptr);
      GC.KeepAlive(_stamp.Domain);
      return span;
    }
  }

  /// <summary>The package name.</summary>
  public string Name
  {
    get
    {
      var name = NativeString.FromNative((nint)NativeMethods.alpm_pkg_get_name(BackingStruct))!;
      GC.KeepAlive(_stamp.Domain);
      return name;
    }
  }

  /// <summary>The version of the package.</summary>
  public PackageVersion Version
  {
    get
    {
      var ver = new PackageVersion(NativeMethods.alpm_pkg_get_version(BackingStruct));
      GC.KeepAlive(_stamp.Domain);
      return ver;
    }
  }

  /// <summary>The archive filename of the package, or <c>null</c> if not applicable.</summary>
  public string? Filename
  {
    get
    {
      var filename = NativeString.FromNative((nint)NativeMethods.alpm_pkg_get_filename(BackingStruct));
      GC.KeepAlive(_stamp.Domain);
      return filename;
    }
  }

  /// <summary>The base package name (pkgbase) if part of a split package, or <c>null</c> if none.</summary>
  public string? Base
  {
    get
    {
      var b = NativeString.FromNative((nint)NativeMethods.alpm_pkg_get_base(BackingStruct));
      GC.KeepAlive(_stamp.Domain);
      return b;
    }
  }

  /// <summary>The description of the package.</summary>
  public string? Description
  {
    get
    {
      var desc = NativeString.FromNative((nint)NativeMethods.alpm_pkg_get_desc(BackingStruct));
      GC.KeepAlive(_stamp.Domain);
      return desc;
    }
  }

  /// <summary>The upstream project website or homepage URL.</summary>
  public string? Url
  {
    get
    {
      var url = NativeString.FromNative((nint)NativeMethods.alpm_pkg_get_url(BackingStruct));
      GC.KeepAlive(_stamp.Domain);
      return url;
    }
  }

  /// <summary>The date and time when the package was built.</summary>
  public DateTimeOffset BuildDate
  {
    get
    {
      var date = DateTimeOffset.FromUnixTimeSeconds(NativeMethods.alpm_pkg_get_builddate(BackingStruct));
      GC.KeepAlive(_stamp.Domain);
      return date;
    }
  }

  /// <summary>The date and time when the package was installed locally, or <c>null</c> if not installed.</summary>
  public DateTimeOffset? InstallDate
  {
    get
    {
      var date = NativeMethods.alpm_pkg_get_installdate(BackingStruct);
      GC.KeepAlive(_stamp.Domain);
      return date == 0 ? null : DateTimeOffset.FromUnixTimeSeconds(date);
    }
  }

  /// <summary>The name and contact information of the packager.</summary>
  public string? Packager
  {
    get
    {
      var packager = NativeString.FromNative((nint)NativeMethods.alpm_pkg_get_packager(BackingStruct));
      GC.KeepAlive(_stamp.Domain);
      return packager;
    }
  }

  /// <summary>The MD5 checksum of the package archive, or <c>null</c> if not available.</summary>
  public string? Md5Sum
  {
    get
    {
      var md5 = NativeString.FromNative((nint)NativeMethods.alpm_pkg_get_md5sum(BackingStruct));
      GC.KeepAlive(_stamp.Domain);
      return md5;
    }
  }

  /// <summary>The SHA-256 checksum of the package archive, or <c>null</c> if not available.</summary>
  public string? Sha256Sum
  {
    get
    {
      var sha = NativeString.FromNative((nint)NativeMethods.alpm_pkg_get_sha256sum(BackingStruct));
      GC.KeepAlive(_stamp.Domain);
      return sha;
    }
  }

  /// <summary>The target CPU architecture for this package (e.g. "x86_64", "any").</summary>
  public string? Architecture
  {
    get
    {
      var arch = NativeString.FromNative((nint)NativeMethods.alpm_pkg_get_arch(BackingStruct));
      GC.KeepAlive(_stamp.Domain);
      return arch;
    }
  }

  /// <summary>The download size of the package archive in bytes.</summary>
  public long Size
  {
    get
    {
      var size = NativeMethods.alpm_pkg_get_size(BackingStruct);
      GC.KeepAlive(_stamp.Domain);
      return size.Value;
    }
  }

  /// <summary>The unpacked size of the package when installed on disk, in bytes.</summary>
  public long InstalledSize
  {
    get
    {
      var isize = NativeMethods.alpm_pkg_get_isize(BackingStruct);
      GC.KeepAlive(_stamp.Domain);
      return isize.Value;
    }
  }

  /// <summary>The estimated or calculated download size of the package in bytes.</summary>
  public long DownloadSize
  {
    get
    {
      var dsize = NativeMethods.alpm_pkg_download_size(BackingStruct);
      GC.KeepAlive(_stamp.Domain);
      return dsize.Value;
    }
  }

  /// <summary>The origin indicating where this package was loaded from.</summary>
  public PackageOrigin Origin
  {
    get
    {
      var origin = (PackageOrigin)(uint)NativeMethods.alpm_pkg_get_origin(BackingStruct);
      GC.KeepAlive(_stamp.Domain);
      return origin;
    }
  }

  /// <summary>The reason why this package is installed.</summary>
  public PackageReason Reason
  {
    get
    {
      var reason = (PackageReason)(uint)NativeMethods.alpm_pkg_get_reason(BackingStruct);
      GC.KeepAlive(_stamp.Domain);
      return reason;
    }
  }

  /// <summary>Gets or sets why this package was installed on the system.</summary>
  public PackageReason InstallReason
  {
    get => Reason;
    set => SetReason(value);
  }

  /// <summary>The validation methods used to verify this package.</summary>
  public PackageValidation Validation
  {
    get
    {
      var val = (PackageValidation)NativeMethods.alpm_pkg_get_validation(BackingStruct);
      GC.KeepAlive(_stamp.Domain);
      return val;
    }
  }

  /// <summary>The licenses governing the distribution and use of this package.</summary>
  public AlpmStringList Licenses => new(NativeMethods.alpm_pkg_get_licenses(BackingStruct), _stamp.Domain);

  /// <summary>The package groups that this package belongs to.</summary>
  public AlpmStringList Groups => new(NativeMethods.alpm_pkg_get_groups(BackingStruct), _stamp.Domain);

  /// <summary>The list of packages required to run this package.</summary>
  public AlpmList<Dependency> Depends => Dependency.ListFactory(NativeMethods.alpm_pkg_get_depends(BackingStruct), _stamp.Domain);

  /// <summary>The list of optional packages that provide additional functionality.</summary>
  public AlpmList<Dependency> OptionalDepends => Dependency.ListFactory(NativeMethods.alpm_pkg_get_optdepends(BackingStruct), _stamp.Domain);

  /// <summary>The list of dependencies required only to run the test suite when building.</summary>
  public AlpmList<Dependency> CheckDepends => Dependency.ListFactory(NativeMethods.alpm_pkg_get_checkdepends(BackingStruct), _stamp.Domain);

  /// <summary>The list of dependencies required only to build the package from source.</summary>
  public AlpmList<Dependency> MakeDepends => Dependency.ListFactory(NativeMethods.alpm_pkg_get_makedepends(BackingStruct), _stamp.Domain);

  /// <summary>The list of packages that conflict with this package.</summary>
  public AlpmList<Dependency> Conflicts => Dependency.ListFactory(NativeMethods.alpm_pkg_get_conflicts(BackingStruct), _stamp.Domain);

  /// <summary>The virtual provisions or features provided by this package.</summary>
  public AlpmList<Dependency> Provides => Dependency.ListFactory(NativeMethods.alpm_pkg_get_provides(BackingStruct), _stamp.Domain);

  /// <summary>The list of packages that this package replaces.</summary>
  public AlpmList<Dependency> Replaces => Dependency.ListFactory(NativeMethods.alpm_pkg_get_replaces(BackingStruct), _stamp.Domain);

  /// <summary>The package's file list, read on demand.</summary>
  public FileList Files => new(NativeMethods.alpm_pkg_get_files(BackingStruct), _stamp.Domain);

  /// <summary>The list of configuration files marked for backup.</summary>
  public AlpmList<BackupEntry> BackupFiles => BackupEntry.ListFactory(NativeMethods.alpm_pkg_get_backup(BackingStruct), _stamp.Domain);

  /// <summary>Gets the base64-encoded PGP signature of the package, or <c>null</c> if none is present.</summary>
  public string? Base64Signature
  {
    get
    {
      var sig = NativeString.FromNative((nint)NativeMethods.alpm_pkg_get_base64_sig(BackingStruct));
      GC.KeepAlive(_stamp.Domain);
      return sig;
    }
  }

  /// <summary>Gets a value indicating whether the package contains an install or upgrade scriptlet.</summary>
  public bool HasScriptlet
  {
    get
    {
      var has = NativeMethods.alpm_pkg_has_scriptlet(BackingStruct) != 0;
      GC.KeepAlive(_stamp.Domain);
      return has;
    }
  }

  // IPackageMetadata explicit adaptations
  IReadOnlyCollection<string> IPackageMetadata.Licenses => Licenses;
  IReadOnlyCollection<string> IPackageMetadata.Groups => Groups;
  IReadOnlyCollection<Dependency> IPackageMetadata.Depends => Depends;
  IReadOnlyCollection<Dependency> IPackageMetadata.OptionalDepends => OptionalDepends;
  IReadOnlyCollection<Dependency> IPackageMetadata.CheckDepends => CheckDepends;
  IReadOnlyCollection<Dependency> IPackageMetadata.MakeDepends => MakeDepends;
  IReadOnlyCollection<Dependency> IPackageMetadata.Conflicts => Conflicts;
  IReadOnlyCollection<Dependency> IPackageMetadata.Provides => Provides;
  IReadOnlyCollection<Dependency> IPackageMetadata.Replaces => Replaces;
  IReadOnlyCollection<PackageFile>? IPackageMetadata.Files => Files;
  IReadOnlyCollection<BackupEntry>? IPackageMetadata.BackupFiles => BackupFiles;

  /// <summary>Sets the install reason for this package in the local database.</summary>
  public void SetReason(PackageReason reason)
  {
    if (!TrySetReason(reason, out var failure))
    {
      throw failure.ToException();
    }
  }

  /// <summary>Checks whether the package's MD5 checksum matches the expected value.</summary>
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
    GC.KeepAlive(_stamp.Domain);
    return ignore;
  }

  /// <summary>Gets the package's embedded binary PGP signature, or <c>null</c> if none is present.</summary>
  public ReadOnlyMemory<byte>? GetSignature()
  {
    byte* buffer = null;
    nuint len;
    var result = NativeMethods.alpm_pkg_get_sig(BackingStruct, &buffer, &len);
    if (result != 0)
    {
      var rawErrno = (int)NativeMethods.alpm_errno(LibraryHandle);
      GC.KeepAlive(_stamp.Domain);
      throw AlpmFailure.Of(rawErrno, "read package signature").ToException();
    }

    if (buffer != null)
    {
      var bytes = new Span<byte>(buffer, (int)len).ToArray();
      UnmanagedDestructors.CFree(buffer);
      GC.KeepAlive(_stamp.Domain);
      return new ReadOnlyMemory<byte>(bytes);
    }

    GC.KeepAlive(_stamp.Domain);
    return null;
  }

  /// <summary>Gets the names of packages that depend on this package.</summary>
  public IReadOnlyList<string> GetRequiredBy()
  {
    var list = AlpmStringList.TakeOwned(NativeMethods.alpm_pkg_compute_requiredby(BackingStruct),
      &UnmanagedDestructors.CFreeExtern);
    GC.KeepAlive(_stamp.Domain);
    return list;
  }

  /// <summary>Gets the names of packages that optionally depend on this package.</summary>
  public IReadOnlyList<string> GetOptionalFor()
  {
    var list = AlpmStringList.TakeOwned(NativeMethods.alpm_pkg_compute_optionalfor(BackingStruct),
      &UnmanagedDestructors.CFreeExtern);
    GC.KeepAlive(_stamp.Domain);
    return list;
  }

  /// <summary>Copies everything this package reads into an independent <see cref="PackageSnapshot" />.</summary>
  public PackageSnapshot ToSnapshot(bool includeFiles = false) => new(this, includeFiles);

  internal Stream? OpenChangelogStream()
  {
    var fp = NativeMethods.alpm_pkg_changelog_open(BackingStruct);
    return fp != null ? new PackageChangelogStream(this, fp) : null;
  }

  /// <summary>Reads the complete changelog text as a UTF-8 string, or returns <c>null</c> if none is available.</summary>
  public string? ReadChangelog()
  {
    using var stream = OpenChangelogStream();
    if (stream is null) return null;

    using var reader = new StreamReader(stream, Encoding.UTF8);
    return reader.ReadToEnd();
  }

  /// <summary>Copies the package changelog's raw UTF-8 bytes to <paramref name="destination" />.</summary>
  public bool TryCopyChangelogTo(Stream destination)
  {
    ArgumentNullException.ThrowIfNull(destination);
    using var stream = OpenChangelogStream();
    if (stream is null) return false;

    stream.CopyTo(destination);
    return true;
  }

  /// <summary>Copies the package changelog's raw UTF-8 bytes to <paramref name="destination" />.</summary>
  public void CopyChangelogTo(Stream destination)
  {
    if (!TryCopyChangelogTo(destination))
    {
      throw new InvalidOperationException("The package does not carry a changelog.");
    }
  }

  /// <summary>Reads the package's mtree manifest (<c>.MTREE</c>) into a detached list.</summary>
  public IReadOnlyList<MtreeEntry>? GetMtreeEntries()
  {
    PackageMtreeReader? mtree = null;
    try
    {
      if (!TryGetMtree(out mtree, out var failure))
      {
        throw failure.ToException();
      }
      if (mtree is null) return null;

      var entries = new List<MtreeEntry>();
      while (mtree.MoveNext())
      {
        entries.Add(mtree.Current);
      }
      return entries;
    }
    finally
    {
      mtree?.Dispose();
    }
  }

  /// <summary>Finds a package satisfying the specified dependency in a collection of packages.</summary>
  public static PackageView? FindSatisfier(IEnumerable<PackageView> packages, string dependency)
  {
    ArgumentNullException.ThrowIfNull(packages);
    ArgumentNullException.ThrowIfNull(dependency);

    Span<byte> depBytes = stackalloc byte[64];
    using var depBuf = new Utf8Buffer(dependency, depBytes);
    Span<_alpm_list_t> scratch = stackalloc _alpm_list_t[32];
    using var builder = new TransientAlpmListBuilder(scratch);
    var nativeList = builder.ConstructNative(packages);
    var matchedPtr = NativeMethods.alpm_find_satisfier(nativeList, depBuf.Ptr);
    if (matchedPtr == null) return null;

    foreach (var pkg in packages)
    {
      if (pkg.BackingStruct == matchedPtr)
      {
        return pkg;
      }
    }

    throw new InvalidOperationException(
      "libalpm returned a package that is not among the ones passed to the call; " +
      "there is no domain to anchor the resulting view to.");
  }

  /// <summary>Checks whether a newer version of this package is available in the given synchronization databases.</summary>
  public PackageView? GetNewVersion(IEnumerable<Database> syncDatabases)
  {
    ArgumentNullException.ThrowIfNull(syncDatabases);
    ThrowIfStale();

    Span<_alpm_list_t> scratch = stackalloc _alpm_list_t[8];
    using var builder = new TransientAlpmListBuilder(scratch);
    var dbsList = builder.ConstructNative(syncDatabases);
    var newPkgPtr = NativeMethods.alpm_sync_get_new_version(BackingStruct, dbsList);
    if (newPkgPtr == null) return null;

    var dbPtr = NativeMethods.alpm_pkg_get_db(newPkgPtr);
    return new PackageView(newPkgPtr, Database.ResolveLifetime(syncDatabases, dbPtr));
  }

  /// <inheritdoc />
  public bool Equals(PackageView other) => _rawPackage == other._rawPackage;

  /// <inheritdoc />
  public override bool Equals(object? obj) => obj is PackageView other && Equals(other);

  /// <inheritdoc />
  public override int GetHashCode() => unchecked((int)(nint)_rawPackage);

  /// <summary>Whether the two package views point to the same native package.</summary>
  public static bool operator ==(PackageView left, PackageView right) => left.Equals(right);

  /// <summary>Whether the two package views point to different native packages.</summary>
  public static bool operator !=(PackageView left, PackageView right) => !left.Equals(right);

  /// <inheritdoc />
  public override string ToString()
  {
    return this == default ? string.Empty : Name;
  }
}
