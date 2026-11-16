#pragma warning disable SYSLIB1054
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Orson.Alpm.Bindings;

namespace Orson.Alpm;

public sealed partial class AlpmCallbacks
{
  // Domain whose generation advances after every top-level event frame.
  // PackageViews borrowed during an event callback are invalidated as soon as the callback frame exits.
  private readonly ChildLifetime _eventLifetime;

  // Single-slot UTF-8 string cache for high-frequency progress callbacks.
  // Package operations emit thousands of consecutive callbacks for the same package name,
  // so a single-slot cache achieves >99% hit rate without dictionary lookups.
  // Note: Download filenames are not cached here because parallel downloads interleave filenames,
  // which makes single-slot caching counterproductive.
  private readonly Utf8StringCache _progressPackageNames = new();

  /// <summary>
  /// Unpacks the managed <see cref="AlpmCallbacks" /> instance from libalpm's <c>void* ctx</c> handle.
  /// </summary>
  /// <remarks>
  /// Protects against rare shutdown race conditions where native code triggers a callback while the managed session
  /// is being finalized or disposed.
  /// </remarks>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static unsafe bool TryGetContext(void* ctx, [NotNullWhen(true)] out AlpmCallbacks? callback)
  {
    if (ctx == null)
    {
      callback = null;
      return false;
    }

    try
    {
      return WeakGCHandle<AlpmCallbacks>.FromIntPtr((nint)ctx).TryGetTarget(out callback) && callback != null;
    }
    catch
    {
      // An invalid or already-disposed GCHandle must not escape to native code.
      callback = null;
      return false;
    }
  }

  // =========================================================================
  // Native FFI Agents & Internal Dispatchers
  // =========================================================================

