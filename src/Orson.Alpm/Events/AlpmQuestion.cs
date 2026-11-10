using System.Diagnostics;
using JetBrains.Annotations;
using Orson.Alpm.Bindings;

namespace Orson.Alpm;

/// <summary>
/// Question asked when attempting to install a package marked in IgnorePkg.
/// </summary>
[PublicAPI]
public unsafe ref struct InstallIgnorePackageQuestion
{
  private readonly _alpm_question_t* _raw;
  private readonly ChildLifetime _packageLifetime;

  /// <summary>
  /// The name of the package being installed.
  /// </summary>
  public string? PackageName { get; }

  /// <summary>
  /// The version of the package being installed.
  /// </summary>
  public string? PackageVersion { get; }

  /// <summary>
  /// The <see cref="PackageView"/> of the package being installed.
  /// </summary>
  public PackageView Package => field ??= new PackageView(_raw->install_ignorepkg.pkg, _packageLifetime);

  /// <summary>
  /// The answer of whether to install this package.
  /// </summary>
  public readonly bool Install
  {
    get => _raw->install_ignorepkg.install != 0;
    set => _raw->install_ignorepkg.install = value ? 1 : 0;
  }

  /// <summary>
  /// This should NEVER be called.
  /// </summary>
  /// <exception cref="InvalidOperationException"></exception>
  [Obsolete("Questions structs should not be constructed by consumers.", error: true)]
  public InstallIgnorePackageQuestion() =>
    throw new InvalidOperationException("Questions structs should not be constructed by consumers.");

  internal InstallIgnorePackageQuestion(_alpm_question_t* question, ChildLifetime packageLifetime)
  {
    Debug.Assert(question->type_ == _alpm_question_type_t.ALPM_QUESTION_INSTALL_IGNOREPKG);
    _raw = question;
    _packageLifetime = packageLifetime;

    PackageName = NativeString.FromNative((nint)NativeMethods.alpm_pkg_get_name(question->install_ignorepkg.pkg));
    PackageVersion = NativeString.FromNative((nint)NativeMethods.alpm_pkg_get_version(question->install_ignorepkg.pkg));
  }
}

/// <summary>
/// Question asked when an existing package is to be replaced by another package.
/// </summary>
[PublicAPI]
public unsafe ref struct ReplacePackageQuestion
{
  private readonly _alpm_question_t* _raw;
  private readonly ChildLifetime _oldPackageLifetime;
  private readonly ChildLifetime _newPackageLifetime;

  /// <summary>The name of the package being replaced.</summary>
  public string? OldPackageName { get; }

  /// <summary>The version of the package being replaced.</summary>
  public string? OldPackageVersion { get; }

  /// <summary>The name of the replacement package.</summary>
  public string? NewPackageName { get; }

  /// <summary>The version of the replacement package.</summary>
  public string? NewPackageVersion { get; }

  /// <summary>The name of the database providing the replacement package.</summary>
  /// <remarks>
  /// libalpm only provides the database pointer during this callback to identify which repository
  /// is offering the replacement (e.g. for prompt formatting like <c>Replace old with repo/new?</c>).
  /// For simplicity, we just expose the name of it.
  /// To inspect the full database, query the owning <see cref="AlpmSession.GetSyncDatabases"/> by name.
  /// </remarks>
  public string? NewPackageDatabase { get; }

  /// <summary>The existing package to be replaced as a borrowed view.</summary>
  public PackageView OldPackage => field ??= new PackageView(_raw->replace.oldpkg, _oldPackageLifetime);

  /// <summary>The replacement package as a borrowed view.</summary>
  public PackageView NewPackage => field ??= new PackageView(_raw->replace.newpkg, _newPackageLifetime);

  /// <summary>Gets or sets whether the package should be replaced.</summary>
  public readonly bool Replace
  {
    get => _raw->replace.replace != 0;
    set => _raw->replace.replace = value ? 1 : 0;
  }

  /// <summary>
  /// This should NEVER be called.
  /// </summary>
  /// <exception cref="InvalidOperationException"></exception>
  [Obsolete("Question structs should not be constructed by consumers.", error: true)]
  public ReplacePackageQuestion() =>
    throw new InvalidOperationException("Question structs should not be constructed by consumers.");

  internal ReplacePackageQuestion(_alpm_question_t* question, ChildLifetime oldPackageLifetime,
    ChildLifetime newPackageLifetime)
  {
    Debug.Assert(question->type_ == _alpm_question_type_t.ALPM_QUESTION_REPLACE_PKG);
    _raw = question;
    _oldPackageLifetime = oldPackageLifetime;
    _newPackageLifetime = newPackageLifetime;

    OldPackageName = NativeString.FromNative((nint)NativeMethods.alpm_pkg_get_name(question->replace.oldpkg));
    OldPackageVersion = NativeString.FromNative((nint)NativeMethods.alpm_pkg_get_version(question->replace.oldpkg));

    NewPackageName = NativeString.FromNative((nint)NativeMethods.alpm_pkg_get_name(question->replace.newpkg));
    NewPackageVersion = NativeString.FromNative((nint)NativeMethods.alpm_pkg_get_version(question->replace.newpkg));

    NewPackageDatabase = NativeString.FromNative((nint)NativeMethods.alpm_db_get_name(question->replace.newdb));
  }
}

