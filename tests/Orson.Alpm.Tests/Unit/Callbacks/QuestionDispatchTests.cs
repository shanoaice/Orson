using System.Runtime.InteropServices;
using Orson.Alpm.Bindings;
using Orson.Alpm.Tests.Fixtures;

namespace Orson.Alpm.Tests.Unit.Callbacks;

/// <summary>
/// Verifies the end-to-end question callback dispatch pipeline:
/// native <c>alpm_cb_question</c> function pointer → <c>QuestionAgent</c> unmanaged thunk →
/// <see cref="WeakGCHandle{T}" /> resolution → typed <c>AlpmCallbacks.On*</c> delegate routing →
/// package lifetime domain resolution → in-place native answer write-back → exception shielding.
/// </summary>
public sealed unsafe class QuestionDispatchTests : IDisposable
{
  private readonly IsolatedAlpmEnvironment _environment = new();

  public void Dispose() => _environment.Dispose();

  private AlpmSession Session => _environment.AlpmSession;
  private AlpmCallbacks Callbacks => Session.Callbacks;
  private string PackageDirectory => _environment.PackageDirectory;

  private delegate* unmanaged[Cdecl]<void*, _alpm_question_t*, void> QuestionCb
    => NativeMethods.alpm_option_get_questioncb(Session.Handle);

  private void* QuestionCtx
    => NativeMethods.alpm_option_get_questioncb_ctx(Session.Handle);

  // =========================================================================
  // 1. Dispatch & Write-Back Matrix
  // =========================================================================

  [Fact]
  public void Dispatch_InstallIgnorePackage_RoutesAndWritesBack()
  {
    var native = new _alpm_question_t
    {
      type_ = _alpm_question_type_t.ALPM_QUESTION_INSTALL_IGNOREPKG
    };
    native.install_ignorepkg.install = 0;
    native.install_ignorepkg.pkg = null;

    var invoked = false;
    Callbacks.OnInstallIgnorePackage = (ref q) =>
    {
      invoked = true;
      q.Install = true;
    };

    QuestionCb(QuestionCtx, &native);

    Assert.True(invoked);
    Assert.Equal(1, native.install_ignorepkg.install);
  }

  [Fact]
  public void Dispatch_ReplacePackage_RoutesAndWritesBack()
  {
    var native = new _alpm_question_t
    {
      type_ = _alpm_question_type_t.ALPM_QUESTION_REPLACE_PKG
    };
    native.replace.replace = 0;
    native.replace.newpkg = null;
    native.replace.oldpkg = null;
    native.replace.newdb = null;

    var invoked = false;
    Callbacks.OnReplacePackage = (ref q) =>
    {
      invoked = true;
      q.Replace = true;
    };

    QuestionCb(QuestionCtx, &native);

    Assert.True(invoked);
    Assert.Equal(1, native.replace.replace);
  }

  [Fact]
  public void Dispatch_PackageConflict_RoutesAndWritesBack()
  {
    var native = new _alpm_question_t
    {
      type_ = _alpm_question_type_t.ALPM_QUESTION_CONFLICT_PKG
    };
    native.conflict.remove = 0;
    native.conflict.conflict = null;

    var invoked = false;
    Callbacks.OnPackageConflict = (ref q) =>
    {
      invoked = true;
      q.Remove = true;
    };

    QuestionCb(QuestionCtx, &native);

    Assert.True(invoked);
    Assert.Equal(1, native.conflict.remove);
  }

  [Fact]
  public void Dispatch_CorruptedPackage_RoutesAndWritesBack()
  {
    var native = new _alpm_question_t
    {
      type_ = _alpm_question_type_t.ALPM_QUESTION_CORRUPTED_PKG
    };
    native.corrupted.remove = 0;
    native.corrupted.filepath = null;
    native.corrupted.reason = _alpm_errno_t.ALPM_ERR_OK;

    var invoked = false;
    Callbacks.OnCorruptedPackage = (ref q) =>
    {
      invoked = true;
      q.Remove = true;
    };

    QuestionCb(QuestionCtx, &native);

    Assert.True(invoked);
    Assert.Equal(1, native.corrupted.remove);
  }

  [Fact]
  public void Dispatch_RemoveUnresolvablePackages_RoutesAndWritesBack()
  {
    var native = new _alpm_question_t
    {
      type_ = _alpm_question_type_t.ALPM_QUESTION_REMOVE_PKGS
    };
    native.remove_pkgs.skip = 0;
    native.remove_pkgs.packages = null;

    var invoked = false;
    Callbacks.OnRemoveUnresolvablePackages = (ref q) =>
    {
      invoked = true;
      q.Skip = true;
    };

    QuestionCb(QuestionCtx, &native);

    Assert.True(invoked);
    Assert.Equal(1, native.remove_pkgs.skip);
  }

  [Fact]
  public void Dispatch_SelectProvider_RoutesAndWritesBack()
  {
    var native = new _alpm_question_t
    {
      type_ = _alpm_question_type_t.ALPM_QUESTION_SELECT_PROVIDER
    };
    native.select_provider.use_index = 0;
    native.select_provider.providers = null;
    native.select_provider.depend = null;

    var invoked = false;
    Callbacks.OnSelectProvider = (ref q) =>
    {
      invoked = true;
      q.UseIndex = 2;
    };

    QuestionCb(QuestionCtx, &native);

    Assert.True(invoked);
    Assert.Equal(2, native.select_provider.use_index);
  }

