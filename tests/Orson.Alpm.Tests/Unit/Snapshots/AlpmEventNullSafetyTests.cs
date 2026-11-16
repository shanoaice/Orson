using Orson.Alpm.Bindings;

namespace Orson.Alpm.Tests.Unit.Snapshots;

/// <summary>
/// The other half of <see cref="ManagedSnapshotTests" />: a payload is a copy taken inside the callback, so the
/// branches that carry a package have to answer <c>null</c> - not a view over a null pointer - when libalpm reports one.
/// Every case here leaves the package pointers null on purpose.
/// </summary>
public sealed unsafe class AlpmEventNullSafetyTests
{
  [Fact]
  public void StatelessEvents_ReturnCanonicalSingletons()
  {
    var cases = new[]
    {
      (_alpm_event_type_t.ALPM_EVENT_CHECKDEPS_START, (AlpmEvent)AlpmEvent.CheckDepsStart.Instance),
      (_alpm_event_type_t.ALPM_EVENT_CHECKDEPS_DONE, (AlpmEvent)AlpmEvent.CheckDepsDone.Instance),
      (_alpm_event_type_t.ALPM_EVENT_FILECONFLICTS_START, (AlpmEvent)AlpmEvent.FileConflictsStart.Instance),
      (_alpm_event_type_t.ALPM_EVENT_FILECONFLICTS_DONE, (AlpmEvent)AlpmEvent.FileConflictsDone.Instance),
      (_alpm_event_type_t.ALPM_EVENT_RESOLVEDEPS_START, (AlpmEvent)AlpmEvent.ResolveDepsStart.Instance),
      (_alpm_event_type_t.ALPM_EVENT_RESOLVEDEPS_DONE, (AlpmEvent)AlpmEvent.ResolveDepsDone.Instance),
      (_alpm_event_type_t.ALPM_EVENT_INTERCONFLICTS_START, (AlpmEvent)AlpmEvent.InterConflictsStart.Instance),
      (_alpm_event_type_t.ALPM_EVENT_INTERCONFLICTS_DONE, (AlpmEvent)AlpmEvent.InterConflictsDone.Instance),
      (_alpm_event_type_t.ALPM_EVENT_TRANSACTION_START, (AlpmEvent)AlpmEvent.TransactionStart.Instance),
      (_alpm_event_type_t.ALPM_EVENT_TRANSACTION_DONE, (AlpmEvent)AlpmEvent.TransactionDone.Instance),
      (_alpm_event_type_t.ALPM_EVENT_INTEGRITY_START, (AlpmEvent)AlpmEvent.IntegrityStart.Instance),
      (_alpm_event_type_t.ALPM_EVENT_INTEGRITY_DONE, (AlpmEvent)AlpmEvent.IntegrityDone.Instance),
      (_alpm_event_type_t.ALPM_EVENT_LOAD_START, (AlpmEvent)AlpmEvent.LoadStart.Instance),
      (_alpm_event_type_t.ALPM_EVENT_LOAD_DONE, (AlpmEvent)AlpmEvent.LoadDone.Instance),
      (_alpm_event_type_t.ALPM_EVENT_DB_RETRIEVE_START, (AlpmEvent)AlpmEvent.RetrieveStart.Instance),
      (_alpm_event_type_t.ALPM_EVENT_DB_RETRIEVE_DONE, (AlpmEvent)AlpmEvent.RetrieveDone.Instance),
      (_alpm_event_type_t.ALPM_EVENT_DB_RETRIEVE_FAILED, (AlpmEvent)AlpmEvent.RetrieveFailed.Instance),
      (_alpm_event_type_t.ALPM_EVENT_DISKSPACE_START, (AlpmEvent)AlpmEvent.DiskSpaceStart.Instance),
      (_alpm_event_type_t.ALPM_EVENT_DISKSPACE_DONE, (AlpmEvent)AlpmEvent.DiskSpaceDone.Instance),
      (_alpm_event_type_t.ALPM_EVENT_KEYRING_START, (AlpmEvent)AlpmEvent.KeyringStart.Instance),
      (_alpm_event_type_t.ALPM_EVENT_KEYRING_DONE, (AlpmEvent)AlpmEvent.KeyringDone.Instance),
      (_alpm_event_type_t.ALPM_EVENT_KEY_DOWNLOAD_START, (AlpmEvent)AlpmEvent.KeyDownloadStart.Instance),
      (_alpm_event_type_t.ALPM_EVENT_KEY_DOWNLOAD_DONE, (AlpmEvent)AlpmEvent.KeyDownloadDone.Instance),
    };

    var native = stackalloc _alpm_event_t[1];
    foreach (var (nativeType, expected) in cases)
    {
      native->type_ = nativeType;
      Assert.Same(expected, AlpmEvent.FromUnion(native, CreateLifetime()));
    }
  }

