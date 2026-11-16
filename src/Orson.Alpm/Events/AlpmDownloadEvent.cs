using Orson.Alpm.Bindings;

namespace Orson.Alpm;

/// <summary>The kind of payload carried by an <see cref="AlpmDownloadEvent" />.</summary>
public enum DownloadEventType : uint
{
  /// <summary>A download is starting (<c>ALPM_DOWNLOAD_INIT</c>).</summary>
  Init = 0,

  /// <summary>A download has transferred more bytes (<c>ALPM_DOWNLOAD_PROGRESS</c>).</summary>
  Progress = 1,

  /// <summary>A download is about to be retried (<c>ALPM_DOWNLOAD_RETRY</c>).</summary>
  Retry = 2,

  /// <summary>A download has completed (<c>ALPM_DOWNLOAD_COMPLETED</c>).</summary>
  Completed = 3
}

/// <summary>Payload for <see cref="DownloadEventType.Init" />.</summary>
/// <param name="IsOptional">Gets whether the initialization describes an optional file.</param>
public readonly record struct DownloadInit(bool IsOptional);

/// <summary>Payload for <see cref="DownloadEventType.Progress" />.</summary>
/// <param name="Downloaded">Gets the number of bytes downloaded so far.</param>
/// <param name="Total">Gets the expected total download size in bytes.</param>
public readonly record struct DownloadProgress(long Downloaded, long Total);

/// <summary>Payload for <see cref="DownloadEventType.Retry" />.</summary>
/// <param name="WillResume">Gets whether the retry will resume downloading from an existing partial file.</param>
public readonly record struct DownloadRetry(bool WillResume);

/// <summary>Payload for <see cref="DownloadEventType.Completed" />.</summary>
/// <param name="Total">Gets the total size of the completed download in bytes.</param>
/// <param name="Result">
/// Gets libalpm's raw result code: 0 on success, 1 when the file was already up to date, or -1 on error.
/// </param>
public readonly record struct DownloadCompleted(long Total, int Result)
{
  /// <summary>Gets whether the download succeeded.</summary>
  public bool IsSuccessful => Result == 0;

  /// <summary>Gets whether the local file was already up to date and did not need to be retrieved.</summary>
  public bool IsUpToDate => Result == 1;

  /// <summary>Gets whether the download encountered an error.</summary>
  public bool IsError => Result == -1;
}

