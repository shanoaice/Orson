using System.Runtime.InteropServices;
using Orson.Alpm.Bindings;
using Orson.Alpm.Tests.Fixtures;

namespace Orson.Alpm.Tests.Unit.Callbacks;

[Collection("AlpmSessionSerial")]
public sealed unsafe class HotCallbackDispatchTests
{
  [Fact]
  public void ProgressDispatch_ReusesThePackageNameString()
  {
    using var environment = new IsolatedAlpmEnvironment();
    var callbacks = environment.AlpmSession.Callbacks;
    var received = new List<AlpmProgressEvent>();
    callbacks.ProgressHandler = received.Add;

    var packageName = NativeString.ToNative("orson-progress");
    try
    {
      callbacks.DispatchProgress(_alpm_progress_t.ALPM_PROGRESS_ADD_START, packageName, 0, 1, 2);
      callbacks.DispatchProgress(_alpm_progress_t.ALPM_PROGRESS_ADD_START, packageName, 50, 1, 2);
      callbacks.DispatchProgress(_alpm_progress_t.ALPM_PROGRESS_ADD_START, packageName, 100, 1, 2);
    }
    finally
    {
      NativeMemory.Free(packageName);
    }

    Assert.Equal(3, received.Count);
    Assert.Same(received[0].PackageName, received[1].PackageName);
    Assert.Same(received[1].PackageName, received[2].PackageName);
    Assert.Equal("orson-progress", received[0].PackageName);
    Assert.Equal([0, 50, 100], received.Select(item => item.Percent));
  }

  [Fact]
  public void DownloadDispatch_UsesAValuePayload()
  {
    using var environment = new IsolatedAlpmEnvironment();
    var callbacks = environment.AlpmSession.Callbacks;
    var received = new List<(string Filename, AlpmDownloadEvent Event)>();
    callbacks.DownloadHandler = (filename, downloadEvent) => received.Add((filename, downloadEvent));

    var filename = NativeString.ToNative("orson-download.pkg.tar.zst");
    var progress = stackalloc _alpm_download_event_progress_t[1];
    progress->downloaded = new CLong(25);
    progress->total = new CLong(100);
    try
    {
      callbacks.DispatchDownload(filename, _alpm_download_event_type_t.ALPM_DOWNLOAD_PROGRESS, progress);
      progress->downloaded = new CLong(100);
      callbacks.DispatchDownload(filename, _alpm_download_event_type_t.ALPM_DOWNLOAD_PROGRESS, progress);
    }
    finally
    {
      NativeMemory.Free(filename);
    }

    Assert.Equal(2, received.Count);
    Assert.Equal("orson-download.pkg.tar.zst", received[0].Filename);
    Assert.Equal("orson-download.pkg.tar.zst", received[1].Filename);
    Assert.Equal(DownloadEventType.Progress, received[0].Event.Type);
    Assert.Equal(25, received[0].Event.Progress!.Value.Downloaded);
    Assert.Equal(100, received[1].Event.Progress!.Value.Downloaded);
  }
}
