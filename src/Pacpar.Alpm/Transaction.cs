using Pacpar.Alpm.Bindings;

namespace Pacpar.Alpm;

/// <summary>Transaction flags</summary>
/// <remarks>
/// <c>0</c> — the value the optional parameter of <see cref="AlpmSession.BeginTransaction" /> defaults to — is
/// libalpm's default mode rather than "no configuration": every dependency, conflict and file-conflict check runs, hooks
/// and scriptlets are executed, the database is locked and the filesystem is written. Every member below deviates from
/// that, either as an opt-out (<see cref="NoDeps" />, <see cref="NoConflicts" />, <see cref="NoSave" />,
/// <see cref="NoLock" />, <see cref="DbOnly" />, …) or as an explicit opt-in (<see cref="Cascade" />,
/// <see cref="Recurse" />, <see cref="AllDeps" />, …), so a caller who does not want one of those deviations has nothing
/// to pass.
/// </remarks>
[Flags]
public enum TransactionFlags : uint
{
  /// <summary>No transaction flags set; standard fully-checked mode (0).</summary>
  None = 0,

  /// <summary>Ignore dependency checks (<c>ALPM_TRANS_FLAG_NODEPS</c>).</summary>
  NoDeps = 1,

  /// <summary>Delete files even if they are tagged as backup (<c>ALPM_TRANS_FLAG_NOSAVE</c>).</summary>
  NoSave = 4,

  /// <summary>Ignore version numbers when checking dependencies ( <c>ALPM_TRANS_FLAG_NODEPVERSION</c>).</summary>
  NoDepVersion = 8,

  /// <summary>Remove also any packages depending on a package being removed ( <c>ALPM_TRANS_FLAG_CASCADE</c>).</summary>
  Cascade = 16,

  /// <summary>Remove packages and their unneeded deps (not explicitly installed) (<c>ALPM_TRANS_FLAG_RECURSE</c>).</summary>
  Recurse = 32,

  /// <summary>Modify database but do not commit changes to the filesystem ( <c>ALPM_TRANS_FLAG_DBONLY</c>).</summary>
  DbOnly = 64,

  /// <summary>Do not run hooks during a transaction (<c>ALPM_TRANS_FLAG_NOHOOKS</c>).</summary>
  NoHooks = 128,

  /// <summary>Use <see cref="PackageReason.Dependency" /> when installing packages (<c>ALPM_TRANS_FLAG_ALLDEPS</c>).</summary>
  AllDeps = 256,

  /// <summary>Only download packages and do not actually install ( <c>ALPM_TRANS_FLAG_DOWNLOADONLY</c>).</summary>
  DownloadOnly = 512,

  /// <summary>Do not execute install scriptlets after installing ( <c>ALPM_TRANS_FLAG_NOSCRIPTLET</c>).</summary>
  NoScriptlet = 1024,

  /// <summary>Ignore dependency conflicts (<c>ALPM_TRANS_FLAG_NOCONFLICTS</c>).</summary>
  NoConflicts = 2048,

  /// <summary>Do not install a package if it is already installed and up to date (<c>ALPM_TRANS_FLAG_NEEDED</c>).</summary>
  Needed = 8192,

  /// <summary>Use <see cref="PackageReason.Explicit" /> when installing packages (<c>ALPM_TRANS_FLAG_ALLEXPLICIT</c>).</summary>
  AllExplicit = 16384,

  /// <summary>Do not remove a package if it is needed by another one ( <c>ALPM_TRANS_FLAG_UNNEEDED</c>).</summary>
  Unneeded = 32768,

  /// <summary>
  /// Remove also explicitly installed unneeded deps (use with <see cref="Recurse" /> ) (
  /// <c>ALPM_TRANS_FLAG_RECURSEALL</c>).
  /// </summary>
  RecurseAll = 65536,

  /// <summary>Do not lock the database during the operation (<c>ALPM_TRANS_FLAG_NOLOCK</c>).</summary>
  NoLock = 131072,
}