  [Fact]
  public void Dispatch_ImportKey_RoutesAndWritesBack()
  {
    var native = new _alpm_question_t
    {
      type_ = _alpm_question_type_t.ALPM_QUESTION_IMPORT_KEY
    };
    native.import_key.import = 0;
    native.import_key.uid = null;
    native.import_key.fingerprint = null;

    var invoked = false;
    Callbacks.OnImportKey = (ref q) =>
    {
      invoked = true;
      q.Import = true;
    };

    QuestionCb(QuestionCtx, &native);

    Assert.True(invoked);
    Assert.Equal(1, native.import_key.import);
  }

  // =========================================================================
  // 2. Unset Handler Behavior (No-Op)
  // =========================================================================

  [Fact]
  public void Dispatch_WhenHandlerUnset_KeepsDefaultAnswer()
  {
    var native = new _alpm_question_t
    {
      type_ = _alpm_question_type_t.ALPM_QUESTION_INSTALL_IGNOREPKG
    };
    native.install_ignorepkg.install = 0;
    native.install_ignorepkg.pkg = null;

    Callbacks.OnInstallIgnorePackage = null;

    QuestionCb(QuestionCtx, &native);

    Assert.Equal(0, native.install_ignorepkg.install);
  }

  // =========================================================================
  // 3. Lifetime Domain Resolution
  // =========================================================================

  [Fact]
  public void Dispatch_ResolvesFallbackLifetime_WhenPackageHasNoDatabase()
  {
    using var loaded = Load("orson-dispatch-fallback-pkg", file: "usr/bin/probe");
    var native = new _alpm_question_t
    {
      type_ = _alpm_question_type_t.ALPM_QUESTION_INSTALL_IGNOREPKG
    };
    native.install_ignorepkg.install = 0;
    native.install_ignorepkg.pkg = loaded.BackingStruct;

    PackageView? observedPackage = null;
    Callbacks.OnInstallIgnorePackage = (ref q) => observedPackage = q.Package;

    QuestionCb(QuestionCtx, &native);

    Assert.NotNull(observedPackage);
    Assert.Equal("orson-dispatch-fallback-pkg", observedPackage.Name);
  }

  [Fact]
  public void Dispatch_ResolvesTransactionLifetime_WhenTransactionIsActive()
  {
    var tx = Session.BeginTransaction();
    using var loaded = Load("orson-dispatch-tx-pkg", file: "usr/bin/probe");

    var native = new _alpm_question_t
    {
      type_ = _alpm_question_type_t.ALPM_QUESTION_INSTALL_IGNOREPKG
    };
    native.install_ignorepkg.install = 0;
    native.install_ignorepkg.pkg = loaded.BackingStruct;

    PackageView? observedPackage = null;
    Callbacks.OnInstallIgnorePackage = (ref q) => observedPackage = q.Package;

    QuestionCb(QuestionCtx, &native);

    Assert.NotNull(observedPackage);
    Assert.Equal("orson-dispatch-tx-pkg", observedPackage.Name);

    // Disposing transaction invalidates its lifetime domain; accessing the package view now throws
    tx.Dispose();

    Assert.Throws<AlpmLifetimeException>(() => observedPackage.Name);
  }

  // =========================================================================
  // 4. Exception Shielding & Defensive Handling
  // =========================================================================

  [Fact]
  public void Dispatch_CatchesUserHandlerException_AndReportsToObserver()
  {
    Exception? observed = null;
    Callbacks.HandlerException = ex => observed = ex;

    Callbacks.OnInstallIgnorePackage = (ref _) =>
      throw new InvalidOperationException("handler failure");

    var native = new _alpm_question_t
    {
      type_ = _alpm_question_type_t.ALPM_QUESTION_INSTALL_IGNOREPKG
    };
    native.install_ignorepkg.install = 0;
    native.install_ignorepkg.pkg = null;

    // Must not crash or propagate out of native thunk
    QuestionCb(QuestionCtx, &native);

    Assert.NotNull(observed);
    Assert.IsType<InvalidOperationException>(observed);
    Assert.Equal("handler failure", observed.Message);
  }

  [Fact]
  public void Dispatch_UnknownQuestionType_ReportsNotImplementedException()
  {
    Exception? observed = null;
    Callbacks.HandlerException = ex => observed = ex;

    var native = new _alpm_question_t
    {
      type_ = (_alpm_question_type_t)9999
    };

    // Must not crash or propagate out of native thunk
    QuestionCb(QuestionCtx, &native);

    Assert.NotNull(observed);
    Assert.IsType<NotImplementedException>(observed);
  }

  // =========================================================================
  // Helpers
  // =========================================================================

  private LoadedPackage Load(string name, string file)
    => Session.LoadPackage(
      PackageArchive.Create(PackageDirectory, new PackageSpec(name) { File = file }),
      full: true,
      SigLevel.UseDefault);
}