  /// <summary>
  /// Native thunk for general transaction and database lifecycle events (<c>alpm_cb_event</c>).
  /// </summary>
  [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
  private static unsafe void EventAgent(void* ctx, _alpm_event_t* eventT)
  {
    if (!TryGetContext(ctx, out var callback)) return;
    callback.DispatchEvent(eventT);
  }

  /// <summary>
  /// Dispatches an event notification to the registered <see cref="EventHandler" />.
  /// </summary>
  /// <remarks>
  /// Manages the rolling lifetime generation for event frames:
  /// - Outer event frames reuse <see cref="_eventLifetime" /> and advance its generation in <see langword="finally" />,
  ///   retiring all <see cref="PackageView" /> instances created during that frame.
  /// - If a handler triggers a nested event (<c>_invokeDepth > 0</c>), a fresh child domain is created so the nested
  ///   event cannot prematurely invalidate the outer frame's views.
  /// </remarks>
  internal unsafe void DispatchEvent(_alpm_event_t* eventT)
  {
    if (EventHandler is not { } handler) return;

    var frame = _invokeDepth == 0
      ? _eventLifetime
      : _rootLifetime.CreateChild("a nested ALPM event callback");

    _invokeDepth++;
    try
    {
      var invocation = new EventInvocation(handler, eventT, frame);
      SafeInvoke(invocation, static state => state.Invoke(), HandlerException);
    }
    finally
    {
      _invokeDepth--;
      frame.Invalidate("the callback returned");
    }
  }

  /// <summary>
  /// Native thunk for custom file download fetching (<c>alpm_cb_fetch</c>).
  /// </summary>
  [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
  private static unsafe int FetchAgent(void* ctx, byte* url, byte* localPath, int force)
  {
    if (!TryGetContext(ctx, out var callback)) return -1;
    return callback.DispatchFetch(url, localPath, force);
  }

  /// <summary>
  /// Dispatches a custom file fetch request to <see cref="FetchHandler" />.
  /// </summary>
  /// <remarks>
  /// libalpm expects an outcome integer: 0 for success, 1 if the local file is already up to date, or -1 on error.
  /// If the managed handler throws or is missing, <c>-1</c> is returned to abort the fetch cleanly in native code.
  /// </remarks>
  internal unsafe int DispatchFetch(byte* url, byte* localPath, int force)
  {
    if (FetchHandler is not { } handler) return (int)FetchResult.Error;

    _invokeDepth++;
    try
    {
      var invocation = new FetchInvocation(handler, url, localPath, force);
      return SafeInvoke(invocation, static state => state.Invoke(), (int)FetchResult.Error, HandlerException);
    }
    finally
    {
      _invokeDepth--;
    }
  }

  /// <summary>
  /// Native thunk for transaction progress alerts (<c>alpm_cb_progress</c>).
  /// </summary>
  [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
  private static unsafe void ProgressAgent(
    void* ctx,
    _alpm_progress_t progress,
    byte* pkg,
    int percent,
    nuint howmany,
    nuint current)
  {
    if (!TryGetContext(ctx, out var callback)) return;
    callback.DispatchProgress(progress, pkg, percent, (int)current, (int)howmany);
  }

  /// <summary>
  /// Dispatches a progress update to <see cref="ProgressHandler" />.
  /// </summary>
  /// <remarks>
  /// This is libalpm's highest-frequency callback, firing once per archive entry during package extraction and removal.
  /// Steady-state allocations are zero: the package name uses a single-slot cache and the payload is a value type.
  /// </remarks>
  internal unsafe void DispatchProgress(
    _alpm_progress_t progress,
    byte* packageName,
    int percent,
    int current,
    int total)
  {
    if (ProgressHandler is not { } handler) return;

    var invocation = new ProgressInvocation(
      handler,
      (ProgressType)(uint)progress,
      _progressPackageNames.GetOrCreate(packageName),
      percent,
      current,
      total);

    _invokeDepth++;
    try
    {
      SafeInvoke(invocation, static state => state.Invoke(), HandlerException);
    }
    finally
    {
      _invokeDepth--;
    }
  }

  /// <summary>
  /// Native thunk for internal libcurl download progress (<c>alpm_cb_download</c>).
  /// </summary>
  [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
  private static unsafe void DownloadAgent(
    void* ctx,
    byte* filename,
    _alpm_download_event_type_t eventType,
    void* data)
  {
    if (!TryGetContext(ctx, out var callback)) return;
    callback.DispatchDownload(filename, eventType, data);
  }

  /// <summary>
  /// Dispatches download events emitted by libalpm's built-in downloader to <see cref="DownloadHandler" />.
  /// </summary>
  /// <remarks>
  /// The payload is an <see cref="AlpmDownloadEvent" /> readonly struct. Download filenames are decoded
  /// directly per event because parallel downloads interleave filenames, making single-slot caching ineffective.
  /// </remarks>
  internal unsafe void DispatchDownload(byte* filename, _alpm_download_event_type_t eventType, void* data)
  {
    if (DownloadHandler is not { } handler) return;

    var invocation = new DownloadInvocation(
      handler,
      NativeString.FromNative((nint)filename) ?? "",
      AlpmDownloadEvent.FromUnion(eventType, data));

    _invokeDepth++;
    try
    {
      SafeInvoke(invocation, static state => state.Invoke(), HandlerException);
    }
    finally
    {
      _invokeDepth--;
    }
  }

  /// <summary>
  /// Native thunk for libalpm log output (<c>alpm_cb_log</c>).
  /// </summary>
  [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
  private static unsafe void LogAgent(void* ctx, _alpm_loglevel_t level, byte* fmt, void* vaList)
  {
    if (!TryGetContext(ctx, out var callback)) return;
    callback.DispatchLog(level, fmt, vaList);
  }

  /// <summary>
  /// Formats and dispatches a native variadic log message to <see cref="LogHandler" />.
  /// </summary>
  /// <remarks>
  /// Messages are filtered against <see cref="EnabledLogLevels" /> prior to formatting the native <c>va_list</c>.
  /// If the level is enabled, <see cref="LogMessageFormatter" /> formats into a stack-allocated buffer or pooled memory.
  /// The resulting <see cref="ReadOnlySpan{T}" /> is valid only for the duration of the handler invocation.
  /// </remarks>
  internal unsafe void DispatchLog(_alpm_loglevel_t level, byte* format, void* vaList)
  {
    var logLevel = (LogLevel)(uint)level;
    if (LogHandler is not { } handler || (logLevel & _enabledLogLevels) == 0) return;

    var invocation = new LogInvocation(handler, logLevel, format, vaList);
    _invokeDepth++;
    try
    {
      SafeInvoke(invocation, static state => state.Invoke(), HandlerException);
    }
    finally
    {
      _invokeDepth--;
    }
  }

  // =========================================================================
  // Value-Type Invocation Structs (Closure Allocation Elimination)
  // =========================================================================
  //
  // In normal C#, writing `SafeInvoke(() => handler(arg1, arg2))` allocates a compiler-generated display class
  // instance (closure) on the managed heap on EVERY invocation.
  //
  // To keep callback dispatch truly zero-allocation on hot paths, we encapsulate the handler and arguments into
  // lightweight readonly structs and pass them by value into `SafeInvoke<TState>`. The lambda passed to SafeInvoke is
  // then purely static (`static state => state.Invoke()`), which the C# compiler caches as a single static delegate.

  private readonly unsafe struct EventInvocation
  {
    private readonly Action<AlpmEvent> _handler;
    private readonly _alpm_event_t* _native;
    private readonly ChildLifetime _lifetime;

    internal EventInvocation(Action<AlpmEvent> handler, _alpm_event_t* native, ChildLifetime lifetime)
    {
      _handler = handler;
      _native = native;
      _lifetime = lifetime;
    }

    internal void Invoke() => _handler(AlpmEvent.FromUnion(_native, _lifetime));
  }

  private readonly unsafe struct FetchInvocation
  {
    private readonly Func<string, string, bool, FetchResult> _handler;
    private readonly byte* _url;
    private readonly byte* _localPath;
    private readonly int _force;

    internal FetchInvocation(
      Func<string, string, bool, FetchResult> handler,
      byte* url,
      byte* localPath,
      int force)
    {
      _handler = handler;
      _url = url;
      _localPath = localPath;
      _force = force;
    }

    internal int Invoke()
    {
      var url = NativeString.FromNative((nint)_url) ?? "";
      var localPath = NativeString.FromNative((nint)_localPath) ?? "";
      return (int)_handler(url, localPath, _force != 0);
    }
  }

  private readonly struct ProgressInvocation
  {
    private readonly Action<AlpmProgressEvent> _handler;
    private readonly AlpmProgressEvent _event;

    internal ProgressInvocation(
      Action<AlpmProgressEvent> handler,
      ProgressType type,
      string packageName,
      int percent,
      int current,
      int total)
    {
      _handler = handler;
      _event = new AlpmProgressEvent(type, packageName, percent, current, total);
    }

    internal void Invoke() => _handler(_event);
  }

  private readonly struct DownloadInvocation
  {
    private readonly Action<string, AlpmDownloadEvent> _handler;
    private readonly string _filename;
    private readonly AlpmDownloadEvent _event;

    internal DownloadInvocation(
      Action<string, AlpmDownloadEvent> handler,
      string filename,
      AlpmDownloadEvent downloadEvent)
    {
      _handler = handler;
      _filename = filename;
      _event = downloadEvent;
    }

    internal void Invoke() => _handler(_filename, _event);
  }

  private readonly unsafe struct LogInvocation
  {
    private readonly LogMessageHandler _handler;
    private readonly LogLevel _level;
    private readonly byte* _format;
    private readonly void* _vaList;

    internal LogInvocation(LogMessageHandler handler, LogLevel level, byte* format, void* vaList)
    {
      _handler = handler;
      _level = level;
      _format = format;
      _vaList = vaList;
    }

    internal void Invoke() => LogMessageFormatter.Format(_format, _vaList, _handler, _level);
  }

  // =========================================================================
  // FFI Exception Isolation Barrier
  // =========================================================================

  /// <summary>
  /// Invokes a managed handler inside a protective barrier so exceptions never escape to native callers.
  /// </summary>
  /// <remarks>
  /// Under CoreCLR / Native AOT, an unhandled exception escaping an <see cref="UnmanagedCallersOnlyAttribute" /> method
  /// triggers a fatal fail-fast process termination because the native C stack frame has no managed exception handler.
  /// Exceptions are safely caught here and optionally routed to <paramref name="observer" />.
  /// </remarks>
  internal static void SafeInvoke<TState>(TState state, Action<TState> body, Action<Exception>? observer)
  {
    try
    {
      body(state);
    }
    catch (Exception ex)
    {
      NotifyHandlerException(observer, ex);
    }
  }

  /// <summary>
  /// <c>SafeInvoke</c> for callbacks that return an outcome code back to libalpm.
  /// </summary>
  /// <param name="state">State passed to the invocation body without creating a managed closure.</param>
  /// <param name="body">The handler invocation to protect.</param>
  /// <param name="onException">The fallback error code reported to native code when the managed body throws.</param>
  /// <param name="observer">The optional managed observer for the thrown exception.</param>
  internal static int SafeInvoke<TState>(
    TState state,
    Func<TState, int> body,
    int onException,
    Action<Exception>? observer)
  {
    try
    {
      return body(state);
    }
    catch (Exception ex)
    {
      NotifyHandlerException(observer, ex);
      return onException;
    }
  }

  /// <summary>
  /// Forwards caught exceptions to the managed observer hook while ensuring observer errors are swallowed as well.
  /// </summary>
  private static void NotifyHandlerException(Action<Exception>? observer, Exception ex)
  {
    try
    {
      observer?.Invoke(ex);
    }
    catch
    {
      // An exception thrown by the diagnostic observer itself must not escape the FFI boundary.
    }
  }
}
