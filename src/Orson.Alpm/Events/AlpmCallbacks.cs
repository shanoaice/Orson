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

  /// <summary>File conflicts are being checked (<c>ALPM_PROGRESS_CONFLICTS_START</c> ).</summary>
  ConflictsStart = 5,

  /// <summary>Disk space is being checked (<c>ALPM_PROGRESS_DISKSPACE_START</c>).</summary>
  DiskSpaceStart = 6,

  /// <summary>Package integrity is being checked ( <c>ALPM_PROGRESS_INTEGRITY_START</c>).</summary>
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
public sealed class AlpmCallbacks
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

  private readonly AlpmSession _session;

  // The ALPM context, guarded behind a SafeHandle so it is not disposed while
  // callbacks are still active.
  private readonly SafeAlpmHandle _handle;
  private int _invokeDepth;
  private bool _detached;

  // The ALPM handle's root lifetime token
  private readonly RootLifetime _rootLifetime;
  private readonly LifetimeStamp _stamp;

  // do not Dispose this before the callback class has been disposed
  // otherwise it will screw up the callbacks
  private WeakGCHandle<AlpmCallbacks> _ctxHandle;

  [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
  private static unsafe void EventAgent(void* ctx, _alpm_event_t* eventT)
  {
    if (ctx == null) return;
    try
    {
      var success = WeakGCHandle<AlpmCallbacks>.FromIntPtr((nint)ctx).TryGetTarget(out var callback);
      if (!success || callback == null) return;
      callback._invokeDepth++;
      var callbackLifetime = callback._rootLifetime.CreateChild("AlpmCallbacks.EventAgent");
      try
      {
        SafeInvoke(() => callback.EventHandler?.Invoke(AlpmEvent.FromUnion(eventT, callbackLifetime)),
          callback.HandlerException);
      }
      finally
      {
        callback._invokeDepth--;
        callbackLifetime.Invalidate("callback frame ended");
      }
    }
    catch
    {
      // This catch is to guard against a race condition where the callback is disposed while the native code is still
      // calling back. In that case, the WeakGCHandle will throw an exception, which we want to ignore.
    }
  }

  [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
  private static unsafe int FetchAgent(void* ctx, byte* url, byte* localPath, int force)
  {
    // fails without a valid ctx
    if (ctx == null) return -1;
    try
    {
      var success = WeakGCHandle<AlpmCallbacks>.FromIntPtr((nint)ctx).TryGetTarget(out var callback);
      if (!success || callback == null) return -1;
      callback._invokeDepth++;
      try
      {
        return SafeInvoke(() =>
        {
          var urlString = NativeString.FromNative((IntPtr)url) ?? "";
          var localPathString = NativeString.FromNative((IntPtr)localPath) ?? "";

          return (int)(callback.FetchHandler?.Invoke(urlString, localPathString, force != 0) ?? FetchResult.Error);
        }, (int)FetchResult.Error, callback.HandlerException);
      }
      finally
      {
        callback._invokeDepth--;
      }
    }
    catch
    {
      // This catch is to guard against a race condition where the callback is disposed while the native code is still
      // calling back. In that case, the WeakGCHandle will throw an exception, which we want to identify as a failure.
      return -1;
    }
  }

  [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
  private static unsafe void QuestionAgent(void* ctx, _alpm_question_t* questionT)
  {
    if (ctx == null) return;
    try
    {
      var success = WeakGCHandle<AlpmCallbacks>.FromIntPtr((nint)ctx).TryGetTarget(out var callback);
      if (!success || callback == null) return;
      callback._invokeDepth++;
      try
      {
        callback.DispatchQuestion(questionT);
      }
      finally
      {
        callback._invokeDepth--;
      }
    }
    catch
    {
      // This catch is to guard against a race condition where the callback is disposed while the native code is still
      // calling back. In that case, the WeakGCHandle will throw an exception, which we want to ignore.
    }
  }

  private unsafe void DispatchQuestion(_alpm_question_t* questionT)
  {
    try
    {
      switch (questionT->type_)
      {
        case _alpm_question_type_t.ALPM_QUESTION_INSTALL_IGNOREPKG:
          if (OnInstallIgnorePackage is { } installIgnoreHandler)
          {
            var lifetime = ResolvePackageLifetime(questionT->install_ignorepkg.pkg);
            var question = new InstallIgnorePackageQuestion(questionT, lifetime);
            installIgnoreHandler(ref question);
          }

          break;

        case _alpm_question_type_t.ALPM_QUESTION_REPLACE_PKG:
          if (OnReplacePackage is { } replaceHandler)
          {
            var oldPackageLifetime = ResolvePackageLifetime(questionT->replace.oldpkg);
            var newPackageLifetime = ResolvePackageLifetime(questionT->replace.newpkg);
            var question = new ReplacePackageQuestion(questionT, oldPackageLifetime, newPackageLifetime);
            replaceHandler(ref question);
          }

          break;

        case _alpm_question_type_t.ALPM_QUESTION_CONFLICT_PKG:
          if (OnPackageConflict is { } conflictHandler)
          {
            var conflict = questionT->conflict.conflict;
            var package1Lifetime =
              ResolvePackageLifetime(conflict != null ? conflict->package1 : null);
            var package2Lifetime =
              ResolvePackageLifetime(conflict != null ? conflict->package2 : null);
            var question = new PackageConflictQuestion(questionT, package1Lifetime, package2Lifetime);
            conflictHandler(ref question);
          }

          break;

        case _alpm_question_type_t.ALPM_QUESTION_CORRUPTED_PKG:
          if (OnCorruptedPackage is { } corruptedHandler)
          {
            var question = new CorruptedPackageQuestion(questionT);
            corruptedHandler(ref question);
          }

          break;

        case _alpm_question_type_t.ALPM_QUESTION_REMOVE_PKGS:
          if (OnRemoveUnresolvablePackages is { } removeUnresolvablePackagesHandler)
          {
            var packageViews = ResolvePackageViews(questionT->remove_pkgs.packages);
            var question = new RemoveUnresolvablePackagesQuestion(questionT, packageViews);
            removeUnresolvablePackagesHandler(ref question);
          }

          break;

        case _alpm_question_type_t.ALPM_QUESTION_SELECT_PROVIDER:
          if (OnSelectProvider is { } selectProviderHandler)
          {
            var providerViews = ResolvePackageViews(questionT->select_provider.providers);
            var question = new SelectProviderQuestion(questionT, providerViews);
            selectProviderHandler(ref question);
          }

          break;

        case _alpm_question_type_t.ALPM_QUESTION_IMPORT_KEY:
          if (OnImportKey is { } importKeyHandler)
          {
            var question = new ImportKeyQuestion(questionT);
            importKeyHandler(ref question);
          }

          break;

        default:
          throw new NotImplementedException($"Unhandled question type: {questionT->type_}");
      }
    }
    catch (Exception ex)
    {
      NotifyHandlerException(HandlerException, ex);
    }
  }

  [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
  private static unsafe void ProgressAgent(void* ctx, _alpm_progress_t progress, byte* pkg, int percent, nuint howmany,
    nuint current)
  {
    if (ctx == null) return;
    try
    {
      var success = WeakGCHandle<AlpmCallbacks>.FromIntPtr((nint)ctx).TryGetTarget(out var callback);
      if (!success || callback == null) return;
      callback._invokeDepth++;
      try
      {
        SafeInvoke(
          () => callback.ProgressHandler?.Invoke(new AlpmProgressEvent(
            (ProgressType)(uint)progress,
            NativeString.FromNative((nint)pkg) ?? "",
            percent,
            (int)current,
            (int)howmany)), callback.HandlerException);
      }
      finally
      {
        callback._invokeDepth--;
      }
    }
    catch
    {
      // This catch is to guard against a race condition where the callback is disposed while the native code is still
      // calling back. In that case, the WeakGCHandle will throw an exception, which we want to ignore.
    }
  }

  [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
  private static unsafe void DownloadAgent(void* ctx, byte* filename, _alpm_download_event_type_t eventType, void* data)
  {
    if (ctx == null) return;
    try
    {
      var success = WeakGCHandle<AlpmCallbacks>.FromIntPtr((nint)ctx).TryGetTarget(out var callback);
      if (!success || callback == null) return;
      callback._invokeDepth++;
      try
      {
        SafeInvoke(
          () => callback.DownloadHandler?.Invoke(NativeString.FromNative((nint)filename) ?? "",
            AlpmDownloadEvent.FromUnion(eventType, data)), callback.HandlerException);
      }
      finally
      {
        callback._invokeDepth--;
      }
    }
    catch
    {
      // This catch is to guard against a race condition where the callback is disposed while the native code is still calling back. In that case, the WeakGCHandle will throw an exception, which we want to ignore.
    }
  }

  /// <summary>
  /// Log thunk. This is the one libalpm callback that does not receive expanded arguments: it gets a
  /// <c>(fmt, va_list)</c> pair, so the message is expanded by <see cref="LogMessageFormatter" />. The <c>va_list</c> is
  /// delivered as an opaque pointer, which is currently the "correct" representation for all .NET supported platforms.
  /// We hope that this stays true at least until Microsoft adds native __va_list marshaling to C#.
  /// </summary>
  [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
  private static unsafe void LogAgent(void* ctx, _alpm_loglevel_t level, byte* fmt, void* vaList)
  {
    if (ctx == null) return;
    try
    {
      var success = WeakGCHandle<AlpmCallbacks>.FromIntPtr((nint)ctx).TryGetTarget(out var callback);
      if (!success || callback == null) return;
      callback._invokeDepth++;
      try
      {
        SafeInvoke(
          () => callback.LogHandler?.Invoke((LogLevel)(uint)level, LogMessageFormatter.Format(fmt, vaList) ?? ""),
          callback.HandlerException);
      }
      finally
      {
        callback._invokeDepth--;
      }
    }
    catch
    {
      // This catch is to guard against a race condition where the callback is disposed while the native code is still
      // calling back. In that case, the WeakGCHandle will throw an exception, which we want to ignore.
    }
  }

  internal unsafe AlpmCallbacks(AlpmSession session, SafeAlpmHandle alpmHandle, RootLifetime lifetime)
  {
    _session = session;
    _handle = alpmHandle;
    _rootLifetime = lifetime;
    _stamp = lifetime.Capture();
    _ctxHandle = new WeakGCHandle<AlpmCallbacks>(this);

    var ctx = (void*)WeakGCHandle<AlpmCallbacks>.ToIntPtr(_ctxHandle);
    ThrowIfError(NativeMethods.alpm_option_set_questioncb(_handle, &QuestionAgent, ctx));
  }

  /// <summary>
  /// Resolves the lifetime domain for an unmanaged package pointer.
  /// </summary>
  /// <param name="package">The unmanaged package pointer to resolve.</param>
  /// <returns>The lifetime domain that owns the package.</returns>
  private unsafe ChildLifetime ResolvePackageLifetime(_alpm_pkg_t* package)
  {
    if (package == null)
    {
      return _session.CurrentTransaction?.Lifetime
             ?? _rootLifetime.GetLifetimeTokenForHandle(package, "a package");
    }

    var packageDatabase = NativeMethods.alpm_pkg_get_db(package);
    if (packageDatabase == null)
    {
      return _session.CurrentTransaction?.Lifetime
             ?? _rootLifetime.GetLifetimeTokenForHandle(package, "a file-loaded package");
    }

    if (packageDatabase == NativeMethods.alpm_get_localdb(_handle))
    {
      return _session.LocalDatabaseLifetime;
    }

    var dbName = NativeString.FromNative((nint)NativeMethods.alpm_db_get_name(packageDatabase));
    var target = string.IsNullOrEmpty(dbName) ? "a sync database" : $"the sync database {dbName}";
    return _rootLifetime.GetLifetimeTokenForHandle(packageDatabase, target);
  }

  /// <summary>
  /// Resolves an unmanaged list of package pointers into borrowed <see cref="PackageView" /> instances.
  /// </summary>
  private unsafe List<PackageView> ResolvePackageViews(_alpm_list_t* packages)
  {
    List<PackageView> results = [];
    while (packages != null)
    {
      var package = (_alpm_pkg_t*)packages->data;
      if (package != null)
      {
        var lifetime = ResolvePackageLifetime(package);
        results.Add(new PackageView(package, lifetime));
      }

      packages = packages->next;
    }

    return results;
  }

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

  private void ThrowIfError(int err, [CallerMemberName] string? operation = null)
  {
    if (err == 0) return;
    GC.KeepAlive(this);
    throw AlpmFailure.Of(_handle, $"set {operation ?? "callback"}").ToException();
  }

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
  /// Receives progress events during downloads handled by libalpm's internal downloader. Setting a non-null
  /// delegate registers the native download callback; setting <see langword="null" /> unregisters it. Note: this callback
  /// only fires when libalpm downloads files itself, and is inactive while <see cref="FetchHandler" /> is configured.
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
  /// Receives libalpm's log messages, already expanded from the <c>(fmt, va_list)</c> pair the native callback is
  /// given. Setting a non-null delegate registers the native log callback; setting <see langword="null" /> unregisters it.
  /// </summary>
  /// <remarks>
  /// A message that could not be expanded (a null format string, or an allocation failure) arrives as an empty
  /// string rather than as <see langword="null" />.
  /// </remarks>
  public unsafe Action<LogLevel, string>? LogHandler
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
  /// Optional observer for exceptions thrown by the user handlers ( <see cref="EventHandler" />,
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

  /// <summary>
  /// Invokes a user handler body so that no exception escapes to the native caller, which has no managed frame left
  /// to catch it.
  /// </summary>
  /// <param name="body">The handler invocation to protect.</param>
  /// <param name="observer">
  /// When non-null and <paramref name="body" /> throws, called with the thrown exception. Exceptions
  /// thrown by the observer are swallowed too.
  /// </param>
  internal static void SafeInvoke(Action body, Action<Exception>? observer)
  {
    try
    {
      body();
    }
    catch (Exception ex)
    {
      NotifyHandlerException(observer, ex);
    }
  }

  /// <summary>
  /// <c>SafeInvoke</c> for handlers that return a value: <paramref name="onException" /> is returned when the body
  /// throws. The fetch thunk uses -1 so a failing handler is reported to libalpm as an error instead of a bogus success.
  /// </summary>
  internal static int SafeInvoke(Func<int> body, int onException, Action<Exception>? observer)
  {
    try
    {
      return body();
    }
    catch (Exception ex)
    {
      NotifyHandlerException(observer, ex);
      return onException;
    }
  }

  private static void NotifyHandlerException(Action<Exception>? observer, Exception ex)
  {
    try
    {
      observer?.Invoke(ex);
    }
    catch
    {
      // An observer must not be able to escape the FFI boundary either.
    }
  }

  /// <summary>
  /// Releases the callback ctx handle. Deliberately <c>internal</c>: the handle is owned by
  /// <see cref="AlpmSession" />, which releases it once <c>alpm_release</c> has returned successfully. A public entry point
  /// would let a consumer invalidate the ctx while native code may still call back.
  /// </summary>
  /// <remarks>
  /// Not called when <c>alpm_release</c> failed: the handle it belongs to is still alive then (it is what leaked),
  /// and its native thunks would dereference this ctx handle - see <see cref="AlpmSession.Dispose()" />.
  /// </remarks>
  internal void Dispose()
  {
    if (_detached) return;
    _detached = true;
    if (_ctxHandle.IsAllocated) _ctxHandle.Dispose();
  }
}
