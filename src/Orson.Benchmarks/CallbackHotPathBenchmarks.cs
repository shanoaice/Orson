using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;
using Orson.Alpm;
using Orson.Alpm.Bindings;

namespace Orson.Benchmarks;

/// <summary>Allocation guard for the callback paths that libalpm invokes most often.</summary>
[MemoryDiagnoser]
[ProcessCount(1)]
[WarmupCount(3)]
[IterationCount(10)]
public unsafe class CallbackHotPathBenchmarks
{
  private const string HarnessLibrary = "orson_callback_bench_shim";

  [DllImport(HarnessLibrary, EntryPoint = "orson_bench_invoke_log", CallingConvention = CallingConvention.Cdecl,
    ExactSpelling = true)]
  private static extern void InvokeLog(
    delegate* unmanaged[Cdecl]<void*, int, byte*, void*, void> callback,
    void* context,
    int level,
    byte* format,
    byte* text,
    int number);

  private AlpmSession _session = null!;
  private AlpmCallbacks _callbacks = null!;
  private GCHandle _callbackContext;
  private _alpm_event_t* _event;
  private _alpm_download_event_progress_t* _downloadProgress;
  private byte* _packageName;
  private byte* _filename;
  private byte* _format;
  private byte* _text;

  [GlobalSetup]
  public void Setup()
  {
    _session = BenchEnvironment.Open();
    _callbacks = _session.Callbacks;
    _callbacks.EventHandler = _ => { };
    _callbacks.ProgressHandler = _ => { };
    _callbacks.DownloadHandler = (_, _) => { };
    _callbacks.EnabledLogLevels = LogLevel.Warning;
    _callbacks.LogHandler = (_, _) => { };

    _callbackContext = GCHandle.Alloc(_callbacks);
    _event = (_alpm_event_t*)NativeMemory.AllocZeroed((nuint)sizeof(_alpm_event_t));
    _event->type_ = _alpm_event_type_t.ALPM_EVENT_CHECKDEPS_START;

    _downloadProgress =
      (_alpm_download_event_progress_t*)NativeMemory.AllocZeroed((nuint)sizeof(_alpm_download_event_progress_t));
    _downloadProgress->downloaded = new CLong(50);
    _downloadProgress->total = new CLong(100);

    _packageName = NativeString.ToNative("orson-progress-package");
    _filename = NativeString.ToNative("orson-download.pkg.tar.zst");
    _format = NativeString.ToNative("warning %s #%d");
    _text = NativeString.ToNative("callback benchmark");

    // Prime the one-slot caches so measured calls exercise their steady-state path.
    _callbacks.DispatchProgress(_alpm_progress_t.ALPM_PROGRESS_ADD_START, _packageName, 50, 1, 1);
    _callbacks.DispatchDownload(_filename, _alpm_download_event_type_t.ALPM_DOWNLOAD_PROGRESS, _downloadProgress);
    InvokeLogOnce();
  }

  [GlobalCleanup]
  public void Cleanup()
  {
    _callbackContext.Free();
    NativeMemory.Free(_event);
    NativeMemory.Free(_downloadProgress);
    NativeMemory.Free(_packageName);
    NativeMemory.Free(_filename);
    NativeMemory.Free(_format);
    NativeMemory.Free(_text);
    _session.Dispose();
  }

  [Benchmark]
  public void Event_StatelessDispatch() => _callbacks.DispatchEvent(_event);

  [Benchmark]
  public void Progress_RepeatedPackage() =>
    _callbacks.DispatchProgress(_alpm_progress_t.ALPM_PROGRESS_ADD_START, _packageName, 75, 1, 1);

  [Benchmark]
  public void Download_RepeatedFilename() =>
    _callbacks.DispatchDownload(_filename, _alpm_download_event_type_t.ALPM_DOWNLOAD_PROGRESS, _downloadProgress);

  [Benchmark]
  public void Log_AcceptedWarning() => InvokeLogOnce();

  private void InvokeLogOnce()
    => InvokeLog(
      &LogThunk,
      (void*)GCHandle.ToIntPtr(_callbackContext),
      (int)_alpm_loglevel_t.ALPM_LOG_WARNING,
      _format,
      _text,
      1);

  [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
  private static void LogThunk(void* context, int level, byte* format, void* vaList)
  {
    var callbacks = (AlpmCallbacks)GCHandle.FromIntPtr((nint)context).Target!;
    callbacks.DispatchLog((_alpm_loglevel_t)(uint)level, format, vaList);
  }
}
