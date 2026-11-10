using System.Runtime.InteropServices;
using Orson.Alpm.Bindings;
using Orson.Alpm.Tests.Fixtures;

namespace Orson.Alpm.Tests.Unit.Callbacks;

/// <summary>
/// Question payloads are stack-bound ref structs over native callback unions.
/// These tests verify reading question properties, mutating answers back to native memory,
/// lifetime invalidation of borrowed package views, and explicit snapshot survival.
/// </summary>
public sealed unsafe class QuestionStructTests : IDisposable
{
  private readonly IsolatedAlpmEnvironment _environment = new();

  public void Dispose() => _environment.Dispose();

  private string PackageDirectory => _environment.PackageDirectory;

  // =========================================================================
  // 1. InstallIgnorePackageQuestion
  // =========================================================================

  [Fact]
  public void InstallIgnorePackage_ReadsPropertiesFromNativeUnion()
  {
    using var pkg = Load("orson-payload-default", file: "usr/bin/probe");

    var frameLifetime = _environment.AlpmSession.RootLifetime.CreateChild("test frame");

    var native = new _alpm_question_t
    {
      type_ = _alpm_question_type_t.ALPM_QUESTION_INSTALL_IGNOREPKG,
    };
    native.install_ignorepkg.install = 0;
    native.install_ignorepkg.pkg = pkg.BackingStruct;

    var question = new InstallIgnorePackageQuestion(&native, frameLifetime);

    Assert.Equal("orson-payload-default", question.Package.Name);
    Assert.NotNull(question.Package.Files);
    Assert.Contains(question.Package.Files, file => file.Name == "usr/bin/probe");
    Assert.False(question.Install);

    native.install_ignorepkg.install = 1;
    Assert.True(question.Install);
  }

  [Fact]
  public void InstallIgnorePackage_AnswerWritesBackToNativeUnion()
  {
    using var pkg = Load("orson-disarm-pkg", file: "usr/bin/probe");
    var native = new _alpm_question_t
    {
      type_ = _alpm_question_type_t.ALPM_QUESTION_INSTALL_IGNOREPKG
    };

    native.install_ignorepkg.install = 0;
    native.install_ignorepkg.pkg = pkg.BackingStruct;
    var frame = _environment.AlpmSession.RootLifetime.CreateChild("frame");
    var question = new InstallIgnorePackageQuestion(&native, frame);

    Assert.False(question.Install);

    // Setting answer updates both managed property and native memory
    question.Install = true;
    Assert.True(question.Install);
    Assert.Equal(1, native.install_ignorepkg.install);
  }

  [Fact]
  public void InstallIgnorePackage_PackageViewInvalidatesWhenLifetimeDies()
  {
    using var pkg = Load("orson-payload-frame-dies", file: "usr/bin/probe");
    var frameLifetime = _environment.AlpmSession.RootLifetime.CreateChild("test frame");

    var native = new _alpm_question_t
    {
      type_ = _alpm_question_type_t.ALPM_QUESTION_INSTALL_IGNOREPKG
    };
    native.install_ignorepkg.install = 0;
    native.install_ignorepkg.pkg = pkg.BackingStruct;

    var question = new InstallIgnorePackageQuestion(&native, frameLifetime);

    Assert.Equal("orson-payload-frame-dies", question.Package.Name);

    // Frame ends
    frameLifetime.Invalidate("test frame completed");

    var package = question.Package;

    // Reading package now throws AlpmLifetimeException
    Assert.Throws<AlpmLifetimeException>(() => package.Name);
  }

  // =========================================================================
  // 2. ReplacePackageQuestion
  // =========================================================================

