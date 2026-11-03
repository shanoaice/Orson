using System.Text;
using Pacpar.Alpm.Tests.Fixtures;

namespace Pacpar.Alpm.Tests.Unit.Packages;

/// <summary>
/// Covers the package members that read what the package records about itself: the install reason, its file list,
/// its download size, and its changelog.
/// </summary>
public sealed class PackageMetadataTests : IDisposable
{
  private readonly IsolatedAlpmEnvironment _environment = new();

  public void Dispose() => _environment.Dispose();

  [Fact]
  public void InstallReason_CanBeReadAndModified()
  {
    var pkg = _environment.Install("pacpar-reason-pkg");

    Assert.Equal(PackageReason.Explicit, pkg.InstallReason);

    pkg.InstallReason = PackageReason.Dependency;
    Assert.Equal(PackageReason.Dependency, pkg.InstallReason);

    pkg.SetReason(PackageReason.Explicit);
    Assert.Equal(PackageReason.Explicit, pkg.InstallReason);
  }

  [Fact]
  public void FileList_ContainsAndFindFile_FindsPackagedFiles()
  {
    const string filePath = "usr/bin/tool-binary";
    var pkg = _environment.Install("pacpar-filelist-pkg", file: filePath);

    Assert.True(pkg.Files.Contains(filePath));
    Assert.False(pkg.Files.Contains("usr/bin/nonexistent-binary"));

    var found = pkg.Files.FindFile(filePath);
    Assert.NotNull(found);
    Assert.Equal(filePath, found.Value.Name);

    Assert.Null(pkg.Files.FindFile("usr/bin/nonexistent-binary"));
  }

  /// <summary>
  /// Measured against libalpm 16.0.1: a package the local database owns reports a download size of 0 - it is
  /// already on disk - rather than the size <c>.PKGINFO</c> records.
  /// </summary>
  [Fact]
  public void DownloadSize_IsZeroForAPackageTheLocalDatabaseOwns()
  {
    var pkg = _environment.Install("pacpar-download-size");

    Assert.Equal(0, pkg.DownloadSize);
  }

  /// <summary>
  /// <c>.CHANGELOG</c> inside the package file is the one place libalpm reads a changelog from, so a package loaded
  /// from a file has one and an installed package does not: writing a changelog into the database entry afterwards changes
  /// nothing (measured against libalpm 16.0.1 - neither <c>dbpath/local/pkg/changelog</c> nor the same path without
  /// <c>local/</c> is read).
  /// </summary>
  [Fact]
  public void ReadChangelog_ReadsThePackagedOne_AndNoneFromAnInstalledPackage()
  {
    const string body = "the changelog body";

    var archive = PackageArchive.Create(_environment.PackageDirectory, new PackageSpec("pacpar-changelog") { Changelog = body });
    using var loaded = _environment.AlpmSession.LoadPackage(archive, full: true, SigLevel.UseDefault);

    var changelog = loaded.ReadChangelog();
    Assert.NotNull(changelog);
    Assert.Contains(body, changelog);

    var installed = _environment.Install("pacpar-changelog-installed");
    Assert.Null(installed.ReadChangelog());
  }

  [Fact]
  public void TryCopyChangelogTo_CopiesTheRawUtf8Bytes()
  {
    const string body = "the changelog body \u00e9";

    var archive = PackageArchive.Create(_environment.PackageDirectory, new PackageSpec("pacpar-changelog-copy") { Changelog = body });
    using var loaded = _environment.AlpmSession.LoadPackage(archive, full: true, SigLevel.UseDefault);
    using var destination = new MemoryStream();

    Assert.True(loaded.TryCopyChangelogTo(destination));

    Assert.Equal(Encoding.UTF8.GetBytes(body), destination.ToArray());
  }

  [Fact]
  public void CopyChangelogTo_CopiesTheRawUtf8Bytes()
  {
    const string body = "another changelog body";

    var archive = PackageArchive.Create(_environment.PackageDirectory, new PackageSpec("pacpar-changelog-copy-direct") { Changelog = body });
    using var loaded = _environment.AlpmSession.LoadPackage(archive, full: true, SigLevel.UseDefault);
    using var destination = new MemoryStream();

    loaded.CopyChangelogTo(destination);

    Assert.Equal(Encoding.UTF8.GetBytes(body), destination.ToArray());
  }