/// <summary>
/// Question asked when two packages conflict during transaction preparation.
/// </summary>
[PublicAPI]
public unsafe ref struct PackageConflictQuestion
{
  private readonly _alpm_question_t* _raw;
  private readonly ChildLifetime _package1Lifetime;
  private readonly ChildLifetime _package2Lifetime;

  /// <summary>The name of the first conflicting package.</summary>
  public string? Package1Name { get; }

  /// <summary>The version of the first conflicting package.</summary>
  public string? Package1Version { get; }

  /// <summary>The name of the second conflicting package.</summary>
  public string? Package2Name { get; }

  /// <summary>The version of the second conflicting package.</summary>
  public string? Package2Version { get; }

  /// <summary>The dependency name causing the conflict.</summary>
  public string? ConflictName { get; }

  /// <summary>The dependency version requirement causing the conflict.</summary>
  public string? ConflictVersion { get; }

  /// <summary>The description of the conflict reason.</summary>
  public string? ConflictDescription { get; }

  /// <summary>The first conflicting package as a borrowed view.</summary>
  public PackageView Package1 => field ??= new PackageView(_raw->conflict.conflict->package1, _package1Lifetime);

  /// <summary>The second conflicting package as a borrowed view.</summary>
  public PackageView Package2 => field ??= new PackageView(_raw->conflict.conflict->package2, _package2Lifetime);

  /// <summary>Gets or sets whether the conflicting package should be removed.</summary>
  public readonly bool Remove
  {
    get => _raw->conflict.remove != 0;
    set => _raw->conflict.remove = value ? 1 : 0;
  }

  /// <summary>
  /// This should NEVER be called.
  /// </summary>
  /// <exception cref="InvalidOperationException"></exception>
  [Obsolete("Question structs should not be constructed by consumers.", error: true)]
  public PackageConflictQuestion() =>
    throw new InvalidOperationException("Question structs should not be constructed by consumers.");

  internal PackageConflictQuestion(_alpm_question_t* question, ChildLifetime package1Lifetime,
    ChildLifetime package2Lifetime)
  {
    Debug.Assert(question->type_ == _alpm_question_type_t.ALPM_QUESTION_CONFLICT_PKG);
    _raw = question;
    _package1Lifetime = package1Lifetime;
    _package2Lifetime = package2Lifetime;

    var conflict = question->conflict.conflict;
    if (conflict == null) return;
    Package1Name = NativeString.FromNative((nint)NativeMethods.alpm_pkg_get_name(conflict->package1));
    Package1Version = NativeString.FromNative((nint)NativeMethods.alpm_pkg_get_version(conflict->package1));

    Package2Name = NativeString.FromNative((nint)NativeMethods.alpm_pkg_get_name(conflict->package2));
    Package2Version = NativeString.FromNative((nint)NativeMethods.alpm_pkg_get_version(conflict->package2));

    if (conflict->reason == null) return;
    ConflictName = NativeString.FromNative((nint)conflict->reason->name);
    ConflictVersion = NativeString.FromNative((nint)conflict->reason->version);
    ConflictDescription = NativeString.FromNative((nint)conflict->reason->desc);
  }
}

