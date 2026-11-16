using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Orson.Alpm.Bindings;
using Orson.Alpm.Tests.Fixtures;

namespace Orson.Alpm.Tests.Unit.Callbacks;

/// <summary>
/// The log callback is the one handler whose payload libalpm does not expand for us, so it is the only one that cannot be
/// covered by
/// <c>SafeInvoke</c>
/// tests alone. These drive the whole production path on a real handle: native <c>alpm_cb_log</c> → the log thunk →
/// <see cref="LogMessageFormatter" /> → <see cref="AlpmCallbacks.LogHandler" />.
/// </summary>
/// <remarks>
/// libalpm logs "unregistering database ..." at DEBUG while <c>alpm_release</c> runs, which is what these tests
/// ride on, so disposing the environment is what produces the messages. If a libalpm update stops logging there, the
/// trigger has to be replaced with another one; the assertions are deliberately about shape rather than wording, so they
/// do not break when libalpm rewords a message. The deterministic check of the <c>va_list</c> expansion itself lives in
/// <see cref="LogMessageFormatterTests" />.
/// </remarks>
[Collection("AlpmSessionSerial")]
public sealed class LogCallbackTests
{
  private const LogLevel AllLevels = LogLevel.Error | LogLevel.Warning | LogLevel.Debug | LogLevel.Function;
  private const string HarnessLibrary = "orson_log_shim_test";

  [DllImport(HarnessLibrary, EntryPoint = "orson_shim_test_invoke", CallingConvention = CallingConvention.Cdecl,
    ExactSpelling = true)]
  private static unsafe extern void Invoke(
    delegate* unmanaged[Cdecl]<void*, int, byte*, void*, void> callback,
    void* context,
    int level,
    byte* format,
    byte* text,
    int number,
    double fraction);

  [Fact]
  public unsafe void EnabledLogLevels_FilteringHappensBeforeTheHandler()
  {
    using var environment = new IsolatedAlpmEnvironment();
    var callbacks = environment.AlpmSession.Callbacks;
    var received = new List<LogLevel>();
    callbacks.LogHandler = (level, _) => received.Add(level);

    var format = (byte*)Marshal.StringToCoTaskMemUTF8("%s");
    var text = (byte*)Marshal.StringToCoTaskMemUTF8("debug message");
    var context = GCHandle.Alloc(callbacks);

    try
    {
      Invoke(
        &DispatchThunk,
        (void*)GCHandle.ToIntPtr(context),
        (int)_alpm_loglevel_t.ALPM_LOG_DEBUG,
        format,
        text,
        0,
        0);
      Assert.Empty(received);

      callbacks.EnabledLogLevels = LogLevel.Debug;
      Invoke(
        &DispatchThunk,
        (void*)GCHandle.ToIntPtr(context),
        (int)_alpm_loglevel_t.ALPM_LOG_DEBUG,
        format,
        text,
        0,
        0);

      Assert.Equal([LogLevel.Debug], received);
    }
    finally
    {
      context.Free();
      Marshal.FreeCoTaskMem((nint)format);
      Marshal.FreeCoTaskMem((nint)text);
    }
  }

  [Fact]
  public void LogHandler_ReceivesExpandedMessagesFromARealHandle()
  {
    var received = new List<(LogLevel Level, string Message)>();

    using (var environment = new IsolatedAlpmEnvironment())
    {
      environment.AlpmSession.Callbacks.EnabledLogLevels = AllLevels;
      environment.AlpmSession.Callbacks.LogHandler = (level, messageUtf8) =>
        received.Add((level, Encoding.UTF8.GetString(messageUtf8)));
      // Leaving the block disposes the handle, which is when libalpm logs.
    }

    Assert.NotEmpty(received);
    Assert.All(received, entry => Assert.False(string.IsNullOrEmpty(entry.Message),
      "an empty message means the (fmt, va_list) payload was not expanded"));
    Assert.All(received, entry => Assert.DoesNotContain("%s", entry.Message));
    Assert.All(received, entry => Assert.NotEqual(0u, (uint)entry.Level));
    Assert.All(received, entry => Assert.True((entry.Level & ~AllLevels) == 0,
      $"libalpm reported a log level outside _alpm_loglevel_t: {(uint)entry.Level}"));
  }

  /// <summary>
  /// A throwing log handler must not reach native code: the thunk is entered from libalpm with no managed frame
  /// above it, so an escaping exception would terminate the process instead of failing this test.
  /// </summary>
  [Fact]
  public void LogHandlerExceptions_AreReportedThroughTheObserver()
  {
    var observed = new List<Exception>();

    using (var environment = new IsolatedAlpmEnvironment())
    {
      environment.AlpmSession.Callbacks.EnabledLogLevels = AllLevels;
      environment.AlpmSession.Callbacks.LogHandler = (_, _) =>
        throw new InvalidOperationException("log handler failed");
      environment.AlpmSession.Callbacks.HandlerException = observed.Add;
    }

    Assert.Contains(observed, exception => exception.Message == "log handler failed");
  }

  [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
  private static unsafe void DispatchThunk(void* context, int level, byte* format, void* vaList)
  {
    var callbacks = (AlpmCallbacks)GCHandle.FromIntPtr((nint)context).Target!;
    callbacks.DispatchLog((_alpm_loglevel_t)(uint)level, format, vaList);
  }
}