  [Fact]
  public void ReplacePackage_ReadsPropertiesFromNativeUnion()
  {
    using var oldPkg = Load("orson-payload-oldpkg", file: "usr/bin/probe");
    using var newPkg = Load("orson-payload-newpkg", file: "usr/bin/probe");

    var frameLifetime = _environment.AlpmSession.RootLifetime.CreateChild("test frame");

    var native = new _alpm_question_t
    {
      type_ = _alpm_question_type_t.ALPM_QUESTION_REPLACE_PKG
    };
    native.replace.replace = 0;
    native.replace.newpkg = newPkg.BackingStruct;
    native.replace.oldpkg = oldPkg.BackingStruct;
    native.replace.newdb = null;

    var question = new ReplacePackageQuestion(&native, frameLifetime, frameLifetime);

    // 1. Verify prefetched scalar properties
    Assert.Equal("orson-payload-oldpkg", question.OldPackageName);
    Assert.Equal("orson-payload-newpkg", question.NewPackageName);
    Assert.Equal(PackageArchive.Version, question.OldPackageVersion);
    Assert.Equal(PackageArchive.Version, question.NewPackageVersion);
    Assert.Null(question.NewPackageDatabase);
    Assert.False(question.Replace);

    // 2. Verify lazy PackageView properties
    Assert.NotNull(question.OldPackage);
    Assert.Equal("orson-payload-oldpkg", question.OldPackage.Name);
    Assert.NotNull(question.NewPackage);
    Assert.Equal("orson-payload-newpkg", question.NewPackage.Name);

    // 3. When native answer field is set, managed property reflects it
    native.replace.replace = 1;
    Assert.True(question.Replace);
  }

  [Fact]
  public void ReplacePackage_AnswerWritesBackToNativeUnion()
  {
    var frameLifetime = _environment.AlpmSession.RootLifetime.CreateChild("test frame");

    var native = new _alpm_question_t
    {
      type_ = _alpm_question_type_t.ALPM_QUESTION_REPLACE_PKG
    };
    native.replace.replace = 0;

    var question = new ReplacePackageQuestion(&native, frameLifetime, frameLifetime);

    Assert.False(question.Replace);

    question.Replace = true;
    Assert.True(question.Replace);
    Assert.Equal(1, native.replace.replace);
  }

  [Fact]
  public void ReplacePackage_PackageViewsInvalidateWhenLifetimesDie()
  {
    using var oldPkg = Load("orson-payload-oldpkg", file: "usr/bin/probe");
    using var newPkg = Load("orson-payload-newpkg", file: "usr/bin/probe");

    var frameLifetime = _environment.AlpmSession.RootLifetime.CreateChild("test frame");

    var native = new _alpm_question_t
    {
      type_ = _alpm_question_type_t.ALPM_QUESTION_REPLACE_PKG
    };
    native.replace.replace = 0;
    native.replace.newpkg = newPkg.BackingStruct;
    native.replace.oldpkg = oldPkg.BackingStruct;
    native.replace.newdb = null;

    var question = new ReplacePackageQuestion(&native, frameLifetime, frameLifetime);

    Assert.Equal("orson-payload-oldpkg", question.OldPackage.Name);
    Assert.Equal("orson-payload-newpkg", question.NewPackage.Name);

    // Frame ends
    frameLifetime.Invalidate("test frame completed");

    // Reading package now throws AlpmLifetimeException
    var oldPackage = question.OldPackage;
    var newPackage = question.NewPackage;
    Assert.Throws<AlpmLifetimeException>(() => oldPackage.Name);
    Assert.Throws<AlpmLifetimeException>(() => newPackage.Name);
  }

  // =========================================================================
  // 3. PackageConflictQuestion
  // =========================================================================