/// <summary>
/// Question asked when a downloaded package file is found to be corrupted.
/// </summary>
/// This is marked readonly because the managed struct itself is an immutable
/// proxy of the underlying native question struct. You can and should still write
/// to the <see cref="Remove"/> property to answer the question.
[PublicAPI]
public readonly unsafe ref struct CorruptedPackageQuestion
{
  private readonly _alpm_question_t* _raw;

  /// <summary>The filesystem path to the corrupted package file.</summary>
  public string? FilePath { get; }

  /// <summary>The description of why the package file is considered corrupted.</summary>
  public string? Reason { get; }

  /// <summary>The underlying libalpm errno, for reference only.</summary>
  public uint ReasonCode => (uint)_raw->corrupted.reason;

  /// <summary>Gets or sets whether the corrupted package file should be removed.</summary>
  public bool Remove
  {
    get => _raw->corrupted.remove != 0;
    set => _raw->corrupted.remove = value ? 1 : 0;
  }

  /// <summary>
  /// This should NEVER be called.
  /// </summary>
  /// <exception cref="InvalidOperationException"></exception>
  [Obsolete("Question structs should not be constructed by consumers.", error: true)]
  public CorruptedPackageQuestion() =>
    throw new InvalidOperationException("Question structs should not be constructed by consumers.");

  internal CorruptedPackageQuestion(_alpm_question_t* question)
  {
    Debug.Assert(question->type_ == _alpm_question_type_t.ALPM_QUESTION_CORRUPTED_PKG);
    _raw = question;
    FilePath = NativeString.FromNative((nint)question->corrupted.filepath);
    Reason = ErrnoMapper.StrError(question->corrupted.reason);
  }
}

/// <summary>
/// Question asked when unresolvable dependencies occur in a transaction (libalpm's <c>ALPM_QUESTION_REMOVE_PKGS</c>).
/// </summary>
/// <remarks>
/// Asks whether to remove unresolvable target packages from the transaction (skip them) so that remaining packages
/// can be installed or upgraded.
/// <br/>
/// This is marked readonly because the managed struct itself is an immutable
/// proxy of the underlying native question struct. You can and should still write
/// to the <see cref="Skip"/> property to answer the question.
/// </remarks>
[PublicAPI]
public readonly unsafe ref struct RemoveUnresolvablePackagesQuestion
{
  private readonly _alpm_question_t* _raw;

  /// <summary>The packages with unresolvable dependencies proposed to be removed from the transaction.</summary>
  public IReadOnlyList<PackageView> Packages { get; }

  /// <summary>Gets or sets whether to skip these packages in the transaction.</summary>
  public bool Skip
  {
    get => _raw->remove_pkgs.skip != 0;
    set => _raw->remove_pkgs.skip = value ? 1 : 0;
  }

  /// <summary>
  /// This should NEVER be called.
  /// </summary>
  /// <exception cref="InvalidOperationException"></exception>
  [Obsolete("Question structs should not be constructed by consumers.", error: true)]
  public RemoveUnresolvablePackagesQuestion() =>
    throw new InvalidOperationException("Question structs should not be constructed by consumers.");

  internal RemoveUnresolvablePackagesQuestion(_alpm_question_t* question, IReadOnlyList<PackageView> packages)
  {
    Debug.Assert(question->type_ == _alpm_question_type_t.ALPM_QUESTION_REMOVE_PKGS);
    _raw = question;
    Packages = packages;
  }
}