  [Fact]
  public void PacnewCreated_WithNullNativePackages_YieldsNullViews()
  {
    var native = stackalloc _alpm_event_t[1];
    native->type_ = _alpm_event_type_t.ALPM_EVENT_PACNEW_CREATED;
    native->pacnew_created.oldpkg = null;
    native->pacnew_created.newpkg = null;
    native->pacnew_created.file = null;
    native->pacnew_created.from_noupgrade = 1;

    var ev = Assert.IsType<AlpmEvent.PacnewCreated>(AlpmEvent.FromUnion(native, CreateLifetime()));

    Assert.True(ev.FromNoUpgrade);
    Assert.Null(ev.OldPackage);
    Assert.Null(ev.NewPackage);
    Assert.Equal("", ev.File);
  }

  [Fact]
  public void PacsaveCreated_WithNullNativePackage_YieldsNullView()
  {
    var native = stackalloc _alpm_event_t[1];
    native->type_ = _alpm_event_type_t.ALPM_EVENT_PACSAVE_CREATED;
    native->pacsave_created.oldpkg = null;
    native->pacsave_created.file = null;

    var ev = Assert.IsType<AlpmEvent.PacsaveCreated>(AlpmEvent.FromUnion(native, CreateLifetime()));

    Assert.Null(ev.OldPackage);
    Assert.Equal("", ev.File);
  }

  [Fact]
  public void PackageOperationStart_WithNullNativePackages_YieldsNullViews()
  {
    var native = stackalloc _alpm_event_t[1];
    native->type_ = _alpm_event_type_t.ALPM_EVENT_PACKAGE_OPERATION_START;
    native->package_operation.newpkg = null;
    native->package_operation.oldpkg = null;
    native->package_operation.operation = _alpm_package_operation_t.ALPM_PACKAGE_REMOVE;

    var ev = Assert.IsType<AlpmEvent.PackageOperationStart>(AlpmEvent.FromUnion(native, CreateLifetime()));

    Assert.Null(ev.NewPackage);
    Assert.Null(ev.OldPackage);
    Assert.Equal(PackageOperation.Remove, ev.Operation);
  }

  [Fact]
  public void PackageOperationDone_WithNullNativePackages_YieldsNullViews()
  {
    var native = stackalloc _alpm_event_t[1];
    native->type_ = _alpm_event_type_t.ALPM_EVENT_PACKAGE_OPERATION_DONE;
    native->package_operation.newpkg = null;
    native->package_operation.oldpkg = null;
    native->package_operation.operation = _alpm_package_operation_t.ALPM_PACKAGE_INSTALL;

    var ev = Assert.IsType<AlpmEvent.PackageOperationDone>(AlpmEvent.FromUnion(native, CreateLifetime()));

    Assert.Null(ev.NewPackage);
    Assert.Null(ev.OldPackage);
    Assert.Equal(PackageOperation.Install, ev.Operation);
  }

  private static ChildLifetime CreateLifetime()
    => Lifetime.CreateRoot(new object(), "test root").CreateChild("test callback");
}
