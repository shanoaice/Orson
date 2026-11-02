using Pacpar.Alpm.Tests.Fixtures;

namespace Pacpar.Alpm.Tests.Unit.Databases;

/// <summary>
/// Coverage ledger §4: <c>alpm_db_usage_t</c> exposed as <see cref="DatabaseUsage"/>.
/// </summary>
public sealed class DatabaseUsageTests
{
  /// <summary>Measured on libalpm 16.0.1: a freshly registered sync database reports 15 (ALL).</summary>
  [Fact]
  public void Usage_DefaultsToAll()
  {
    using var environment = new IsolatedAlpmEnvironment();
    var db = environment.AlpmSession.RegisterSyncDatabase("pacpar-test", 0);

    Assert.Equal(DatabaseUsage.All, db.Usage);
  }

  [Fact]
  public void Usage_RoundTrips()
  {
    using var environment = new IsolatedAlpmEnvironment();
    var db = environment.AlpmSession.RegisterSyncDatabase("pacpar-test", 0);

    db.Usage = DatabaseUsage.Search | DatabaseUsage.Install;
    Assert.Equal(DatabaseUsage.Search | DatabaseUsage.Install, db.Usage);

    db.Usage = DatabaseUsage.All;
    Assert.Equal(DatabaseUsage.All, db.Usage);
  }

  [Fact]
  public void Usage_OnTheLocalDatabase_IsReadable()
  {
    using var environment = new IsolatedAlpmEnvironment();

    Assert.Equal(DatabaseUsage.All, environment.AlpmSession.GetLocalDatabase().Usage);
  }
}