  [Fact]
  public void PackageConflict_ReadsPropertiesFromNativeUnion()
  {
    using var pkg1 = Load("orson-payload-pkg1", file: "usr/bin/probe");
    using var pkg2 = Load("orson-payload-pkg2", file: "usr/bin/probe");

    var frameLifetime = _environment.AlpmSession.RootLifetime.CreateChild("test frame");

    fixed (byte* conflictName = "libconflict\0"u8,
                 conflictVersion = ">=1.0.0\0"u8,
                 conflictDesc = "conflicts with provided feature\0"u8)
    {
      var nativeReason = new _alpm_depend_t
      {
        name = conflictName,
        version = conflictVersion,
        desc = conflictDesc
      };

      var nativeConflict = new _alpm_conflict_t
      {
        package1 = pkg1.BackingStruct,
        package2 = pkg2.BackingStruct,
        reason = &nativeReason
      };

      var native = new _alpm_question_t
      {
        type_ = _alpm_question_type_t.ALPM_QUESTION_CONFLICT_PKG
      };
      native.conflict.conflict = &nativeConflict;
      native.conflict.remove = 0;

      // 1. Full payload with packages and conflict reason
      var question = new PackageConflictQuestion(&native, frameLifetime, frameLifetime);

      Assert.Equal("orson-payload-pkg1", question.Package1Name);
      Assert.Equal("orson-payload-pkg2", question.Package2Name);
      Assert.Equal(PackageArchive.Version, question.Package1Version);
      Assert.Equal(PackageArchive.Version, question.Package2Version);
      Assert.Equal("libconflict", question.ConflictName);
      Assert.Equal(">=1.0.0", question.ConflictVersion);
      Assert.Equal("conflicts with provided feature", question.ConflictDescription);
      Assert.False(question.Remove);

      Assert.NotNull(question.Package1);
      Assert.Equal("orson-payload-pkg1", question.Package1.Name);
      Assert.Contains(question.Package1.Files, file => file.Name == "usr/bin/probe");
      Assert.NotNull(question.Package2);
      Assert.Equal("orson-payload-pkg2", question.Package2.Name);
      Assert.Contains(question.Package2.Files, file => file.Name == "usr/bin/probe");

      // 2. Reuse packages: when reason is null, conflict details are null
      nativeConflict.reason = null;
      var noReasonQuestion = new PackageConflictQuestion(&native, frameLifetime, frameLifetime);

      Assert.Equal("orson-payload-pkg1", noReasonQuestion.Package1Name);
      Assert.Equal("orson-payload-pkg2", noReasonQuestion.Package2Name);
      Assert.Null(noReasonQuestion.ConflictName);
      Assert.Null(noReasonQuestion.ConflictVersion);
      Assert.Null(noReasonQuestion.ConflictDescription);
      Assert.False(noReasonQuestion.Remove);

      // 3. When conflict pointer itself is null, all package/conflict details are null
      native.conflict.conflict = null;
      var nullConflictQuestion = new PackageConflictQuestion(&native, frameLifetime, frameLifetime);

      Assert.Null(nullConflictQuestion.Package1Name);
      Assert.Null(nullConflictQuestion.Package1Version);
      Assert.Null(nullConflictQuestion.Package2Name);
      Assert.Null(nullConflictQuestion.Package2Version);
      Assert.Null(nullConflictQuestion.ConflictName);
      Assert.Null(nullConflictQuestion.ConflictVersion);
      Assert.Null(nullConflictQuestion.ConflictDescription);

      // 4. When native answer field is set, managed property reflects it
      native.conflict.remove = 1;
      Assert.True(nullConflictQuestion.Remove);
    }
  }

  [Fact]
  public void PackageConflict_AnswerWritesBackToNativeUnion()
  {
    var frameLifetime = _environment.AlpmSession.RootLifetime.CreateChild("test frame");

    var native = new _alpm_question_t
    {
      type_ = _alpm_question_type_t.ALPM_QUESTION_CONFLICT_PKG
    };
    native.conflict.remove = 0;
    native.conflict.conflict = null;

    var question = new PackageConflictQuestion(&native, frameLifetime, frameLifetime);

    Assert.False(question.Remove);

    question.Remove = true;
    Assert.True(question.Remove);
    Assert.Equal(1, native.conflict.remove);
  }

  [Fact]
  public void PackageConflict_PackageViewsInvalidateWhenLifetimesDie()
  {
    using var pkg1 = Load("orson-payload-pkg1", file: "usr/bin/probe");
    using var pkg2 = Load("orson-payload-pkg2", file: "usr/bin/probe");

    var frameLifetime = _environment.AlpmSession.RootLifetime.CreateChild("test frame");

    var nativeConflict = new _alpm_conflict_t
    {
      package1 = pkg1.BackingStruct,
      package2 = pkg2.BackingStruct,
      reason = null
    };

    var native = new _alpm_question_t
    {
      type_ = _alpm_question_type_t.ALPM_QUESTION_CONFLICT_PKG
    };
    native.conflict.remove = 0;
    native.conflict.conflict = &nativeConflict;

    var question = new PackageConflictQuestion(&native, frameLifetime, frameLifetime);

    Assert.Equal("orson-payload-pkg1", question.Package1.Name);
    Assert.Equal("orson-payload-pkg2", question.Package2.Name);

    // Frame ends
    frameLifetime.Invalidate("test frame completed");

    // Reading package now throws AlpmLifetimeException
    var package1 = question.Package1;
    var package2 = question.Package2;
    Assert.Throws<AlpmLifetimeException>(() => package1.Name);
    Assert.Throws<AlpmLifetimeException>(() => package2.Name);
  }

