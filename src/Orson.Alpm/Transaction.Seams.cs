using System.Diagnostics.CodeAnalysis;
using Orson.Alpm.Bindings;

namespace Orson.Alpm;

public sealed partial class Transaction
{
  internal static bool TryCreate(AlpmSession alpmSession, TransactionFlags flags,
    [NotNullWhen(true)] out Transaction? transaction,
    [NotNullWhen(false)] out AlpmFailure? failure)
  {
    var err = NativeMethods.alpm_trans_init(alpmSession.Handle, (int)flags);
    if (err != 0)
    {
      transaction = null;
      failure = AlpmFailure.Of(alpmSession.Handle, "start transaction");
      return false;
    }

    transaction = new Transaction(alpmSession);
    failure = null;
    return true;
  }

  /// <summary>The failure-returning seam for <see cref="Prepare" />.</summary>
  internal unsafe bool TryPrepare([NotNullWhen(false)] out AlpmFailure? failure)
  {
    ThrowIfDisposed();

    _alpm_list_t* errData = null;

    var err = NativeMethods.alpm_trans_prepare(_session.Handle, &errData);
    if (err != 0)
    {
      _completionState = CompletionState.Faulted;
      failure = AlpmFailure.Take(_session.Errno, errData, "prepare transaction");
      GC.KeepAlive(_session);
      GC.KeepAlive(this);
      return false;
    }

    failure = null;
    return true;
  }

  /// <summary>The failure-returning seam for <see cref="AddPackage(PackageView)" />.</summary>
  internal unsafe bool TryAddPackage(PackageView pkg, [NotNullWhen(false)] out AlpmFailure? failure)
  {
    ThrowIfDisposed();
    if (pkg == default)
    {
      throw new ArgumentException("The package view is uninitialized.", nameof(pkg));
    }

    var err = NativeMethods.alpm_add_pkg(_session.Handle, pkg.BackingStruct);
    if (err != 0)
    {
      _completionState = CompletionState.Faulted;
      failure = AlpmFailure.Of(_session.Handle, "add package");
      return false;
    }
    failure = null;
    return true;
  }

  /// <summary>The failure-returning seam for <see cref="AddPackage(LoadedPackage)" />.</summary>
  internal unsafe bool TryAddPackage(LoadedPackage pkg,
    out PackageView view,
    [NotNullWhen(false)] out AlpmFailure? failure)
  {
    ThrowIfDisposed();
    ArgumentNullException.ThrowIfNull(pkg);

    // Before touching libalpm: this instance is already inert when it was handed over once, and
    // repeating the call is a caller error rather than something libalpm could act on.
    pkg.ThrowIfNotOwned();

    var err = NativeMethods.alpm_add_pkg(_session.Handle, pkg.BackingStruct);
    if (err != 0)
    {
      _completionState = CompletionState.Faulted;
      view = default;
      failure = AlpmFailure.Of(_session.Handle, "add package");
      return false;
    }

    // The pointer is now the transaction's to free; the wrapper must never release it again. Read the
    // views off it first, because the hand-over retires the wrapper for reads too. The view carries
    // the transaction's token: the package lives exactly as long as the transaction owns it.
    view = new PackageView(pkg.BackingStruct, Lifetime);
    pkg.Disown();
    failure = null;
    return true;
  }

  /// <summary>The failure-returning seam for <see cref="RemovePackage(PackageView)" />.</summary>
  internal unsafe bool TryRemovePackage(PackageView pkg, [NotNullWhen(false)] out AlpmFailure? failure)
  {
    ThrowIfDisposed();
    if (pkg == default)
    {
      throw new ArgumentException("The package view is uninitialized.", nameof(pkg));
    }

    var err = NativeMethods.alpm_remove_pkg(_session.Handle, pkg.BackingStruct);
    if (err != 0)
    {
      _completionState = CompletionState.Faulted;
      failure = AlpmFailure.Of(_session.Handle, "remove package");
      return false;
    }

    failure = null;
    return true;
  }

  /// <summary>The failure-returning seam for <see cref="SystemUpgrade(bool)" />.</summary>
  internal bool TrySystemUpgrade(bool enableDowngrade, [NotNullWhen(false)] out AlpmFailure? failure)
  {
    ThrowIfDisposed();
    var err = NativeMethods.alpm_sync_sysupgrade(_session.Handle, enableDowngrade ? 1 : 0);
    if (err != 0)
    {
      _completionState = CompletionState.Faulted;
      failure = AlpmFailure.Of(_session.Handle, "compute system upgrade");
      return false;
    }
    failure = null;
    return true;
  }

  /// <summary>The failure-returning seam for <see cref="Commit" />.</summary>
  internal unsafe bool TryCommit([NotNullWhen(false)] out AlpmFailure? failure)
  {
    ThrowIfDisposed();

    _completionState = CompletionState.Committing;

    // Retire existing local database and transaction views before libalpm rewrites them.
    // Callbacks receive their own scoped lifetime tokens, and sync databases remain valid.
    _session.InvalidateLocalDatabase("Transaction.Commit()");
    Lifetime.Invalidate("Transaction.Commit()");

    try
    {
      _alpm_list_t* messages = null;
      var err = NativeMethods.alpm_trans_commit(_session.Handle, &messages);
      if (err != 0)
      {
        _completionState = CompletionState.Faulted;
        failure = AlpmFailure.Take(_session.Errno, messages, "commit transaction");
        GC.KeepAlive(_session);
        GC.KeepAlive(this);
        return false;
      }

      _completionState = CompletionState.Committed;
      failure = null;
      return true;
    }
    finally
    {
      // Ensure any local database wrapper created during a callback is retired.
      _session.InvalidateLocalDatabase("Transaction.Commit()");
    }
  }
}
