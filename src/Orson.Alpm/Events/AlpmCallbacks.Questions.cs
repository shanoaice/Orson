#pragma warning disable SYSLIB1054
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Orson.Alpm.Bindings;

namespace Orson.Alpm;

public sealed partial class AlpmCallbacks
{
  /// <summary>
  /// Registers the permanent question callback with libalpm.
  /// </summary>
  /// <remarks>
  /// Unlike progress or event callbacks, libalpm expects a question callback whenever prompt handling is active.
  /// Rather than unhooking the native callback when managed delegates are null, we keep QuestionAgent registered
  /// and let libalpm fall back to its internal defaults if no managed handler is assigned for a given question type.
  /// </remarks>
  private unsafe void RegisterQuestionCallback()
  {
    var ctx = (void*)WeakGCHandle<AlpmCallbacks>.ToIntPtr(_ctxHandle);
    ThrowIfError(NativeMethods.alpm_option_set_questioncb(_handle, &QuestionAgent, ctx));
  }

  /// <summary>
  /// Native thunk invoked by libalpm when a transaction requires user input or approval.
  /// </summary>
  [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
  private static unsafe void QuestionAgent(void* ctx, _alpm_question_t* questionT)
  {
    if (!TryGetContext(ctx, out var callback)) return;

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

  /// <summary>
  /// Demultiplexes a native question union into the corresponding managed question structure.
  /// </summary>
  /// <remarks>
  /// Question delegates take their argument by <see langword="ref" /> because libalpm reads the answer directly
  /// from the native struct fields immediately after the callback returns.
  /// Handlers must answer synchronously before returning.
  /// </remarks>
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
      // Catch exceptions here so unhandled managed errors do not escape across the native FFI boundary.
      NotifyHandlerException(HandlerException, ex);
    }
  }

  /// <summary>
  /// Resolves the lifetime domain for an unmanaged package pointer passed into a question payload.
  /// </summary>
  /// <remarks>
  /// Packages appearing in questions originate from three possible sources:
  /// 1. The local installed database (tied to LocalDatabaseLifetime).
  /// 2. A registered sync repository database (tied to the sync database's child lifetime).
  /// 3. A file-loaded archive or in-flight transaction package (tied to the transaction or root lifetime).
  /// Correct domain resolution ensures that borrowed PackageViews invalidate immediately if their parent database or transaction closes.
  /// </remarks>
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
  /// Traverses an unmanaged <c>alpm_list_t</c> of package pointers, resolving lifetimes and wrapping each into a
  /// borrowed <see cref="PackageView" />.
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
}