  // =========================================================================
  // 4. CorruptedPackageQuestion
  // =========================================================================

  [Fact]
  public void CorruptedPackage_ReadsPropertiesFromNativeUnion()
  {
    fixed (byte* filepath = "/var/cache/pacman/pkg/test.pkg.tar.zst\0"u8)
    {
      var native = new _alpm_question_t
      {
        type_ = _alpm_question_type_t.ALPM_QUESTION_CORRUPTED_PKG
      };
      native.corrupted.remove = 0;
      native.corrupted.filepath = filepath;
      native.corrupted.reason = _alpm_errno_t.ALPM_ERR_PKG_INVALID;

      var question = new CorruptedPackageQuestion(&native);

      Assert.Equal("/var/cache/pacman/pkg/test.pkg.tar.zst", question.FilePath);
      Assert.Equal((uint)_alpm_errno_t.ALPM_ERR_PKG_INVALID, question.ReasonCode);
      Assert.Equal(ErrnoMapper.StrError(_alpm_errno_t.ALPM_ERR_PKG_INVALID), question.Reason);
      Assert.False(question.Remove);

      native.corrupted.remove = 1;
      Assert.True(question.Remove);
    }
  }

  [Fact]
  public void CorruptedPackage_AnswerWritesBackToNativeUnion()
  {
    fixed (byte* filepath = "/var/cache/pacman/pkg/test.pkg.tar.zst\0"u8)
    {
      var native = new _alpm_question_t
      {
        type_ = _alpm_question_type_t.ALPM_QUESTION_CORRUPTED_PKG
      };
      native.corrupted.remove = 0;
      native.corrupted.filepath = filepath;
      native.corrupted.reason = _alpm_errno_t.ALPM_ERR_PKG_INVALID;

      var question = new CorruptedPackageQuestion(&native);
      Assert.False(question.Remove);

      question.Remove = true;
      Assert.True(question.Remove);
      Assert.Equal(1, native.corrupted.remove);
    }
  }

  // =========================================================================
  // 5. RemoveUnresolvablePackagesQuestion (alpm_list_t)
  // =========================================================================

  [Fact]
  public void RemoveUnresolvablePackages_ReadsPackagesFromNativeList()
  {
    using var pkg = Load("orson-payload-unresolvable", file: "usr/bin/probe");
    var frameLifetime = _environment.AlpmSession.RootLifetime.CreateChild("test frame");

    _alpm_list_t* packages = null;
    packages = NativeMethods.alpm_list_add(packages, pkg.BackingStruct);
    try
    {
      var native = new _alpm_question_t
      {
        type_ = _alpm_question_type_t.ALPM_QUESTION_REMOVE_PKGS
      };
      native.remove_pkgs.skip = 0;
      native.remove_pkgs.packages = packages;

      var packageViews = new[] { new PackageView(pkg.BackingStruct, frameLifetime) };
      var question = new RemoveUnresolvablePackagesQuestion(&native, packageViews);

      var item = Assert.Single(question.Packages);
      Assert.Equal("orson-payload-unresolvable", item.Name);
      Assert.NotNull(item.Files);
      Assert.Contains(item.Files, file => file.Name == "usr/bin/probe");
      Assert.False(question.Skip);

      native.remove_pkgs.skip = 1;
      Assert.True(question.Skip);
    }
    finally
    {
      NativeMethods.alpm_list_free(packages);
    }
  }

  [Fact]
  public void RemoveUnresolvablePackages_AnswerWritesBackToNativeUnion()
  {
    var native = new _alpm_question_t
    {
      type_ = _alpm_question_type_t.ALPM_QUESTION_REMOVE_PKGS
    };
    native.remove_pkgs.skip = 0;
    native.remove_pkgs.packages = null;

    var question = new RemoveUnresolvablePackagesQuestion(&native, []);
    Assert.False(question.Skip);

    question.Skip = true;
    Assert.True(question.Skip);
  }

