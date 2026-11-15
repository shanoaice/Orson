using Orson.Alpm.Bindings;
using Orson.Alpm.Tests.Fixtures;

namespace Orson.Alpm.Tests.Unit.Ownership;

/// <summary>
/// The handle's one-transaction-at-a-time contract as the wrapper presents it: libalpm allows one active transaction per handle.
/// Attempting to start a second transaction while one is active throws <see cref="InvalidOperationException" />.
/// </summary>
[Collection("AlpmSessionSerial")]
public sealed class TransactionLifecycleTests
{
  /// <summary>No flags: every check on, database locked, filesystem written.</summary>
  private const TransactionFlags NoFlags = 0;
  private const TransactionFlags Lockless = TransactionFlags.NoLock;

  [Fact]
  public void BeginTransaction_ThrowsInvalidOperationException_WhenTransactionIsAlreadyActive()
  {
    using var environment = new IsolatedAlpmEnvironment();
    var alpm = environment.AlpmSession;

    using var first = alpm.BeginTransaction(Lockless);
    var exception = Assert.Throws<InvalidOperationException>(() => alpm.BeginTransaction(Lockless));

    Assert.Contains("already active", exception.Message);
  }

  [Fact]
  public void BeginTransaction_DefaultsToTheFullyCheckedMode()
  {
    using var environment = new IsolatedAlpmEnvironment();
    var alpm = environment.AlpmSession;

    var transaction = alpm.BeginTransaction();

    Assert.Equal(NoFlags, transaction.Flags);
    Assert.Same(transaction, alpm.CurrentTransaction);
  }

  [Fact]
  public void BeginTransaction_WithDifferentFlags_WhileATransactionIsActive_Throws()
  {
    using var environment = new IsolatedAlpmEnvironment();
    var alpm = environment.AlpmSession;
    using var active = alpm.BeginTransaction();

    var exception = Assert.Throws<InvalidOperationException>(() => alpm.BeginTransaction(Lockless));

    Assert.Contains("already active", exception.Message);
    Assert.Same(active, alpm.CurrentTransaction);
    Assert.Equal(NoFlags, active.Flags);
  }

  [Fact]
  public void CurrentTransaction_IsCleared_WhenTheTransactionIsDisposed()
  {
    using var environment = new IsolatedAlpmEnvironment();
    var alpm = environment.AlpmSession;

    var transaction = alpm.BeginTransaction(Lockless);
    transaction.Dispose();

    Assert.Null(alpm.CurrentTransaction);
  }
  [Fact]
  public void CurrentTransaction_IsNotCleared_WhenNativeReleaseFails()
  {
    using var environment = new IsolatedAlpmEnvironment();
    var alpm = environment.AlpmSession;

    var transaction = alpm.BeginTransaction(Lockless);

    // Directly release the native transaction so that transaction.Dispose()'s release call fails with -1
    var nativeErr = NativeMethods.alpm_trans_release(alpm.Handle);
    Assert.Equal(0, nativeErr);

    // Now disposing the transaction fails at the native layer because handle->trans is already null
    transaction.Dispose();

    // The wrapper must keep CurrentTransaction intact because release failed.
    Assert.Same(transaction, alpm.CurrentTransaction);

    // The domain is retired all the same. alpm_trans_release fails only when handle->trans is
    // already NULL - that is, when the native transaction is already gone - and that is exactly when
    // every view into it is dangling. Dispose invalidates before it releases, so a free never
    // happens under a view that still passes its check; the failure path is no exception.
    Assert.False(transaction.Lifetime.IsAlive);
  }

