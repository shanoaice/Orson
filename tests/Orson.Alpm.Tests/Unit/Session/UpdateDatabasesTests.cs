using Orson.Alpm.Tests.Fixtures;

namespace Orson.Alpm.Tests.Unit.Session;

/// <summary>
/// Covers <see cref="AlpmSession.UpdateDatabases" /> argument validation: an empty sequence is caller error, not
/// a native failure.
/// </summary>
public sealed class UpdateDatabasesTests
{
  [Fact]
  public void UpdateDatabases_WithNoDatabases_ThrowsArgumentException()
  {
    using var environment = new IsolatedAlpmEnvironment();

    var exception = Assert.Throws<ArgumentException>(() => environment.AlpmSession.UpdateDatabases([]));

    Assert.Equal("databases", exception.ParamName);
  }

  [Fact]
  public void TryUpdateDatabases_WithNoDatabases_ThrowsArgumentException()
  {
    using var environment = new IsolatedAlpmEnvironment();

    var exception = Assert.Throws<ArgumentException>(() =>
      environment.AlpmSession.TryUpdateDatabases([], force: false, out _, out _));

    Assert.Equal("databases", exception.ParamName);
  }
}