  [Fact]
  public void TryCopyChangelogTo_ReturnsFalseWhenNoChangelog()
  {
    var installed = _environment.Install("pacpar-changelog-copy-none");
    using var destination = new MemoryStream();

    Assert.False(installed.TryCopyChangelogTo(destination));
    Assert.Empty(destination.ToArray());
  }

  [Fact]
  public void CopyChangelogTo_ThrowsWhenNoChangelog()
  {
    var installed = _environment.Install("pacpar-changelog-copy-throws");
    using var destination = new MemoryStream();

    Assert.Throws<InvalidOperationException>(() => installed.CopyChangelogTo(destination));
  }

  [Fact]
  public void ChangelogStream_ReadRejectsOutOfRangeBoundsWithoutOverflow()
  {
    var archive = PackageArchive.Create(_environment.PackageDirectory, new PackageSpec("pacpar-changelog-bounds") { Changelog = "body" });
    using var loaded = _environment.AlpmSession.LoadPackage(archive, full: true, SigLevel.UseDefault);
    using var stream = loaded.OpenChangelogStream();
    Assert.NotNull(stream);

    // The second case used to overflow offset + count into a negative value and fall through to
    // Span's own range check; both must fail the standard bounds check with the same exception type.
    Assert.IsType<ArgumentException>(Record.Exception(() => stream.Read(new byte[4], 3, 5)));
    Assert.IsType<ArgumentException>(Record.Exception(() => stream.Read(new byte[4], int.MaxValue, 1)));
  }

  [Fact]
  public void LoadedAndInstalledPackage_ExposesAllPkgInfoMetadataAndLists()
  {
    var archive = PackageArchive.Create(
      _environment.PackageDirectory,
      new PackageSpec("meta-pkg")
      {
        Version = "2.1-3",
        Base = "meta-base",
        Description = "Full description",
        Url = "https://example.org",
        Packager = "Test Packager <test@example.org>",
        BuildDate = 1700000000L,
        Size = 4096L,
        License = "MIT",
        Group = "base-devel",
        Depend = "glibc>=2.38",
        OptDepend = "python: scripting",
        CheckDepend = "pytest",
        MakeDepend = "cmake",
        Conflict = "legacy-meta",
        Provide = "virtual-meta=2.1",
        Replace = "old-meta",
        Backup = "etc/meta.conf",
        File = "etc/meta.conf",
        InstallScriptlet = "post_install() { true; }\n",
      });

    using (var loaded = _environment.AlpmSession.LoadPackage(archive, full: true, SigLevel.UseDefault))
    {
      Assert.Equal("meta-pkg", loaded.Name);
      Assert.Equal("meta-base", loaded.Base);
      Assert.Equal("2.1-3", loaded.Version.ToString());
      Assert.Equal("Full description", loaded.Description);
      Assert.Equal("https://example.org", loaded.Url);
      Assert.Equal("Test Packager <test@example.org>", loaded.Packager);
      Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1700000000L), loaded.BuildDate);
      Assert.Equal(4096L, loaded.InstalledSize);
      Assert.True(loaded.Size > 0);
      Assert.Equal("x86_64", loaded.Arch);
      Assert.Equal(PackageOrigin.File, loaded.Origin);
      _ = loaded.Validation;
      Assert.True(loaded.HasScriptlet);

      Assert.Equal("MIT", Assert.Single(loaded.Licenses));
      Assert.Equal("base-devel", Assert.Single(loaded.Groups));

      var dep = Assert.Single(loaded.Depends);
      Assert.Equal("glibc", dep.Name);
      Assert.Equal("2.38", dep.Version);
      Assert.Equal(DepMod.GreaterOrEqual, dep.Depmod);

      var optdep = Assert.Single(loaded.OptionalDepends);
      Assert.Equal("python", optdep.Name);
      Assert.Equal("scripting", optdep.Description);

      var checkdep = Assert.Single(loaded.CheckDepends);
      Assert.Equal("pytest", checkdep.Name);

      var makedep = Assert.Single(loaded.MakeDepends);
      Assert.Equal("cmake", makedep.Name);

      var conflict = Assert.Single(loaded.Conflicts);
      Assert.Equal("legacy-meta", conflict.Name);

