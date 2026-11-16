#pragma warning disable SYSLIB1054
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Orson.Alpm.Bindings;

namespace Orson.Alpm;

/// <summary>The phase a progress callback reports (libalpm's <c>_alpm_progress_t</c>).</summary>
/// <remarks>Indicates the active progress reporting stage during package operations.</remarks>
public enum ProgressType : uint
{
  /// <summary>Packages are being added (<c>ALPM_PROGRESS_ADD_START</c>).</summary>
  AddStart = 0,

  /// <summary>Packages are being upgraded (<c>ALPM_PROGRESS_UPGRADE_START</c>).</summary>
  UpgradeStart = 1,

  /// <summary>Packages are being downgraded (<c>ALPM_PROGRESS_DOWNGRADE_START</c>).</summary>
  DowngradeStart = 2,

  /// <summary>Packages are being reinstalled (<c>ALPM_PROGRESS_REINSTALL_START</c>).</summary>
  ReinstallStart = 3,

  /// <summary>Packages are being removed (<c>ALPM_PROGRESS_REMOVE_START</c>).</summary>
  RemoveStart = 4,

  /// <summary>File conflicts are being checked (<c>ALPM_PROGRESS_CONFLICTS_START</c>).</summary>
  ConflictsStart = 5,

  /// <summary>Disk space is being checked (<c>ALPM_PROGRESS_DISKSPACE_START</c>).</summary>
  DiskSpaceStart = 6,

  /// <summary>Package integrity is being checked (<c>ALPM_PROGRESS_INTEGRITY_START</c>).</summary>
  IntegrityStart = 7,

  /// <summary>Package targets are being loaded (<c>ALPM_PROGRESS_LOAD_START</c>).</summary>
  LoadStart = 8,

  /// <summary>The keyring is being checked (<c>ALPM_PROGRESS_KEYRING_START</c>).</summary>
  KeyringStart = 9
}

/// <summary>The severity of a log message (libalpm's <c>_alpm_loglevel_t</c>).</summary>
/// <remarks>
/// A bitmask, not a scale: libalpm calls <c>alpm_cb_log</c> once per message with a single flag, and consumers
/// that want a subset test the flags (pacman and paru both filter on their own side, since libalpm emits DEBUG and
/// FUNCTION messages regardless).
/// </remarks>
[Flags]
public enum LogLevel : uint
{
  /// <summary>No log level flags set (0).</summary>
  None = 0,

  /// <summary>An error message (<c>ALPM_LOG_ERROR</c>).</summary>
  Error = 1,

  /// <summary>A warning message (<c>ALPM_LOG_WARNING</c>).</summary>
  Warning = 2,

  /// <summary>A debug message (<c>ALPM_LOG_DEBUG</c>).</summary>
  Debug = 4,

  /// <summary>A function-call trace message (<c>ALPM_LOG_FUNCTION</c>).</summary>
  Function = 8
}

/// <summary>The outcome a fetch handler reports back to libalpm.</summary>
/// <remarks>
/// Replaces the bare <c>int</c> of <c>alpm_cb_fetch</c>, whose contract is "0 on success, 1 if the file exists and is
/// identical, -1 on error" (alpm.h). Any other value is not something libalpm defines, so returning an arbitrary number
/// should not be expressible.
/// </remarks>
public enum FetchResult
{
  /// <summary>The file was fetched (0).</summary>
  Success = 0,

  /// <summary>The local file already exists and is identical, so nothing was transferred (1).</summary>
  UpToDate = 1,

  /// <summary>The fetch failed (-1).</summary>
  Error = -1
}