/// <summary>Represents an active package transaction within an <see cref="AlpmSession" />.</summary>
/// <remarks>
/// A transaction modifies local packages and databases in atomic phases. Only one transaction can exist per
/// session at any time. The lifecycle consists of adding operations, calling <see cref="Prepare" />, and calling
/// <see cref="Commit" />. Acquires the database lock unless <see cref="TransactionFlags.NoLock" /> is configured.
/// Disposing the transaction releases the lock and native transaction memory.
/// </remarks>
public sealed partial class Transaction : IDisposable
{
  private enum CompletionState : byte
  {
    InProgress,
    Committing,
    Committed,
    Faulted,
  }

  private CompletionState _completionState = CompletionState.InProgress;
  private readonly AlpmSession _library;
  private readonly LifetimeStamp _stamp;

  /// <summary>
  /// The transaction's lifetime token, a child of the handle's root token: it is retired both by this transaction's
  /// own successful release and - through the parent chain - by the handle's disposal. Every view this transaction issues
  /// (the <see cref="AddPackage(LoadedPackage)" /> return value and the elements of <see cref="AddedPackages" />/
  /// <see cref="RemovedPackages" />) carries it.
  /// </summary>
  internal readonly ChildLifetime Lifetime;

  private Transaction(AlpmSession alpmLibrary)
  {
    _library = alpmLibrary;
    _stamp = alpmLibrary.RootLifetime.Capture();
    Lifetime = alpmLibrary.RootLifetime.CreateChild("the transaction");
  }


  private void ThrowIfDisposed()
  {
    ObjectDisposedException.ThrowIf(IsReleased, this);
    _stamp.ThrowIfStale();
  }

  /// <summary>
  /// Whether this transaction has been released: <c>true</c> once <see cref="Dispose()" /> has run to the point
  /// where nothing of it is left to release.
  /// </summary>
  /// <remarks>
  /// Becomes <c>true</c> once <see cref="Dispose()" /> successfully frees the native transaction, or when the owning
  /// <see cref="AlpmSession" /> was disposed first.
  /// <para>If this property is <c>false</c> after calling <see cref="Dispose()" />, releasing the native transaction failed:</para>
  /// <list type="bullet">
  /// <item>
  /// <description>
  /// For uncommitted or faulted transactions, <see cref="Dispose()" /> suppresses release failures to avoid
  /// masking existing exceptions. Check this property and <see cref="ReleaseFailure" /> to detect whether the database lock
  /// was left held.
  /// </description>
  /// </item>
  /// <item>
  /// <description>
  /// For successfully committed transactions, <see cref="Dispose()" /> throws immediately instead of remaining
  /// silent, leaving this property <c>false</c>.
  /// </description>
  /// </item>
  /// </list>
  /// </remarks>
  public bool IsReleased { get; private set; }

  /// <summary>
  /// The failure <see cref="Dispose()" /> observed from <c>alpm_trans_release</c>, or <c>null</c> when the release
  /// succeeded or was never attempted.
  /// </summary>
  /// <remarks>
  /// The value is an <see cref="AlpmException" /> (carrying the libalpm errno) or an <see cref="OutOfMemoryException" />.
  /// <para>
  /// When disposing an uncommitted or faulted transaction, <see cref="Dispose()" /> records the failure here without
  /// throwing, preserving any active exception during <c>using</c> or <c>finally</c> block unwinding. When disposing a
  /// committed transaction, this failure represents unrecoverable native state corruption with no prior exception to mask;
  /// <see cref="Dispose()" /> records it here and throws it.
  /// </para>
  /// <para>
  /// The first failure is kept: a later <see cref="Dispose()" /> that retries does not clear it, because the state
  /// that failure describes is the one still in force.
  /// </para>
  /// <para>
  /// This is not written to the consumer's log callback. That callback is an inbound channel from libalpm and may have
  /// never been registered; teardown is also when it is the least safe to call.
  /// </para>
  /// </remarks>
  public Exception? ReleaseFailure { get; private set; }


  /// <summary>Prepares the transaction: the dependency, conflict and architecture checks.</summary>
  /// <remarks>
  /// A successful call returns normally without diagnostics. A failure is reported as the
  /// <see cref="AlpmTransactionException" /> case that matches the errno, carrying the payload as a managed snapshot; the
  /// native list is freed at the same moment, with the element destructor the errno requires.
  /// </remarks>
  public void Prepare()
  {
    if (!TryPrepare(out var failure))
    {
      throw failure.ToException("Failed to prepare transaction");
    }
  }

