using Pacpar.Alpm.Tests.Fixtures;

namespace Pacpar.Alpm.Tests.Unit.Packages;

/// <summary>
/// Covers <see cref="PackageView.GetNewVersion"/>, which searches only the databases it is handed.
/// </summary>
public sealed class PackageUpgradeTests : IDisposable
{
  private readonly IsolatedAlpmEnvironment _environment = new();

  public void Dispose() => _environment.Dispose();

  [Fact]
  public void PackageView_GetNewVersion_ReturnsNullWhenNoSyncDatabasesHaveNewer()
  {
    var pkg = _environment.Install("pacpar-upgrade-check");
    var syncDb = _environment.AlpmSession.RegisterSyncDatabase("pacpar-sync-empty", 0);

    var upgrade = pkg.GetNewVersion([syncDb]);
    Assert.Null(upgrade);
  }

  [Fact]
  public void PackageView_GetNewVersion_ReturnsNewerSyncPackageWhenAvailable()
  {
    var installedPkg = _environment.Install("upgrade-pkg");
    PackageArchive.CreateSyncDatabase(_environment.DatabasePath, "core", "upgrade-pkg", "base", version: "2.0-1");
    _environment.AlpmSession.RegisterSyncDatabase("core", SigLevel.UseDefault);

    var newer = installedPkg.GetNewVersion(_environment.AlpmSession.GetSyncDatabases());
    Assert.NotNull(newer);
    Assert.Equal("2.0-1", newer.Version.ToString());
  }
}
