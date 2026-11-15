using System.Diagnostics.CodeAnalysis;
using Orson.Alpm.Bindings;

namespace Orson.Alpm;

/// <summary>
/// A package loaded from an archive file. It owns the unmanaged package memory and releases it on
/// <see cref="Dispose" />, or when the owning <see cref="AlpmSession" /> is disposed.
/// </summary>
public sealed unsafe class LoadedPackage : IDisposable, IPackageMetadata
{
  private readonly LoadedPackageRegistry _registry;
  private readonly ChildLifetime _lifetime;
  private nint _rawPackage;

  internal LoadedPackage(LoadedPackageRegistry registry, _alpm_pkg_t* backingStruct, ChildLifetime lifetime)
  {
    _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    _rawPackage = (nint)backingStruct;
    _lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
    View = new PackageView(backingStruct, lifetime);
  }

  /// <summary>Gets a live, borrowed <see cref="PackageView" /> of this package.</summary>
  public PackageView View { get; }

  /// <summary>Implicitly converts a <see cref="LoadedPackage" /> to a <see cref="PackageView" />.</summary>
  public static implicit operator PackageView(LoadedPackage pkg)
  {
    ArgumentNullException.ThrowIfNull(pkg);
    return pkg.View;
  }

  /// <summary>Whether this instance was disposed or handed over.</summary>
  private bool _disposed;

  /// <summary>Whether this instance still owns the package (it stops owning it on dispose or hand-over).</summary>
  internal bool OwnsPackage => !_disposed && _rawPackage != 0;

  /// <summary>Throws when the package was already released or handed to a transaction.</summary>
  internal void ThrowIfNotOwned()
  {
    ObjectDisposedException.ThrowIf(!OwnsPackage, this);
  }

  internal _alpm_pkg_t* BackingStruct
  {
    get
    {
      ThrowIfNotOwned();
      return (_alpm_pkg_t*)_rawPackage;
    }
  }

  /// <summary>
  /// Gives up ownership <b>without</b> releasing the package, because somebody else owns the pointer now and
  /// releases it. <see cref="Dispose" /> becomes a no-op.
  /// </summary>
  internal void Disown()
  {
    ThrowIfNotOwned();
    var pkg = (_alpm_pkg_t*)Interlocked.Exchange(ref _rawPackage, 0);
    if (pkg == null) return;
    _disposed = true;
    _registry.Unregister(pkg, freeNative: false);
    _lifetime.Invalidate("the hand-over to a transaction");
  }

  /// <summary>Releases the unmanaged resources used by the <see cref="LoadedPackage" />.</summary>
  public void Dispose()
  {
    if (_disposed) return;
    _disposed = true;
    var pkg = (_alpm_pkg_t*)Interlocked.Exchange(ref _rawPackage, 0);
    if (pkg == null) return;
    _lifetime.Invalidate("LoadedPackage.Dispose()");
    _registry.Unregister(pkg, freeNative: true);
  }

  // IPackageMetadata delegation
  /// <inheritdoc />
  public string Name { get { ThrowIfNotOwned(); return View.Name; } }
  /// <inheritdoc />
  public PackageVersion Version { get { ThrowIfNotOwned(); return View.Version; } }
  /// <inheritdoc />
  public string? Filename { get { ThrowIfNotOwned(); return View.Filename; } }
  /// <inheritdoc />
  public string? Base { get { ThrowIfNotOwned(); return View.Base; } }
  /// <inheritdoc />
  public string? Description { get { ThrowIfNotOwned(); return View.Description; } }
  /// <inheritdoc />
  public string? Url { get { ThrowIfNotOwned(); return View.Url; } }
  /// <inheritdoc />
  public DateTimeOffset BuildDate { get { ThrowIfNotOwned(); return View.BuildDate; } }
  /// <inheritdoc />
  public DateTimeOffset? InstallDate { get { ThrowIfNotOwned(); return View.InstallDate; } }
  /// <inheritdoc />
  public string? Packager { get { ThrowIfNotOwned(); return View.Packager; } }
  /// <inheritdoc />
  public string? Md5Sum { get { ThrowIfNotOwned(); return View.Md5Sum; } }
  /// <inheritdoc />
  public string? Sha256Sum { get { ThrowIfNotOwned(); return View.Sha256Sum; } }
  /// <inheritdoc />
  public string? Architecture { get { ThrowIfNotOwned(); return View.Architecture; } }
  /// <inheritdoc />
  public long Size { get { ThrowIfNotOwned(); return View.Size; } }
  /// <inheritdoc />
  public long InstalledSize { get { ThrowIfNotOwned(); return View.InstalledSize; } }
  /// <inheritdoc />
  public long DownloadSize { get { ThrowIfNotOwned(); return View.DownloadSize; } }
  /// <inheritdoc />
  public PackageOrigin Origin { get { ThrowIfNotOwned(); return View.Origin; } }
  /// <inheritdoc />
  public PackageReason Reason { get { ThrowIfNotOwned(); return View.Reason; } }
  /// <inheritdoc />
  public PackageValidation Validation { get { ThrowIfNotOwned(); return View.Validation; } }
  /// <inheritdoc />
  public IReadOnlyCollection<string> Licenses { get { ThrowIfNotOwned(); return View.Licenses; } }
  /// <inheritdoc />
  public IReadOnlyCollection<string> Groups { get { ThrowIfNotOwned(); return View.Groups; } }
  /// <inheritdoc />
  public IReadOnlyCollection<Dependency> Depends { get { ThrowIfNotOwned(); return View.Depends; } }
  /// <inheritdoc />
  public IReadOnlyCollection<Dependency> OptionalDepends { get { ThrowIfNotOwned(); return View.OptionalDepends; } }
  /// <inheritdoc />
  public IReadOnlyCollection<Dependency> CheckDepends { get { ThrowIfNotOwned(); return View.CheckDepends; } }
  /// <inheritdoc />
  public IReadOnlyCollection<Dependency> MakeDepends { get { ThrowIfNotOwned(); return View.MakeDepends; } }
  /// <inheritdoc />
  public IReadOnlyCollection<Dependency> Conflicts { get { ThrowIfNotOwned(); return View.Conflicts; } }
  /// <inheritdoc />
  public IReadOnlyCollection<Dependency> Provides { get { ThrowIfNotOwned(); return View.Provides; } }
  /// <inheritdoc />
  public IReadOnlyCollection<Dependency> Replaces { get { ThrowIfNotOwned(); return View.Replaces; } }
  /// <inheritdoc />
  public IReadOnlyCollection<PackageFile>? Files { get { ThrowIfNotOwned(); return View.Files; } }
  /// <inheritdoc />
  public IReadOnlyCollection<BackupEntry>? BackupFiles { get { ThrowIfNotOwned(); return View.BackupFiles; } }
  /// <inheritdoc />
  public bool HasScriptlet { get { ThrowIfNotOwned(); return View.HasScriptlet; } }
  /// <inheritdoc />
  public string? Base64Signature { get { ThrowIfNotOwned(); return View.Base64Signature; } }