/// <summary>
/// Holds the native callback context (ctx) and the user-facing handlers of an <see cref="AlpmSession" />
/// instance.
/// </summary>
/// <remarks>
/// Registration of native callbacks with libalpm is presence-driven: setting a non-null handler registers the
/// corresponding callback, while setting a handler to <see langword="null" /> unregisters it from the native handle. When
/// a callback is unregistered, libalpm's own default behavior applies directly:
/// <list type="bullet">
/// <item>
/// <description>Leaving <see cref="FetchHandler" /> unset allows libalpm to use its internal libcurl-based downloader.</description>
/// </item>
/// <item>
/// <description>Leaving question hooks unset lets libalpm proceed with its built-in default answers.</description>
/// </item>
/// <item>
/// <description>
/// <see cref="DownloadHandler" /> receives progress events emitted only by libalpm's internal downloader, and
/// is therefore inert whenever an external <see cref="FetchHandler" /> is active.
/// </description>
/// </item>
/// </list>
/// Handlers are configuration, not control flow. Mutating a handler from within an active callback invocation throws
/// <see cref="InvalidOperationException" /> to prevent native use-after-free and NULL dereference crashes in libalpm's
/// download and dispatch loops. Mutating a handler after the owning <see cref="AlpmSession" /> handle has been disposed
/// throws <see cref="ObjectDisposedException" />.
/// <para>
/// Handlers run synchronously on the thread libalpm invokes them from, which is the thread already running the transaction.
/// This library never chooses which thread handlers runs on, and they block libalpm for as long as they run.
/// </para>
/// <para>
/// Question hooks are even stricter. libalpm reads the answer as soon as the callback returns, so a
/// question must be answered inline, before returning. Answering from a continuation, a task, or another thread leaves
/// libalpm reading the default value instead of your intended one.
/// </para>
/// <para>
/// Every handler invocation is routed through <c>SafeInvoke</c>, which swallows handler exceptions at the FFI
/// boundary and reports them through <see cref="HandlerException" /> when that observer is set. The ctx handle is owned by
/// <see cref="AlpmSession" />, which releases it only once <c>alpm_release</c> has returned successfully.
/// </para>
/// </remarks>
public sealed partial class AlpmCallbacks
{
  /// <summary>Handler delegate for <see cref="InstallIgnorePackageQuestion" />.</summary>
  /// <param name="question">The question payload.</param>
  public delegate void InstallIgnorePackageHandler(ref InstallIgnorePackageQuestion question);

  /// <summary>Handler delegate for <see cref="ReplacePackageQuestion" />.</summary>
  /// <param name="question">The question payload.</param>
  public delegate void ReplacePackageHandler(ref ReplacePackageQuestion question);

  /// <summary>Handler delegate for <see cref="PackageConflictQuestion" />.</summary>
  /// <param name="question">The question payload.</param>
  public delegate void PackageConflictHandler(ref PackageConflictQuestion question);

  /// <summary>Handler delegate for <see cref="CorruptedPackageQuestion" />.</summary>
  /// <param name="question">The question payload.</param>
  public delegate void CorruptedPackageHandler(ref CorruptedPackageQuestion question);

  /// <summary>Handler delegate for <see cref="RemoveUnresolvablePackagesQuestion" />.</summary>
  /// <param name="question">The question payload.</param>
  public delegate void RemoveUnresolvablePackagesHandler(ref RemoveUnresolvablePackagesQuestion question);

  /// <summary>Handler delegate for <see cref="SelectProviderQuestion" />.</summary>
  /// <param name="question">The question payload.</param>
  public delegate void SelectProviderHandler(ref SelectProviderQuestion question);

  /// <summary>Handler delegate for <see cref="ImportKeyQuestion" />.</summary>
  /// <param name="question">The question payload.</param>
  public delegate void ImportKeyHandler(ref ImportKeyQuestion question);

  /// <summary>Receives one callback-scoped UTF-8 log message.</summary>
  /// <param name="level">The message severity.</param>
  /// <param name="messageUtf8">
  /// The UTF-8 message bytes. The span is valid only until this handler returns; copy it to retain the message.
  /// </param>
  public delegate void LogMessageHandler(LogLevel level, ReadOnlySpan<byte> messageUtf8);

  // Owning session and native ALPM handle reference
  private readonly AlpmSession _session;
  private readonly SafeAlpmHandle _handle;

  // Lifetime root and captured stamp to ensure callbacks are not reconfigured after session invalidation
  private readonly RootLifetime _rootLifetime;
  private readonly LifetimeStamp _stamp;

  // Native callback context passed to libalpm as `void* ctx`.
  // Uses a weak handle so libalpm retaining the pointer does not artificially prevent GC if session is abandoned,
  // while still allowing the native thunks to safely dereference the target instance.
  private WeakGCHandle<AlpmCallbacks> _ctxHandle;

  // Tracks active invocation depth on the current thread.
  // 0 indicates that no callback is active; > 0 indicates an active callback frame (or nested callbacks).
  private int _invokeDepth;

  // Tracks whether Dispose() has executed and native ctx resources have been released.
  private bool _detached;

  // Bitmask of log levels forwarded to LogHandler. Defaults to Error and Warning only to avoid formatting overhead.
  private LogLevel _enabledLogLevels = LogLevel.Error | LogLevel.Warning;

  /// <summary>
  /// Initializes a new callback coordinator for the owning ALPM session.
  /// </summary>
  internal unsafe AlpmCallbacks(AlpmSession session, SafeAlpmHandle alpmHandle, RootLifetime lifetime)
  {
    _session = session;
    _handle = alpmHandle;
    _rootLifetime = lifetime;
    _eventLifetime = lifetime.CreateChild("the ALPM event callback");
    _stamp = lifetime.Capture();
    _ctxHandle = new WeakGCHandle<AlpmCallbacks>(this);

    // Question callbacks are registered permanently on the native handle because libalpm requires an active
    // question callback if prompts are to be handled. Individual question types are presence-driven in managed code.
    RegisterQuestionCallback();
  }

