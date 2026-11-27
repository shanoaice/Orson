using Orson.Alpm.Tests.Fixtures;

namespace Orson.Alpm.Tests.Unit.Databases;

/// <summary>Covers <see cref="SyncDatabase.Update" /> reporting the failure libalpm gives it.</summary>
[Collection("AlpmSessionSerial")]
public sealed class DatabaseUpdateTests : IDisposable
{
  private readonly IsolatedAlpmEnvironment _environment = new();

  public void Dispose() => _environment.Dispose();

  [Fact]
  public void Database_Update_WithoutServers_FailsAndReportsError()
  {
    var syncDb = _environment.AlpmSession.RegisterSyncDatabase("orson-sync-update", 0);

    var ok = syncDb.TryUpdate(force: false, out var updated, out var failure);
    Assert.False(ok);
    Assert.False(updated);
    Assert.NotNull(failure);

    Assert.ThrowsAny<AlpmException>(() => syncDb.Update());
  }
}
