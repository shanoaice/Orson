using Orson.Alpm.Tests.Fixtures;

namespace Orson.Alpm.Tests.Unit.Options;

/// <summary>Coverage ledger §4: the sandbox options.</summary>
/// <remarks>
/// Component flags are wrapped (filesystem and syscalls). The aggregate C accessors (
/// <c>alpm_option_get/set_disable_sandbox</c>) are declared in alpm.h but are <b>not exported</b> by the installed
/// <c>libalpm.so.16</c>. Callers that need the state should read the component flags.
/// </remarks>
public sealed class SandboxOptionTests
{
  [Fact]
  public void Components_DefaultToEnabled()
  {
    using var environment = new IsolatedAlpmEnvironment();
    var options = environment.AlpmSession.Options;

    Assert.False(options.DisableSandboxFilesystem);
    Assert.False(options.DisableSandboxSyscalls);
  }

  [Fact]
  public void Components_RoundTrip()
  {
    using var environment = new IsolatedAlpmEnvironment();
    var options = environment.AlpmSession.Options;

    options.DisableSandboxFilesystem = true;
    options.DisableSandboxSyscalls = true;
    Assert.True(options.DisableSandboxFilesystem);
    Assert.True(options.DisableSandboxSyscalls);

    options.DisableSandboxFilesystem = false;
    Assert.False(options.DisableSandboxFilesystem);
    Assert.True(options.DisableSandboxSyscalls);
  }

  [Fact]
  public void SandboxUser_StartsNull_AndRoundTrips()
  {
    using var environment = new IsolatedAlpmEnvironment();
    var options = environment.AlpmSession.Options;

    Assert.Null(options.SandboxUser);

    options.SandboxUser = "nobody";
    Assert.Equal("nobody", options.SandboxUser);
  }

  /// <summary>A null user is legal: libalpm clears the setting and returns 0.</summary>
  [Fact]
  public void SandboxUser_CanBeResetToNull()
  {
    using var environment = new IsolatedAlpmEnvironment();
    var options = environment.AlpmSession.Options;
    options.SandboxUser = "nobody";

    options.SandboxUser = null;

    Assert.Null(options.SandboxUser);
  }

  /// <summary>
  /// Measured: libalpm returns <c>1</c> for an unknown user <b>without setting pm_errno</b>, so this must not go
  /// through <c>ErrorHandler.ToException(Errno)</c> — ALPM_ERR_OK would turn into a misleading ArgumentOutOfRangeException.
  /// </summary>
  [Fact]
  public void SandboxUser_WithAnUnknownUser_ThrowsArgumentException()
  {
    using var environment = new IsolatedAlpmEnvironment();
    var options = environment.AlpmSession.Options;

    var exception = Assert.Throws<ArgumentException>(() => options.SandboxUser = "orson-no-such-user-9f3a");

    Assert.Equal("value", exception.ParamName);
    Assert.Null(options.SandboxUser);
  }

  [Fact]
  public void DisableDownloadTimeout_RoundTrips()
  {
    using var environment = new IsolatedAlpmEnvironment();
    var options = environment.AlpmSession.Options;

    Assert.False(options.DisableDownloadTimeout);

    options.DisableDownloadTimeout = true;
    Assert.True(options.DisableDownloadTimeout);

    options.DisableDownloadTimeout = false;
    Assert.False(options.DisableDownloadTimeout);
  }
}