  [Fact]
  public void RemoveUnresolvablePackages_PackageViewsInvalidateWhenLifetimesDie()
  {
    using var pkg = Load("orson-payload-unresolvable-die", file: "usr/bin/probe");
    var frameLifetime = _environment.AlpmSession.RootLifetime.CreateChild("test frame");

    _alpm_list_t* packages = null;
    packages = NativeMethods.alpm_list_add(packages, pkg.BackingStruct);
    try
    {
      var native = new _alpm_question_t
      {
        type_ = _alpm_question_type_t.ALPM_QUESTION_REMOVE_PKGS
      };
      native.remove_pkgs.skip = 0;
      native.remove_pkgs.packages = packages;

      var packageViews = new[] { new PackageView(pkg.BackingStruct, frameLifetime) };
      var question = new RemoveUnresolvablePackagesQuestion(&native, packageViews);

      var item = Assert.Single(question.Packages);
      Assert.Equal("orson-payload-unresolvable-die", item.Name);

      // Frame ends
      frameLifetime.Invalidate("test frame completed");

      // Reading package now throws AlpmLifetimeException
      Assert.Throws<AlpmLifetimeException>(() => item.Name);
    }
    finally
    {
      NativeMethods.alpm_list_free(packages);
    }
  }

  // =========================================================================
  // 6. SelectProviderQuestion (alpm_list_t & depend)
  // =========================================================================

  [Fact]
  public void SelectProvider_ReadsPropertiesAndProvidersFromNativeUnion()
  {
    using var pkg1 = Load("orson-payload-prov1", file: "usr/bin/probe");
    using var pkg2 = Load("orson-payload-prov2", file: "usr/bin/probe");

    var frameLifetime = _environment.AlpmSession.RootLifetime.CreateChild("test frame");

    fixed (byte* depName = "virtual-dep\0"u8,
                 depVersion = ">=2.0\0"u8,
                 depDesc = "a virtual dependency\0"u8)
    {
      var nativeDepend = new _alpm_depend_t
      {
        name = depName,
        version = depVersion,
        desc = depDesc
      };

      _alpm_list_t* providers = null;
      providers = NativeMethods.alpm_list_add(providers, pkg1.BackingStruct);
      providers = NativeMethods.alpm_list_add(providers, pkg2.BackingStruct);
      try
      {
        var native = new _alpm_question_t
        {
          type_ = _alpm_question_type_t.ALPM_QUESTION_SELECT_PROVIDER
        };
        native.select_provider.use_index = 0;
        native.select_provider.providers = providers;
        native.select_provider.depend = &nativeDepend;

        var providerViews = new[]
        {
          new PackageView(pkg1.BackingStruct, frameLifetime),
          new PackageView(pkg2.BackingStruct, frameLifetime)
        };
        var question = new SelectProviderQuestion(&native, providerViews);

        Assert.Equal("virtual-dep", question.DependencyName);
        Assert.Equal(">=2.0", question.DependencyVersion);
        Assert.Equal("a virtual dependency", question.DependencyDescription);

        Assert.Equal(2, question.Providers.Count);
        Assert.Equal("orson-payload-prov1", question.Providers[0].Name);
        Assert.Equal("orson-payload-prov2", question.Providers[1].Name);
        Assert.Equal(PackageArchive.Version, question.Providers[0].Version.ToString());
        Assert.Equal(PackageArchive.Version, question.Providers[1].Version.ToString());
        Assert.Contains(question.Providers[0].Files, file => file.Name == "usr/bin/probe");
        Assert.Contains(question.Providers[1].Files, file => file.Name == "usr/bin/probe");

        Assert.Equal(0, question.UseIndex);

        native.select_provider.use_index = 1;
        Assert.Equal(1, question.UseIndex);
      }
      finally
      {
        NativeMethods.alpm_list_free(providers);
      }
    }
  }

  [Fact]
  public void SelectProvider_AnswerWritesBackToNativeUnion()
  {
    var native = new _alpm_question_t
    {
      type_ = _alpm_question_type_t.ALPM_QUESTION_SELECT_PROVIDER
    };
    native.select_provider.use_index = 0;
    native.select_provider.providers = null;
    native.select_provider.depend = null;

    var question = new SelectProviderQuestion(&native, []);
    Assert.Equal(0, question.UseIndex);

    question.UseIndex = 1;
    Assert.Equal(1, question.UseIndex);
    Assert.Equal(1, native.select_provider.use_index);
  }