/// <summary>
/// Question asked when multiple providers satisfy a dependency and a selection is required.
/// </summary>
/// <remarks>
/// This is marked readonly because the managed struct itself is an immutable
/// proxy of the underlying native question struct. You can and should still write
/// to the <see cref="UseIndex"/> property to answer the question.
/// </remarks>
[PublicAPI]
public readonly unsafe ref struct SelectProviderQuestion
{
  private readonly _alpm_question_t* _raw;

  /// <summary>The name of the dependency needing a provider.</summary>
  public string? DependencyName { get; }

  /// <summary>The required version of the dependency.</summary>
  public string? DependencyVersion { get; }

  /// <summary>The description of the dependency.</summary>
  public string? DependencyDescription { get; }

  /// <summary>The candidate packages providing the dependency as borrowed views.</summary>
  public IReadOnlyList<PackageView> Providers { get; }

  /// <summary>Gets or sets the 0-based index of the chosen provider from <see cref="Providers" />.</summary>
  public int UseIndex
  {
    get => _raw->select_provider.use_index;
    set => _raw->select_provider.use_index = value;
  }

  /// <summary>
  /// This should NEVER be called.
  /// </summary>
  /// <exception cref="InvalidOperationException"></exception>
  [Obsolete("Question structs should not be constructed by consumers.", error: true)]
  public SelectProviderQuestion() =>
    throw new InvalidOperationException("Question structs should not be constructed by consumers.");

  internal SelectProviderQuestion(_alpm_question_t* question, IReadOnlyList<PackageView> providers)
  {
    Debug.Assert(question->type_ == _alpm_question_type_t.ALPM_QUESTION_SELECT_PROVIDER);
    _raw = question;
    Providers = providers;

    if (question->select_provider.depend == null) return;
    DependencyName = NativeString.FromNative((nint)question->select_provider.depend->name);
    DependencyVersion = NativeString.FromNative((nint)question->select_provider.depend->version);
    DependencyDescription = NativeString.FromNative((nint)question->select_provider.depend->desc);
  }
}

/// <summary>
/// Question asked when an unknown PGP key needs to be imported into the keyring.
/// </summary>
/// <remarks>
/// This is marked readonly because the managed struct itself is an immutable
/// proxy of the underlying native question struct. You can and should still write
/// to the <see cref="Import"/> property to answer the question.
/// </remarks>
[PublicAPI]
public readonly unsafe ref struct ImportKeyQuestion
{
  private readonly _alpm_question_t* _raw;

  /// <summary>The user ID or identity associated with the key.</summary>
  public string? Uid { get; }

  /// <summary>The fingerprint of the key.</summary>
  public string? Fingerprint { get; }

  /// <summary>Gets or sets whether to import the key.</summary>
  public bool Import
  {
    get => _raw->import_key.import != 0;
    set => _raw->import_key.import = value ? 1 : 0;
  }

  /// <summary>
  /// This should NEVER be called.
  /// </summary>
  /// <exception cref="InvalidOperationException"></exception>
  [Obsolete("Question structs should not be constructed by consumers.", error: true)]
  public ImportKeyQuestion() =>
    throw new InvalidOperationException("Question structs should not be constructed by consumers.");

  internal ImportKeyQuestion(_alpm_question_t* question)
  {
    Debug.Assert(question->type_ == _alpm_question_type_t.ALPM_QUESTION_IMPORT_KEY);
    _raw = question;
    Uid = NativeString.FromNative((nint)question->import_key.uid);
    Fingerprint = NativeString.FromNative((nint)question->import_key.fingerprint);
  }
}
