using System.Runtime.InteropServices;
using Orson.Alpm.Bindings;

namespace Orson.Alpm.Tests.Unit.Snapshots;

public sealed unsafe class AlpmDownloadEventTests
{
  [Fact]
  public void FromUnion_CopiesInitPayload()
  {
    var data = stackalloc _alpm_download_event_init_t[1];
    data->optional = 1;

    var payload = AlpmDownloadEvent.FromUnion(_alpm_download_event_type_t.ALPM_DOWNLOAD_INIT, data);
    data->optional = 0;

    Assert.Equal(DownloadEventType.Init, payload.Type);
    var init = Assert.NotNull(payload.Init);
    Assert.True(init.IsOptional);
    Assert.True(payload.TryGetInit(out var tryInit));
    Assert.True(tryInit.IsOptional);
    Assert.Null(payload.Progress);
    Assert.False(payload.TryGetProgress(out _));
  }

  [Fact]
  public void FromUnion_CopiesProgressPayload()
  {
    var data = stackalloc _alpm_download_event_progress_t[1];
    data->downloaded = new CLong(123);
    data->total = new CLong(456);

    var payload = AlpmDownloadEvent.FromUnion(_alpm_download_event_type_t.ALPM_DOWNLOAD_PROGRESS, data);
    data->downloaded = default;
    data->total = default;

    Assert.Equal(DownloadEventType.Progress, payload.Type);
    var progress = Assert.NotNull(payload.Progress);
    Assert.Equal(123, progress.Downloaded);
    Assert.Equal(456, progress.Total);
    Assert.True(payload.TryGetProgress(out var tryProgress));
    Assert.Equal(123, tryProgress.Downloaded);
    Assert.Equal(456, tryProgress.Total);
    var (downloaded, total) = progress;
    Assert.Equal(123, downloaded);
    Assert.Equal(456, total);
    Assert.Null(payload.Init);
    Assert.False(payload.TryGetInit(out _));
  }

  [Fact]
  public void FromUnion_CopiesRetryPayload()
  {
    var data = stackalloc _alpm_download_event_retry_t[1];
    data->resume = 1;

    var payload = AlpmDownloadEvent.FromUnion(_alpm_download_event_type_t.ALPM_DOWNLOAD_RETRY, data);
    data->resume = 0;

    Assert.Equal(DownloadEventType.Retry, payload.Type);
    var retry = Assert.NotNull(payload.Retry);
    Assert.True(retry.WillResume);
    Assert.True(payload.TryGetRetry(out var tryRetry));
    Assert.True(tryRetry.WillResume);
    Assert.Null(payload.Completed);
    Assert.False(payload.TryGetCompleted(out _));
  }

  [Theory]
  [InlineData(0, true, false, false)]
  [InlineData(1, false, true, false)]
  [InlineData(-1, false, false, true)]
  public void FromUnion_CopiesCompletedPayloadAndResult(
    int result,
    bool isSuccessful,
    bool isUpToDate,
    bool isError)
  {
    var data = stackalloc _alpm_download_event_completed_t[1];
    data->total = new CLong(789);
    data->result = result;

    var payload = AlpmDownloadEvent.FromUnion(_alpm_download_event_type_t.ALPM_DOWNLOAD_COMPLETED, data);
    data->total = default;
    data->result = 0;

    Assert.Equal(DownloadEventType.Completed, payload.Type);
    var completed = Assert.NotNull(payload.Completed);
    Assert.Equal(789, completed.Total);
    Assert.Equal(result, completed.Result);
    Assert.Equal(isSuccessful, completed.IsSuccessful);
    Assert.Equal(isUpToDate, completed.IsUpToDate);
    Assert.Equal(isError, completed.IsError);
    Assert.True(payload.TryGetCompleted(out var tryCompleted));
    Assert.Equal(789, tryCompleted.Total);
    Assert.Equal(result, tryCompleted.Result);
    var (total, deconstructedResult) = completed;
    Assert.Equal(789, total);
    Assert.Equal(result, deconstructedResult);
    Assert.Null(payload.Progress);
    Assert.False(payload.TryGetProgress(out _));
  }

  [Fact]
  public void FromUnion_RejectsAnUnknownNativeEventType()
  {
    Assert.Throws<ArgumentException>(() =>
      AlpmDownloadEvent.FromUnion((_alpm_download_event_type_t)uint.MaxValue, null));
  }
}
