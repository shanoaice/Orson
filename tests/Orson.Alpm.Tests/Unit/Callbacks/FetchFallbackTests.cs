using Orson.Alpm.Tests.Fixtures;

namespace Orson.Alpm.Tests.Unit.Callbacks;

public sealed class FetchFallbackTests
{
  [Fact]
  public void FetchPackageUrl_WithoutFetchHandler_UsesInternalDownloader_FailsWithRetrieveFailedEvent()
  {
    using var env = new IsolatedAlpmEnvironment();

    var events = new List<AlpmEvent>();
    env.AlpmSession.Callbacks.EventHandler = events.Add;

    Assert.ThrowsAny<AlpmException>(() =>
      env.AlpmSession.FetchPackageUrls(["http://127.0.0.1:1/x-1.0-1-x86_64.pkg.tar.zst"]));

    Assert.Contains(events, e => e is AlpmEvent.PackageRetrieveFailed);
    Assert.DoesNotContain(events, e => e is AlpmEvent.PackageRetrieveDone);
  }
}
