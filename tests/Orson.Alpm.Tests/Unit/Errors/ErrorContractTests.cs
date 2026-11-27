using Orson.Alpm.Bindings;
using Orson.Alpm.Tests.Fixtures;

namespace Orson.Alpm.Tests.Unit.Errors;

/// <summary>
/// Locks in the low-risk half of the error-signal rules:
/// <list type="bullet">
/// <item>rule 3 - a null native pointer with an ok errno means "empty", not "failure";</item>
/// <item>rule 5 - option getters that start out unset are nullable instead of asserting non-null.</item>
/// </list>
/// </summary>
[Collection("AlpmSessionSerial")]
public sealed class ErrorContractTests : IDisposable
{
  private readonly IsolatedAlpmEnvironment _environment = new();

  public void Dispose() => _environment.Dispose();

  private AlpmSession AlpmSession => _environment.AlpmSession;

  private Database LocalDatabase => _environment.AlpmSession.GetLocalDatabase();

  [Fact]
  public void LoadPackageCache_IsAnEmptyView_WhenTheDatabaseHasNoPackages()
  {
    // Regression: this used to throw ArgumentOutOfRangeException - the null cache came with
    // ALPM_ERR_OK and ToException rejects "ok is not an error".
    Assert.Empty(LocalDatabase.LoadPackageCache());
  }

  [Fact]
  public void LoadGroupCache_IsAnEmptyView_WhenTheDatabaseHasNoGroups()
    => Assert.Empty(LocalDatabase.LoadGroupCache());

  [Fact]
  public void TryLoadPackageCache_ReturnsFalseAndSetsFailure_WhenLibalpmReportsAnError()
  {
    var sync = AlpmSession.RegisterSyncDatabase("core", 0);

    var ok = sync.TryLoadPackageCache(out var cache, out var failure);

    Assert.False(ok);
    Assert.Null(cache);
    Assert.NotNull(failure);
    Assert.Equal(AlpmFailureCode.DatabaseNotFound, failure.Code);
    Assert.Equal((int)_alpm_errno_t.ALPM_ERR_DB_NOT_FOUND, failure.NativeCode);
  }

  [Fact]
  public void UnsetOptionStrings_AreNull_InsteadOfPretendingToBeSet()
  {
    Assert.Null(AlpmSession.Options.LogFile);
    Assert.Null(AlpmSession.Options.GpgDirectory);
  }

  [Fact]
  public void AlwaysSetOptionStrings_AreNotNull()
  {
    Assert.NotNull(AlpmSession.Options.Root);
    Assert.NotNull(AlpmSession.Options.DatabasePath);
    Assert.NotNull(AlpmSession.Options.Lockfile);
  }
}