  [Fact]
  public void SelectProvider_ProviderViewsInvalidateWhenLifetimesDie()
  {
    using var pkg = Load("orson-payload-prov-die", file: "usr/bin/probe");
    var frameLifetime = _environment.AlpmSession.RootLifetime.CreateChild("test frame");

    _alpm_list_t* providers = null;
    providers = NativeMethods.alpm_list_add(providers, pkg.BackingStruct);
    try
    {
      var native = new _alpm_question_t
      {
        type_ = _alpm_question_type_t.ALPM_QUESTION_SELECT_PROVIDER
      };
      native.select_provider.use_index = 0;
      native.select_provider.providers = providers;
      native.select_provider.depend = null;

      var providerViews = new[] { new PackageView(pkg.BackingStruct, frameLifetime) };
      var question = new SelectProviderQuestion(&native, providerViews);

      var item = Assert.Single(question.Providers);
      Assert.Equal("orson-payload-prov-die", item.Name);

      // Frame ends
      frameLifetime.Invalidate("test frame completed");

      // Reading package now throws AlpmLifetimeException
      Assert.Throws<AlpmLifetimeException>(() => item.Name);
    }
    finally
    {
      NativeMethods.alpm_list_free(providers);
    }
  }

  // =========================================================================
  // 7. ImportKeyQuestion
  // =========================================================================

  [Fact]
  public void ImportKey_ReadsPropertiesFromNativeUnion()
  {
    fixed (byte* uid = "Test User <test@example.com>\0"u8,
                 fpr = "1234567890ABCDEF1234567890ABCDEF12345678\0"u8)
    {
      var native = new _alpm_question_t
      {
        type_ = _alpm_question_type_t.ALPM_QUESTION_IMPORT_KEY
      };
      native.import_key.import = 0;
      native.import_key.uid = uid;
      native.import_key.fingerprint = fpr;

      var question = new ImportKeyQuestion(&native);

      Assert.Equal("Test User <test@example.com>", question.Uid);
      Assert.Equal("1234567890ABCDEF1234567890ABCDEF12345678", question.Fingerprint);
      Assert.False(question.Import);

      native.import_key.import = 1;
      Assert.True(question.Import);
    }
  }

  [Fact]
  public void ImportKey_AnswerWritesBackToNativeUnion()
  {
    fixed (byte* uid = "Test User <test@example.com>\0"u8,
                 fpr = "1234567890ABCDEF1234567890ABCDEF12345678\0"u8)
    {
      var native = new _alpm_question_t
      {
        type_ = _alpm_question_type_t.ALPM_QUESTION_IMPORT_KEY
      };
      native.import_key.import = 0;
      native.import_key.uid = uid;
      native.import_key.fingerprint = fpr;

      var question = new ImportKeyQuestion(&native);
      Assert.False(question.Import);

      question.Import = true;
      Assert.True(question.Import);
      Assert.Equal(1, native.import_key.import);
    }
  }

  // =========================================================================
  // 8. Explicit Snapshot Survival
  // =========================================================================

  [Fact]
  public void ExplicitSnapshot_OutlivesPackageLifetimeAndNativeQuestion()
  {
    PackageSnapshot snapshot;
    var frameLifetime = _environment.AlpmSession.RootLifetime.CreateChild("test frame");

    using (var pkg = Load("orson-payload-detached", file: "usr/bin/probe"))
    {
      var native = new _alpm_question_t
      {
        type_ = _alpm_question_type_t.ALPM_QUESTION_INSTALL_IGNOREPKG
      };
      native.install_ignorepkg.install = 0;
      native.install_ignorepkg.pkg = pkg.BackingStruct;

      var question = new InstallIgnorePackageQuestion(&native, frameLifetime);
      snapshot = question.Package.ToSnapshot(includeFiles: true);
      Assert.Equal("orson-payload-detached", snapshot.Name);
    }

    // Invalidate frame
    frameLifetime.Invalidate("test frame completed");

    // The explicit snapshot stays valid independently of native memory and frame lifetime
    Assert.Equal("orson-payload-detached", snapshot.Name);
    Assert.Equal(PackageArchive.Version, snapshot.Version.ToString());
    Assert.NotNull(snapshot.Files);
    Assert.Contains(snapshot.Files, file => file.Name == "usr/bin/probe");
  }

  private LoadedPackage Load(string name, string file)
    => _environment.AlpmSession.LoadPackage(
      PackageArchive.Create(PackageDirectory, new PackageSpec(name) { File = file }),
      full: true,
      SigLevel.UseDefault);
}
