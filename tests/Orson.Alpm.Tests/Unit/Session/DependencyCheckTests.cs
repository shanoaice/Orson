using Orson.Alpm.Tests.Fixtures;

namespace Orson.Alpm.Tests.Unit.Session;

/// <summary>Coverage ledger §4: <c>alpm_checkdeps</c> / <c>alpm_checkconflicts</c>.</summary>
/// <remarks>
/// Both functions return an allocated list or <c>NULL</c>, and <b>NULL means "nothing wrong"</b> — not an error
/// (measured: errno stays ALPM_ERR_OK). With an empty local database there is nothing to check, which is what these tests
/// exercise; the cases that do report a missing dependency or a conflict are covered through a transaction, in
/// <see cref="Orson.Alpm.Tests.Unit.Errors.TransactionFailureTests" />.
/// </remarks>
public sealed class DependencyCheckTests
{
  [Fact]
  public void CheckDependencies_WithAnEmptyPackageList_ReturnsNothing()
  {
    using var environment = new IsolatedAlpmEnvironment();
    var packages = environment.AlpmSession.GetLocalDatabase().LoadPackageCache();

    Assert.Empty(environment.AlpmSession.CheckDependencies(packages));
  }

  [Fact]
  public void CheckConflicts_WithAnEmptyPackageList_ReturnsNothing()
  {
    using var environment = new IsolatedAlpmEnvironment();
    var packages = environment.AlpmSession.GetLocalDatabase().LoadPackageCache();

    Assert.Empty(environment.AlpmSession.CheckConflicts(packages));
  }

  [Fact]
  public void CheckDependencies_AcceptsTheRemoveAndUpgradeLists()
  {
    using var environment = new IsolatedAlpmEnvironment();
    var packages = environment.AlpmSession.GetLocalDatabase().LoadPackageCache();

    var missing = environment.AlpmSession.CheckDependencies(
      packages, remove: packages, upgrade: packages, reverseDependencies: true);

    Assert.Empty(missing);
  }

  /// <summary>
  /// The input list is borrowed by libalpm and only freed by us if we built it; the caller's
  /// <c>AlpmList&lt;PackageView&gt;</c> must still be usable afterwards.
  /// </summary>
  [Fact]
  public void CheckConflicts_DoesNotConsumeTheCallersList()
  {
    using var environment = new IsolatedAlpmEnvironment();
    var packages = environment.AlpmSession.GetLocalDatabase().LoadPackageCache();

    environment.AlpmSession.CheckConflicts(packages);

    Assert.Empty(packages);
  }

  [Fact]
  public void CheckDependencies_ReportsUnsatisfiedDependencyOnMissingTarget()
  {
    using var env = new IsolatedAlpmEnvironment();
    env.Install("libfoo");
    env.Install("app-needs-libfoo", depend: "libfoo>=1.0");

    var localDb = env.AlpmSession.GetLocalDatabase();
    var packages = localDb.LoadPackageCache();
    var libfoo = localDb.GetPackage("libfoo")!;
    var result = env.AlpmSession.CheckDependencies(packages, remove: [libfoo], upgrade: [], reverseDependencies: true);
    var missing = Assert.Single(result);
    Assert.Equal("app-needs-libfoo", missing.Target);
    Assert.NotNull(missing.Dependency);
    Assert.Equal("libfoo", missing.Dependency.Name);
    Assert.Equal("libfoo", missing.CausingPackage);
  }

  [Fact]
  public void CheckConflicts_ReportsConflictBetweenPackages()
  {
    using var env = new IsolatedAlpmEnvironment();
    env.Install("pkg-a", conflict: "pkg-b");
    env.Install("pkg-b", flags: TransactionFlags.NoConflicts);

    var localDb = env.AlpmSession.GetLocalDatabase();
    var packages = localDb.LoadPackageCache();

    var conflicts = env.AlpmSession.CheckConflicts(packages);
    var conflict = Assert.Single(conflicts);
    Assert.Equal("pkg-a", conflict.Package1Name);
    Assert.Equal("pkg-b", conflict.Package2Name);
    Assert.Equal("pkg-b", conflict.Reason.Name);
  }
}