  /// <summary>Adds a package that libalpm already owns (a database package, for example a member of the local database).</summary>
  /// <remarks>
  /// The transaction only borrows it: releasing the transaction does not free it. A package this library loaded
  /// from a file must go through <see cref="AddPackage(LoadedPackage)" /> instead, which is the only overload that can take
  /// over the release - the two overloads cannot be confused, because <see cref="PackageView" /> and
  /// <see cref="LoadedPackage" /> do not convert to one another.
  /// </remarks>
  public void AddPackage(PackageView pkg)
  {
    var packageName = pkg.Name;
    if (!TryAddPackage(pkg, out var failure))
    {
      throw failure.ToException($"Failed to add package: {packageName}");
    }
  }


  /// <summary>
  /// Adds a package this library loaded from a file, handing over its ownership: libalpm frees such a package when
  /// the transaction is released (<c>alpm.h</c>: "If the package was loaded by <c>alpm_pkg_load()</c>, it will be freed upon
  /// <c>alpm_trans_release</c> invocation").
  /// </summary>
  /// <returns>
  /// A borrowed view of the package, valid while the transaction owns it: it belongs to this transaction, so
  /// releasing the transaction - or disposing the handle - retires it. The view replaces the wrapper whose ownership was
  /// given up, which stops answering reads after this call; the view itself owns nothing, so giving it to
  /// <see cref="AddPackage(PackageView)" /> never transfers ownership.
  /// </returns>
  /// <exception cref="ObjectDisposedException">The package was already released or handed to a transaction.</exception>
  public PackageView AddPackage(LoadedPackage pkg)
  {
    var packageName = pkg.Name;
    return !TryAddPackage(pkg, out var view, out var failure)
      ? throw failure.ToException($"Failed to add package: {packageName}")
      : view;
  }


  /// <summary>Marks an installed package to be removed as part of this transaction.</summary>
  /// <param name="pkg">The package to remove.</param>
  public void RemovePackage(PackageView pkg)
  {
    var packageName = pkg.Name;
    if (!TryRemovePackage(pkg, out var failure))
    {
      throw failure.ToException($"Failed to remove package: {packageName}");
    }
  }


  /// <summary>Searches registered sync databases for updates to installed packages and adds them to this transaction.</summary>
  /// <param name="enableDowngrade">
  /// Whether to allow downgrading packages if the repository version is older. The default,
  /// <c>false</c>, matches <c>pacman -Su</c>; <c>true</c> matches <c>pacman -Suu</c> .
  /// </param>
  public void SystemUpgrade(bool enableDowngrade = false)
  {
    if (!TrySystemUpgrade(enableDowngrade, out var failure))
    {
      throw failure.ToException();
    }
  }


  /// <summary>Requests that the active transaction operation be interrupted.</summary>
  public void Interrupt()
  {
    ThrowIfDisposed();
    if (_completionState != CompletionState.Committing)
    {
      throw new InvalidOperationException("Cannot interrupt a transaction that is not committing.");
    }

    var err = NativeMethods.alpm_trans_interrupt(_library.Handle);
    if (err != 0)
    {
      throw AlpmFailure.Of(_library.Handle, "interrupt transaction").ToException();
    }
  }


  /// <summary>Commits the transaction.</summary>
  /// <remarks>
  /// A successful commit returns normally without diagnostics. A failure is reported as the
  /// <see cref="AlpmTransactionException" /> case that matches the errno: conflicting files, or a list of package names,
  /// depending on the errno. The native list is freed at the same moment, with the element destructor that errno requires.
  /// </remarks>
  public void Commit()
  {
    if (!TryCommit(out var failure))
    {
      throw failure.ToException("Failed to commit transaction");
    }
  }