  /// <summary>
  /// Ensures callback handlers can only be reconfigured while the session is alive and not currently executing
  /// inside an active callback frame (mutating callbacks mid-execution causes native memory corruption in libalpm).
  /// </summary>
  private void ConfigureGuard()
  {
    ObjectDisposedException.ThrowIf(_detached, this);
    _stamp.ThrowIfStale();
    if (_invokeDepth != 0)
    {
      throw new InvalidOperationException(
        "Callback handlers are configuration, not control flow, and cannot be modified from within a handler.");
    }
  }

  /// <summary>
  /// Validates libalpm option return codes when setting or clearing native function pointers.
  /// </summary>
  private void ThrowIfError(int err, [CallerMemberName] string? operation = null)
  {
    if (err == 0) return;
    GC.KeepAlive(this);
    throw AlpmFailure.Of(_handle, $"set {operation ?? "callback"}").ToException();
  }

  /// <summary>
  /// Releases the callback ctx handle and returns rented string buffers to shared pools.
  /// Deliberately internal: owned by AlpmSession, which releases it only once alpm_release returns.
  /// </summary>
  internal void Dispose()
  {
    if (_detached) return;
    _detached = true;
    _progressPackageNames.Release();
    if (_ctxHandle.IsAllocated) _ctxHandle.Dispose();
  }

  // =========================================================================
  // Public Callback Configuration Properties
  // =========================================================================

  /// <summary>
  /// Receives libalpm event notifications. Setting a non-null delegate registers the native event callback; setting
  /// <see langword="null" /> unregisters it.
  /// </summary>
  public unsafe Action<AlpmEvent>? EventHandler
  {
    get;
    set
    {
      ConfigureGuard();
      if (value == null)
      {
        ThrowIfError(NativeMethods.alpm_option_set_eventcb(_handle, null, null));
        field = null;
        return;
      }

      field = value;
      var ctx = (void*)WeakGCHandle<AlpmCallbacks>.ToIntPtr(_ctxHandle);
      ThrowIfError(NativeMethods.alpm_option_set_eventcb(_handle, &EventAgent, ctx));
    }
  }

  /// <summary>
  /// Custom file retriever callback. Setting a non-null delegate registers an external download agent with libalpm;
  /// setting <see langword="null" /> restores libalpm's built-in libcurl downloader.
  /// </summary>
  public unsafe Func<string, string, bool, FetchResult>? FetchHandler
  {
    get;
    set
    {
      ConfigureGuard();
      if (value == null)
      {
        ThrowIfError(NativeMethods.alpm_option_set_fetchcb(_handle, null, null));
        field = null;
        return;
      }

      field = value;
      var ctx = (void*)WeakGCHandle<AlpmCallbacks>.ToIntPtr(_ctxHandle);
      ThrowIfError(NativeMethods.alpm_option_set_fetchcb(_handle, &FetchAgent, ctx));
    }
  }

  /// <summary>
  /// Receives progress events during downloads handled by libalpm's internal downloader. Setting a non-null
  /// delegate registers the native download callback; setting <see langword="null" /> unregisters it. Note: this callback
  /// only fires when libalpm downloads files itself, and is inactive while <see cref="FetchHandler" /> is configured.
  /// The payload is a value type, so dispatching it does not allocate a managed event object.
  /// </summary>
  public unsafe Action<string, AlpmDownloadEvent>? DownloadHandler
  {
    get;
    set
    {
      ConfigureGuard();
      if (value == null)
      {
        ThrowIfError(NativeMethods.alpm_option_set_dlcb(_handle, null, null));
        field = null;
        return;
      }

      field = value;
      var ctx = (void*)WeakGCHandle<AlpmCallbacks>.ToIntPtr(_ctxHandle);
      ThrowIfError(NativeMethods.alpm_option_set_dlcb(_handle, &DownloadAgent, ctx));
    }
  }

  /// <summary>
  /// Receives progress alerts during database and transaction operations. Setting a non-null delegate registers the
  /// native progress callback; setting <see langword="null" /> unregisters it.
  /// </summary>
  /// <remarks>
  /// This is the most frequent callback: libalpm reports once per file entry while it extracts or removes a
  /// package. The payload is an <see cref="AlpmProgressEvent" /> value, so the handler call allocates nothing for the
  /// payload itself.
  /// </remarks>
  public unsafe Action<AlpmProgressEvent>? ProgressHandler
  {
    get;
    set
    {
      ConfigureGuard();
      if (value == null)
      {
        ThrowIfError(NativeMethods.alpm_option_set_progresscb(_handle, null, null));
        field = null;
        return;
      }

      field = value;
      var ctx = (void*)WeakGCHandle<AlpmCallbacks>.ToIntPtr(_ctxHandle);
      ThrowIfError(NativeMethods.alpm_option_set_progresscb(_handle, &ProgressAgent, ctx));
      GC.KeepAlive(this);
    }
  }

