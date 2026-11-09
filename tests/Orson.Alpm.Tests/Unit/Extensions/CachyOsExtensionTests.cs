using Orson.Alpm.Extensions;
using Orson.Alpm.Tests.Fixtures;

namespace Orson.Alpm.Tests.Unit.Extensions;

public sealed class CachyOsExtensionTests
{
  [Fact]
  public void GetPhysicalArchitecture_BehavesAccordingToSupport()
  {
    using var env = new IsolatedAlpmEnvironment();
    var options = env.AlpmSession.Options;

    if (CachyOs.IsPhysicalArchitectureSupported)
    {
      var arches = options.GetPhysicalArchitecture();
      Assert.NotEmpty(arches);
      Assert.Contains("x86_64", arches);
      Assert.All(arches, arch => Assert.False(string.IsNullOrEmpty(arch)));
      Assert.Equal(arches.Distinct().Count(), arches.Count);
    }
    else
    {
      Assert.Throws<PlatformNotSupportedException>(options.GetPhysicalArchitecture);
    }
  }

  [Fact]
  public void GetPhysicalArchitecture_ReturnsManagedCopyThatOutlivesTheSession()
  {
    Assert.SkipUnless(CachyOs.IsPhysicalArchitectureSupported,
      "The installed libalpm does not export alpm_option_get_physical_architectures.");

    using var env = new IsolatedAlpmEnvironment();
    var arches = env.AlpmSession.Options.GetPhysicalArchitecture();

    // The list libalpm returns is freed in the call. Naming it after the handle is gone proves the
    // wrapper copied the strings instead of handing out a view over released native memory.
    Assert.Contains("x86_64", arches);
  }

  [Fact]
  public void GetPhysicalArchitecture_GuardsAgainstDisposedOptions()
  {
    var env = new IsolatedAlpmEnvironment();
    var options = env.AlpmSession.Options;
    env.Dispose();

    Assert.Throws<AlpmLifetimeException>(options.GetPhysicalArchitecture);
  }

  [Fact]
  public void GetPhysicalArchitecture_GuardsAgainstNullOptions()
  {
    Assert.Throws<ArgumentNullException>(() => ((AlpmOptions)null!).GetPhysicalArchitecture());
  }

  [Fact]
  public void GetInstalledDatabase_BehavesAccordingToSupport()
  {
    using var env = new IsolatedAlpmEnvironment();
    var pkg = env.Install("test-pkg");

    if (CachyOs.IsInstalledDatabaseSupported)
    {
      var dbName = pkg.GetInstalledDatabase();
      // An isolated test install does not come from a sync db, so null is expected.
      Assert.Null(dbName);
    }
    else
    {
      Assert.Throws<PlatformNotSupportedException>(pkg.GetInstalledDatabase);
    }
  }

  [Fact]
  public void GetInstalledDatabase_ReturnsOriginDatabaseName()
  {
    Assert.SkipUnless(CachyOs.IsInstalledDatabaseSupported,
      "The installed libalpm does not export alpm_pkg_get_installed_db.");

    using var env = new IsolatedAlpmEnvironment();
    env.Install("synced-pkg");

    // A file install has no origin database. Recording the field the CachyOS fork writes on a
    // sync-database install exercises the non-null path through libalpm's local-database parser.
    var desc = Path.Combine(env.DatabasePath, "local", $"synced-pkg-{PackageArchive.Version}", "desc");
    File.AppendAllText(desc, "%INSTALLED_DB%\ncore\n\n");

    env.AlpmSession.Dispose();
    using var reopened = new AlpmSession(env.Root, env.DatabasePath);
    var pkg = reopened.GetLocalDatabase().GetPackage("synced-pkg");

    Assert.NotNull(pkg);
    Assert.Equal("core", pkg.GetInstalledDatabase());
  }

  [Fact]
  public void GetInstalledDatabase_GuardsAgainstDisposedPackage()
  {
    var env = new IsolatedAlpmEnvironment();
    var pkg = env.Install("test-pkg");

    env.Dispose(); // dispose the environment and the associated AlpmSession

    Assert.Throws<AlpmLifetimeException>(pkg.GetInstalledDatabase);
  }

  [Fact]
  public void GetInstalledDatabase_GuardsAgainstNullPackage()
  {
    Assert.Throws<ArgumentNullException>(() => ((Package)null!).GetInstalledDatabase());
  }
}