  /// <summary>The packages this transaction is going to install, as a borrowed view.</summary>
  /// <remarks>
  /// The list and its elements belong to the transaction: releasing the transaction frees the file-loaded packages
  /// it owns, after which this view answers <see cref="AlpmLifetimeException" /> instead of reading freed memory.
  /// </remarks>
  public unsafe AlpmList<PackageView> AddedPackages
  {
    get
    {
      ThrowIfDisposed();
      return AlpmList<PackageView>.Borrow(NativeMethods.alpm_trans_get_add(_library.Handle),
        &PackageView.Factory, Lifetime);
    }
  }

  /// <summary>Gets the flags configured for this transaction.</summary>
  /// <remarks>
  /// Returns the flags specified when calling <see cref="AlpmSession.BeginTransaction" />. These flags dictate
  /// whether dependency checks, hooks, database locks, or file overwrites are applied.
  /// </remarks>
  public TransactionFlags Flags
  {
    get
    {
      ThrowIfDisposed();
      var flags = (TransactionFlags)NativeMethods.alpm_trans_get_flags(_library.Handle);
      GC.KeepAlive(_library);
      GC.KeepAlive(this);
      return flags;
    }
  }

  /// <summary>The packages this transaction is going to remove, as a borrowed view.</summary>
  /// <remarks>
  /// Like <see cref="AddedPackages" />, the view belongs to the transaction and is retired when the transaction is
  /// released.
  /// </remarks>
  public unsafe AlpmList<PackageView> RemovedPackages
  {
    get
    {
      ThrowIfDisposed();
      return AlpmList<PackageView>.Borrow(NativeMethods.alpm_trans_get_remove(_library.Handle),
        &PackageView.Factory, Lifetime);
    }
  }

  /// <summary>Releases the native transaction and its database lock deterministically.</summary>
  /// <remarks>
  /// This disposal might throw. Its behavior depends on the transaction's completion state:
  /// <list type="bullet">
  /// <item>
  /// <description>
  /// <b>Committed:</b> If <see cref="Commit" /> succeeded but releasing the native transaction fails, this
  /// method throws. There is no prior exception to mask, and continuing execution with a retained database lock or
  /// unreleased transaction handle leaves the session corrupted.
  /// </description>
  /// </item>
  /// <item>
  /// <description>
  /// <b>Uncommitted or Faulted:</b> If an operation failed prior to disposal (or the transaction was
  /// abandoned), release failures are captured in <see cref="ReleaseFailure" /> without throwing, preventing suppression of
  /// the primary exception during stack unwinding.
  /// </description>
  /// </item>
  /// </list>
  /// </remarks>
  /// <exception cref="AlpmException">
  /// The transaction committed successfully, but the native release failed. Inspect
  /// <see cref="ReleaseFailure" /> for the underlying libalpm errno.
  /// </exception>
  public void Dispose()
  {
    if (IsReleased || ReleaseFailure is not null) return;

    GC.SuppressFinalize(this);

    if (_library.Disposed)
    {
      // The session released this transaction on its way out; see IsReleased.
      IsReleased = true;
      return;
    }

    // Retire before the free. alpm_trans_release frees the transaction and the file-loaded packages
    // it owns, and a view must not pass its check while that happens. Invalidate is also correct on
    // the failure path: alpm_trans_release only fails when the transaction is already gone.
    Lifetime.Invalidate("Transaction.Dispose()");

    var err = NativeMethods.alpm_trans_release(_library.Handle);
    if (err == 0)
    {
      IsReleased = true;
      _library.CurrentTransaction = null;
      return;
    }

    // Behavior branches on completion state when native release fails:
    // 1. Faulted / InProgress: During 'using' stack unwinding, an exception is already in flight.
    //    Throwing here from 'finally' would cause the CLR to discard the primary exception
    //    (e.g., file conflict or missing dependency), masking the root cause. Record in
    //    ReleaseFailure without throwing (ADR 0008).
    // 2. Committed: Commit succeeded, so no exception is in flight to mask. A failed release
    //    indicates native state corruption (e.g., retained db.lck), so throw to fail fast.
    var failure = AlpmFailure.Of(_library.Handle, "release transaction");
    GC.KeepAlive(_library);
    GC.KeepAlive(this);

    var ex = failure.ToException();
    ReleaseFailure ??= ex;

    if (_completionState == CompletionState.Committed)
    {
      throw ex;
    }
  }
}