  /// <summary>
  /// Gets or sets the native log levels forwarded to <see cref="LogHandler" />.
  /// </summary>
  /// <remarks>
  /// Filtering happens before the message is formatted. The default excludes <see cref="LogLevel.Debug" /> and
  /// <see cref="LogLevel.Function" />; set <see cref="LogLevel.None" /> to suppress all log-handler calls.
  /// </remarks>
  public LogLevel EnabledLogLevels
  {
    get => _enabledLogLevels;
    set
    {
      ConfigureGuard();
      _enabledLogLevels = value;
    }
  }

  /// <summary>
  /// Receives libalpm's log messages, already expanded from the <c>(fmt, va_list)</c> pair the native callback is
  /// given. Setting a non-null delegate registers the native log callback; setting <see langword="null" /> unregisters it.
  /// </summary>
  /// <remarks>
  /// The message is UTF-8 and remains valid only for the duration of the handler call. Copy it before retaining it or
  /// crossing an asynchronous boundary. A message that cannot be expanded arrives as an empty span.
  /// </remarks>
  public unsafe LogMessageHandler? LogHandler
  {
    get;
    set
    {
      ConfigureGuard();
      if (value == null)
      {
        ThrowIfError(NativeMethods.alpm_option_set_logcb(_handle, null, null));
        field = null;
        return;
      }

      field = value;
      var ctx = (void*)WeakGCHandle<AlpmCallbacks>.ToIntPtr(_ctxHandle);
      ThrowIfError(NativeMethods.alpm_option_set_logcb(_handle, &LogAgent, ctx));
    }
  }

  /// <summary>
  /// Receives prompts when attempting to install a package marked in IgnorePkg.
  /// </summary>
  public InstallIgnorePackageHandler? OnInstallIgnorePackage
  {
    get;
    set
    {
      ConfigureGuard();
      field = value;
    }
  }

  /// <summary>
  /// Receives prompts when an existing package is to be replaced by another package.
  /// </summary>
  public ReplacePackageHandler? OnReplacePackage
  {
    get;
    set
    {
      ConfigureGuard();
      field = value;
    }
  }

  /// <summary>
  /// Receives prompts when two packages conflict during transaction preparation.
  /// </summary>
  public PackageConflictHandler? OnPackageConflict
  {
    get;
    set
    {
      ConfigureGuard();
      field = value;
    }
  }

  /// <summary>
  /// Receives prompts when a downloaded package file is found to be corrupted.
  /// </summary>
  public CorruptedPackageHandler? OnCorruptedPackage
  {
    get;
    set
    {
      ConfigureGuard();
      field = value;
    }
  }

  /// <summary>
  /// Receives prompts when unresolvable dependencies occur in a transaction (libalpm's <c>ALPM_QUESTION_REMOVE_PKGS</c>),
  /// asking whether to remove those packages from the transaction.
  /// </summary>
  public RemoveUnresolvablePackagesHandler? OnRemoveUnresolvablePackages
  {
    get;
    set
    {
      ConfigureGuard();
      field = value;
    }
  }

  /// <summary>
  /// Receives prompts when multiple providers satisfy a dependency and a selection is required.
  /// </summary>
  public SelectProviderHandler? OnSelectProvider
  {
    get;
    set
    {
      ConfigureGuard();
      field = value;
    }
  }

  /// <summary>
  /// Receives prompts when an unknown PGP key needs to be imported into the keyring.
  /// </summary>
  public ImportKeyHandler? OnImportKey
  {
    get;
    set
    {
      ConfigureGuard();
      field = value;
    }
  }

  /// <summary>
  /// Optional observer for exceptions thrown by user handlers (<see cref="EventHandler" />,
  /// <see cref="FetchHandler" />, <see cref="OnInstallIgnorePackage" />, <see cref="DownloadHandler" />,
  /// <see cref="ProgressHandler" />, <see cref="LogHandler" />).
  /// </summary>
  /// <remarks>
  /// This is a managed observer hook and does not register a native callback with libalpm. Handler exceptions never
  /// cross the FFI boundary: they are caught by the wrapper's native callbacks and, when this is set, forwarded here so they
  /// can be logged. Exceptions thrown by this observer are swallowed as well, so it cannot terminate the process either.
  /// Leave it null to ignore handler failures.
  /// </remarks>
  public Action<Exception>? HandlerException { get; set; }
}