  // Strongly typed view collections & inspections
  /// <summary>The files contained in this package.</summary>
  public FileList PackageFiles { get { ThrowIfNotOwned(); return View.Files; } }
  /// <summary>The backup entries contained in this package.</summary>
  public AlpmList<BackupEntry> PackageBackupFiles { get { ThrowIfNotOwned(); return View.BackupFiles; } }
  /// <summary>The licenses declared by this package.</summary>
  public AlpmStringList PackageLicenses { get { ThrowIfNotOwned(); return View.Licenses; } }
  /// <summary>The groups this package belongs to.</summary>
  public AlpmStringList PackageGroups { get { ThrowIfNotOwned(); return View.Groups; } }

  /// <summary>Checks whether the MD5 checksum matches.</summary>
  public bool CheckMd5Sum() { ThrowIfNotOwned(); return View.CheckMd5Sum(); }
  internal bool TryCheckMd5Sum(out bool matches, [NotNullWhen(false)] out AlpmFailure? failure)
  {
    ThrowIfNotOwned();
    return View.TryCheckMd5Sum(out matches, out failure);
  }

  /// <summary>Whether this package is ignored by configuration.</summary>
  public bool ShouldIgnore() { ThrowIfNotOwned(); return View.ShouldIgnore(); }
  /// <summary>Retrieves the raw signature bytes if present.</summary>
  public ReadOnlyMemory<byte>? GetSignature() { ThrowIfNotOwned(); return View.GetSignature(); }
  /// <summary>Retrieves packages requiring this package.</summary>
  public IReadOnlyList<string> GetRequiredBy() { ThrowIfNotOwned(); return View.GetRequiredBy(); }
  /// <summary>Retrieves packages for which this package is optional.</summary>
  public IReadOnlyList<string> GetOptionalFor() { ThrowIfNotOwned(); return View.GetOptionalFor(); }
  /// <summary>Creates a snapshot of this package's metadata.</summary>
  public PackageSnapshot ToSnapshot(bool includeFiles = false) { ThrowIfNotOwned(); return View.ToSnapshot(includeFiles); }
  /// <summary>Reads the full changelog text.</summary>
  public string? ReadChangelog() { ThrowIfNotOwned(); return View.ReadChangelog(); }
  /// <summary>Tries copying changelog bytes to destination.</summary>
  public bool TryCopyChangelogTo(Stream destination) { ThrowIfNotOwned(); return View.TryCopyChangelogTo(destination); }
  /// <summary>Copies changelog bytes to destination stream.</summary>
  public void CopyChangelogTo(Stream destination) { ThrowIfNotOwned(); View.CopyChangelogTo(destination); }
  internal Stream? OpenChangelogStream() { ThrowIfNotOwned(); return View.OpenChangelogStream(); }
  /// <summary>Reads mtree entries from the package archive.</summary>
  public IReadOnlyList<MtreeEntry>? GetMtreeEntries() { ThrowIfNotOwned(); return View.GetMtreeEntries(); }
}