      var provides = Assert.Single(loaded.Provides);
      Assert.Equal("virtual-meta", provides.Name);
      Assert.Equal("2.1", provides.Version);

      var replaces = Assert.Single(loaded.Replaces);
      Assert.Equal("old-meta", replaces.Name);

      var backup = Assert.Single(loaded.BackupFiles);
      Assert.Equal("etc/meta.conf", backup.Name);

      var snapshot = loaded.ToSnapshot(includeFiles: true);
      Assert.Equal(loaded.Name, snapshot.Name);
      Assert.NotNull(snapshot.Files);
      Assert.Equal(loaded.Files.Count, snapshot.Files.Count);
    }

    var loadForInstall = _environment.AlpmSession.LoadPackage(archive, full: true, SigLevel.UseDefault);
    using (var tx = _environment.AlpmSession.BeginTransaction(TransactionFlags.NoScriptlet | TransactionFlags.NoDeps))
    {
      tx.AddPackage(loadForInstall);
      tx.Prepare();
      tx.Commit();
    }

    var depArchive = PackageArchive.Create(
      _environment.PackageDirectory,
      new PackageSpec("dep-pkg")
      {
        Depend = "meta-pkg",
        OptDepend = "meta-pkg: optional integration",
      });

    var depLoad = _environment.AlpmSession.LoadPackage(depArchive, full: true, SigLevel.UseDefault);
    using (var tx = _environment.AlpmSession.BeginTransaction(TransactionFlags.NoDeps))
    {
      tx.AddPackage(depLoad);
      tx.Prepare();
      tx.Commit();
    }

    var installed = _environment.AlpmSession.GetLocalDatabase().GetPackage("meta-pkg");
    Assert.NotNull(installed);
    Assert.Equal(PackageOrigin.LocalDatabase, installed.Origin);
    Assert.NotNull(installed.InstallDate);
    Assert.True(installed.InstallDate.Value.ToUnixTimeSeconds() > 0);
    Assert.Contains("dep-pkg", installed.GetRequiredBy());
    Assert.Contains("dep-pkg", installed.GetOptionalFor());

    _environment.AlpmSession.Options.IgnorePackages.Add("meta-pkg");
    Assert.True(installed.ShouldIgnore());
  }

  [Fact]
  public void FileList_IndexerAndEnumerator_EnforceBoundsAndReadEntries()
  {
    var archive = PackageArchive.Create(
      _environment.PackageDirectory,
      new PackageSpec("files-pkg")
      {
        Files = ["etc/a.conf", "usr/bin/tool"],
      });

    using var loaded = _environment.AlpmSession.LoadPackage(archive, full: true, SigLevel.UseDefault);
    using (var tx = _environment.AlpmSession.BeginTransaction(TransactionFlags.NoDeps))
    {
      tx.AddPackage(loaded);
      tx.Prepare();
      tx.Commit();
    }

    var pkg = _environment.AlpmSession.GetLocalDatabase().GetPackage("files-pkg");
    Assert.NotNull(pkg);

    Assert.Equal(2, pkg.Files.Count);
    Assert.Equal("etc/a.conf", pkg.Files[0].Name);
    Assert.Equal("usr/bin/tool", pkg.Files[1].Name);

    Assert.Throws<ArgumentOutOfRangeException>(() => _ = pkg.Files[-1]);
    Assert.Throws<ArgumentOutOfRangeException>(() => _ = pkg.Files[2]);
    Assert.Throws<ArgumentNullException>(() => pkg.Files.Contains(null!));
    Assert.Throws<ArgumentNullException>(() => pkg.Files.FindFile(null!));

    using var enumerator = pkg.Files.GetEnumerator();
    Assert.Throws<InvalidOperationException>(() => _ = enumerator.Current);

    Assert.True(enumerator.MoveNext());
    Assert.Equal("etc/a.conf", enumerator.Current.Name);
    Assert.True(enumerator.MoveNext());
    Assert.Equal("usr/bin/tool", enumerator.Current.Name);
    Assert.False(enumerator.MoveNext());
    Assert.Throws<InvalidOperationException>(() => _ = enumerator.Current);

    enumerator.Reset();
    Assert.Throws<InvalidOperationException>(() => _ = enumerator.Current);
    Assert.True(enumerator.MoveNext());
    Assert.Equal("etc/a.conf", enumerator.Current.Name);
  }
}