/// <summary>
/// A copy of the payload libalpm passes to a download callback. The value is independent of the native callback
/// frame and remains valid after the callback returns.
/// </summary>
/// <remarks>
/// Inspect <see cref="Type" /> or match against <see cref="Init" />, <see cref="Progress" />, <see cref="Retry" />,
/// or <see cref="Completed" /> to retrieve the strongly-typed payload.
/// </remarks>
public readonly struct AlpmDownloadEvent
{
  private readonly long _downloaded;
  private readonly long _total;
  private readonly int _result;
  private readonly bool _isOptional;
  private readonly bool _willResume;

  private AlpmDownloadEvent(
    DownloadEventType type,
    bool isOptional = false,
    long downloaded = 0,
    long total = 0,
    bool willResume = false,
    int result = 0)
  {
    Type = type;
    _isOptional = isOptional;
    _downloaded = downloaded;
    _total = total;
    _willResume = willResume;
    _result = result;
  }

  internal static unsafe AlpmDownloadEvent FromUnion(_alpm_download_event_type_t eventType, void* data)
  {
    return eventType switch
    {
      _alpm_download_event_type_t.ALPM_DOWNLOAD_INIT =>
        new(DownloadEventType.Init, isOptional: ((_alpm_download_event_init_t*)data)->optional != 0),
      _alpm_download_event_type_t.ALPM_DOWNLOAD_PROGRESS => CreateProgress((_alpm_download_event_progress_t*)data),
      _alpm_download_event_type_t.ALPM_DOWNLOAD_RETRY =>
        new(DownloadEventType.Retry, willResume: ((_alpm_download_event_retry_t*)data)->resume != 0),
      _alpm_download_event_type_t.ALPM_DOWNLOAD_COMPLETED => CreateCompleted((_alpm_download_event_completed_t*)data),
      _ => throw new ArgumentException($"Unknown download event type: {eventType}"),
    };
  }

  private static unsafe AlpmDownloadEvent CreateProgress(_alpm_download_event_progress_t* progress)
    => new(DownloadEventType.Progress, downloaded: progress->downloaded.Value, total: progress->total.Value);

  private static unsafe AlpmDownloadEvent CreateCompleted(_alpm_download_event_completed_t* completed)
    => new(DownloadEventType.Completed, total: completed->total.Value, result: completed->result);

  /// <summary>Gets the kind of download event.</summary>
  public DownloadEventType Type { get; }

  /// <summary>
  /// Gets the initialization payload when <see cref="Type" /> is <see cref="DownloadEventType.Init" />; otherwise,
  /// <see langword="null" />.
  /// </summary>
  public DownloadInit? Init => Type == DownloadEventType.Init ? new DownloadInit(_isOptional) : null;

  /// <summary>
  /// Gets the progress payload when <see cref="Type" /> is <see cref="DownloadEventType.Progress" />; otherwise,
  /// <see langword="null" />.
  /// </summary>
  public DownloadProgress? Progress =>
    Type == DownloadEventType.Progress ? new DownloadProgress(_downloaded, _total) : null;

  /// <summary>
  /// Gets the retry payload when <see cref="Type" /> is <see cref="DownloadEventType.Retry" />; otherwise,
  /// <see langword="null" />.
  /// </summary>
  public DownloadRetry? Retry => Type == DownloadEventType.Retry ? new DownloadRetry(_willResume) : null;

  /// <summary>
  /// Gets the completion payload when <see cref="Type" /> is <see cref="DownloadEventType.Completed" />; otherwise,
  /// <see langword="null" />.
  /// </summary>
  public DownloadCompleted? Completed =>
    Type == DownloadEventType.Completed ? new DownloadCompleted(_total, _result) : null;

  /// <summary>Attempts to retrieve the initialization payload.</summary>
  /// <param name="init">The payload when this event is <see cref="DownloadEventType.Init" />.</param>
  /// <returns><see langword="true" /> if this event is an initialization event; otherwise, <see langword="false" />.</returns>
  public bool TryGetInit(out DownloadInit init)
  {
    if (Type == DownloadEventType.Init)
    {
      init = new DownloadInit(_isOptional);
      return true;
    }

    init = default;
    return false;
  }

  /// <summary>Attempts to retrieve the progress payload.</summary>
  /// <param name="progress">The payload when this event is <see cref="DownloadEventType.Progress" />.</param>
  /// <returns><see langword="true" /> if this event is a progress event; otherwise, <see langword="false" />.</returns>
  public bool TryGetProgress(out DownloadProgress progress)
  {
    if (Type == DownloadEventType.Progress)
    {
      progress = new DownloadProgress(_downloaded, _total);
      return true;
    }

    progress = default;
    return false;
  }

  /// <summary>Attempts to retrieve the retry payload.</summary>
  /// <param name="retry">The payload when this event is <see cref="DownloadEventType.Retry" />.</param>
  /// <returns><see langword="true" /> if this event is a retry event; otherwise, <see langword="false" />.</returns>
  public bool TryGetRetry(out DownloadRetry retry)
  {
    if (Type == DownloadEventType.Retry)
    {
      retry = new DownloadRetry(_willResume);
      return true;
    }

    retry = default;
    return false;
  }

  /// <summary>Attempts to retrieve the completion payload.</summary>
  /// <param name="completed">The payload when this event is <see cref="DownloadEventType.Completed" />.</param>
  /// <returns><see langword="true" /> if this event is a completion event; otherwise, <see langword="false" />.</returns>
  public bool TryGetCompleted(out DownloadCompleted completed)
  {
    if (Type == DownloadEventType.Completed)
    {
      completed = new DownloadCompleted(_total, _result);
      return true;
    }

    completed = default;
    return false;
  }
}