  /// <summary>
  /// ADR 0008: <see cref="Transaction.Dispose" /> does not throw, so a failed <c>alpm_trans_release</c> has to be
  /// observable on the transaction - otherwise the caller is told the transaction ended while the lock and the handle are
  /// still held.
  /// </summary>
  [Fact]
  public void Dispose_RecordsTheFailure_WhenTheNativeReleaseFails()
  {
    using var environment = new IsolatedAlpmEnvironment();
    var alpm = environment.AlpmSession;

    var transaction = alpm.BeginTransaction(Lockless);
    Assert.Null(transaction.ReleaseFailure);

    // Release the native transaction behind the wrapper's back, so the release Dispose attempts
    // fails with -1 on a handle whose trans is already NULL.
    Assert.Equal(0, NativeMethods.alpm_trans_release(alpm.Handle));

    transaction.Dispose();

    Assert.False(transaction.IsReleased);

    var failure = Assert.IsAssignableFrom<AlpmException>(transaction.ReleaseFailure);
    Assert.Equal((int)_alpm_errno_t.ALPM_ERR_TRANS_NULL, failure.Errno);
    Assert.Contains("release transaction", failure.Message);
  }

  /// <summary>The other half: a release that worked leaves nothing to report.</summary>
  [Fact]
  public void Dispose_LeavesNoFailure_WhenTheReleaseSucceeds()
  {
    using var environment = new IsolatedAlpmEnvironment();
    var alpm = environment.AlpmSession;

    var transaction = alpm.BeginTransaction(Lockless);
    Assert.False(transaction.IsReleased);

    transaction.Dispose();

    Assert.True(transaction.IsReleased);
    Assert.Null(transaction.ReleaseFailure);
    Assert.Null(alpm.CurrentTransaction);
  }
  [Fact]
  public void Dispose_Throws_WhenReleaseFailsAfterSuccessfulCommit()
  {
    using var environment = new IsolatedAlpmEnvironment();
    var alpm = environment.AlpmSession;

    var pkgPath = PackageArchive.Create(environment.PackageDirectory, "orson-commit-ok");
    using var loaded = alpm.LoadPackage(pkgPath, full: true, SigLevel.UseDefault);

    var transaction = alpm.BeginTransaction();
    transaction.AddPackage(loaded);
    transaction.Prepare();
    transaction.Commit();

    // Force a release failure behind the wrapper's back after commit succeeded:
    Assert.Equal(0, NativeMethods.alpm_trans_release(alpm.Handle));

    var ex = Assert.Throws<AlpmTransactionException>(() => transaction.Dispose());
    Assert.Equal((int)_alpm_errno_t.ALPM_ERR_TRANS_NULL, ex.Errno);
    Assert.False(transaction.IsReleased);
    Assert.NotNull(transaction.ReleaseFailure);
  }

  [Fact]
  public void BeginTransaction_AfterTheHandleWasDisposed_ThrowsObjectDisposed()
  {
    using var environment = new IsolatedAlpmEnvironment();
    var alpm = environment.AlpmSession;
    alpm.Dispose();

    Assert.Throws<ObjectDisposedException>(() => alpm.BeginTransaction());
  }

  [Fact]
  public void Transaction_RemovePackage_PopulatesRemovedPackagesAndFlags()
  {
    using var environment = new IsolatedAlpmEnvironment();
    var pkg = environment.Install("removable-pkg");
    var localDb = environment.AlpmSession.GetLocalDatabase();

    using (var tx = environment.AlpmSession.BeginTransaction(TransactionFlags.NoSave))
    {
      tx.RemovePackage(pkg);
      Assert.Equal(TransactionFlags.NoSave, tx.Flags);
      Assert.Single(tx.RemovedPackages, p => p.Name == "removable-pkg");
      tx.Prepare();
      tx.Commit();
    }

    Assert.Null(environment.AlpmSession.GetLocalDatabase().GetPackage("removable-pkg"));
  }

  [Fact]
  public void Interrupt_WhenNotCommitting_ThrowsInvalidOperationException()
  {
    using var environment = new IsolatedAlpmEnvironment();
    using var tx = environment.AlpmSession.BeginTransaction();

    Assert.Throws<InvalidOperationException>(() => tx.Interrupt());
  }
}
