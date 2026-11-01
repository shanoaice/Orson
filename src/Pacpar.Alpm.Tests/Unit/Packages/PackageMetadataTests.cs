using System.Text;
using Pacpar.Alpm.Tests.Fixtures;

namespace Pacpar.Alpm.Tests.Unit.Packages;

/// <summary>
/// Covers the package members that read what the package records about itself: the install reason,
/// its file list, its download size, and its changelog.
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
    Assert.Equal(filePath, found!.Value.Name);

    Assert.Null(pkg.Files.FindFile("usr/bin/nonexistent-binary"));
  }

  /// <summary>
  /// Measured against libalpm 16.0.1: a package the local database owns reports a download size of
  /// 0 - it is already on disk - rather than the size <c>.PKGINFO</c> records.
  /// </summary>
  [Fact]
  public void DownloadSize_IsZeroForAPackageTheLocalDatabaseOwns()
  {
    var pkg = _environment.Install("pacpar-download-size");

    Assert.Equal(0, pkg.DownloadSize);
  }

  /// <summary>
  /// <c>.CHANGELOG</c> inside the package file is the one place libalpm reads a changelog from, so a
  /// package loaded from a file has one and an installed package does not: writing a changelog into
  /// the database entry afterwards changes nothing (measured against libalpm 16.0.1 - neither
  /// <c>dbpath/local/pkg/changelog</c> nor the same path without <c>local/</c> is read).
  /// </summary>
  [Fact]
  public void ReadChangelog_ReadsThePackagedOne_AndNoneFromAnInstalledPackage()
  {
    const string body = "the changelog body";

    var archive = PackageArchive.Create(_environment.PackageDirectory, "pacpar-changelog", changelog: body);
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

    var archive = PackageArchive.Create(_environment.PackageDirectory, "pacpar-changelog-copy", changelog: body);
    using var loaded = _environment.AlpmSession.LoadPackage(archive, full: true, SigLevel.UseDefault);
    using var destination = new MemoryStream();

    Assert.True(loaded.TryCopyChangelogTo(destination));

    Assert.Equal(Encoding.UTF8.GetBytes(body), destination.ToArray());
  }

  [Fact]
  public void CopyChangelogTo_CopiesTheRawUtf8Bytes()
  {
    const string body = "another changelog body";

    var archive = PackageArchive.Create(_environment.PackageDirectory, "pacpar-changelog-copy-direct", changelog: body);
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
    var archive = PackageArchive.Create(_environment.PackageDirectory, "pacpar-changelog-bounds", changelog: "body");
    using var loaded = _environment.AlpmSession.LoadPackage(archive, full: true, SigLevel.UseDefault);
    using var stream = loaded.OpenChangelogStream();
    Assert.NotNull(stream);

    // The second case used to overflow offset + count into a negative value and fall through to
    // Span's own range check; both must fail the standard bounds check with the same exception type.
    Assert.IsType<ArgumentException>(Record.Exception(() => stream.Read(new byte[4], 3, 5)));
    Assert.IsType<ArgumentException>(Record.Exception(() => stream.Read(new byte[4], int.MaxValue, 1)));
  }
}
