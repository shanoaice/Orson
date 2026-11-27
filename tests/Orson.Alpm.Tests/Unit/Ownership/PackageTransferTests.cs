using Orson.Alpm.Bindings;
using Orson.Alpm.Tests.Fixtures;

namespace Orson.Alpm.Tests.Unit.Ownership;

/// <summary>
/// Covers the ownership hand-over of <see cref="Transaction.AddPackage(LoadedPackage)" />: libalpm frees a
/// file-loaded package when the transaction is released, so the wrapper must stop owning it at that moment - otherwise the
/// natural <c>using</c> pattern aborts the process with a double free. The two <c>AddPackage</c> overloads cannot be
/// confused, because <see cref="PackageView" /> and <see cref="LoadedPackage" /> do not convert to one another.
/// </summary>
/// <remarks>
/// Reaching the end of <see cref="AddPackage_TransfersOwnership_SoBothDisposalsAreSafe" /> is half of the test:
/// the old wrapper freed the package a second time there and died with SIGABRT, which a C probe against libalpm reproduced
/// in two release orders.
/// </remarks>
[Collection("AlpmSessionSerial")]
public sealed class PackageTransferTests : IDisposable
{
  private readonly IsolatedAlpmEnvironment _environment = new();

  public void Dispose() => _environment.Dispose();

  private string PackageDirectory => _environment.PackageDirectory;

  private LoadedPackage Load(string name, string? file = null)
    => _environment.AlpmSession.LoadPackage(
      file == null
        ? PackageArchive.Create(PackageDirectory, name)
        : PackageArchive.Create(PackageDirectory, new PackageSpec(name) { File = file }),
      full: true, SigLevel.UseDefault);

  /// <summary>
  /// The shape that used to abort. Both <c>using</c> declarations run their <c>Dispose</c> at the end of the method
  /// - the transaction first (which frees the package), then the wrapper (which must not touch it again).
  /// </summary>
  [Fact]
  public void AddPackage_TransfersOwnership_SoBothDisposalsAreSafe()
  {
    using var package = Load("orson-transfer", file: "usr/bin/probe-file");
    using var transaction = _environment.AlpmSession.BeginTransaction();

    var view = transaction.AddPackage(package);

    Assert.Equal("orson-transfer", view.Name);

    transaction.Prepare();
    transaction.Commit();
  }

  /// <summary>
  /// Handing the package over gives the caller a borrowed view back: it replaces the wrapper whose ownership was
  /// given up, and it does not own anything itself.
  /// </summary>
  [Fact]
  public void AddPackage_ReturnsABorrowedView()
  {
    var package = Load("orson-view");
    using var transaction = _environment.AlpmSession.BeginTransaction();

    var view = transaction.AddPackage(package);

    Assert.Equal("orson-view", view.Name);
  }

  /// <summary>
  /// A view whose owning transaction was released must never reach libalpm. Before the pointer was confined behind
  /// <c>ValidatedPtr</c>, this call handed the stale pointer straight to <c>alpm_add_pkg</c>, so libalpm parsed freed memory
  /// and the diagnostic was a SIGSEGV inside native code rather than a managed lifetime exception.
  /// </summary>
  [Fact]
  public void AddPackage_OfAViewWhoseTransactionWasReleased_Throws()
  {
    PackageView stale;
    using (var owner = _environment.AlpmSession.BeginTransaction())
    {
      stale = owner.AddPackage(Load("orson-stale-view"));
    }

    using var receiver = _environment.AlpmSession.BeginTransaction();

    Assert.Throws<AlpmLifetimeException>(() => receiver.AddPackage(stale));
  }

  /// <summary>
  /// After the hand-over the wrapper is inert: reads throw (the pointer stays valid only until the transaction is
  /// released, which the wrapper cannot observe) and <c>Dispose</c> does nothing.
  /// </summary>
  [Fact]
  public void AddPackage_RetiresTheWrapperThatWasHandedOver()
  {
    var package = Load("orson-retired");
    using var transaction = _environment.AlpmSession.BeginTransaction();

    transaction.AddPackage(package);

    Assert.False(package.OwnsPackage);
    Assert.Throws<ObjectDisposedException>(() => package.Name);

    // Must not throw, and must not release the package the transaction owns.
    package.Dispose();

    Assert.Throws<ObjectDisposedException>(() => package.Origin);
  }

  /// <summary>
  /// A second hand-over is a caller error, and it is rejected before libalpm is touched. libalpm would not act on
  /// it either - it deduplicates the same package pointer in the transaction's list (probed: <c>alpm_add_pkg</c> twice
  /// leaves exactly one entry).
  /// </summary>
  [Fact]
  public void AddPackage_OfAnAlreadyHandedOverPackage_Throws()
  {
    var package = Load("orson-twice");
    using var transaction = _environment.AlpmSession.BeginTransaction();
    transaction.AddPackage(package);

    Assert.Throws<ObjectDisposedException>(() => transaction.AddPackage(package));

    Assert.Single(transaction.AddedPackages);
  }

  /// <summary>
  /// The borrowing overload is the one for a package libalpm owns. libalpm refuses an *already installed* package
  /// with <c>ALPM_ERR_WRONG_ARGS</c>; adding one is only meaningful for a sync-database package.
  /// </summary>
  /// <remarks>
  /// This is also a deterministic failing <c>alpm_add_pkg</c>: adding the same package twice is deduplicated, and an
  /// ignored package is still accepted (probed).
  /// </remarks>
  [Fact]
  public void AddPackage_OfAnInstalledPackage_IsRefused_AndLeavesItWithItsDatabase()
  {
    var path = PackageArchive.Create(PackageDirectory, new PackageSpec("orson-installed") { File = "usr/bin/probe-file" });

    using (var installer = _environment.AlpmSession.BeginTransaction())
    {
      installer.AddPackage(_environment.AlpmSession.LoadPackage(path, full: true, SigLevel.UseDefault));
      installer.Prepare();
      installer.Commit();
    }

    var installedOpt = _environment.AlpmSession.GetLocalDatabase().GetPackage("orson-installed");
    Assert.NotNull(installedOpt);
    var installed = installedOpt.Value;

    using var transaction = _environment.AlpmSession.BeginTransaction();

    var failure = Assert.Throws<AlpmException>(() => transaction.AddPackage(installed));

    Assert.Equal((int)_alpm_errno_t.ALPM_ERR_WRONG_ARGS, failure.Errno);
    Assert.Empty(transaction.AddedPackages);

    // The transaction borrowed nothing and freed nothing: the package still belongs to the local db.
    Assert.Equal("orson-installed", installed.Name);
  }
}
