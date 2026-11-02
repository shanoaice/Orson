using System.Formats.Tar;
using System.IO.Compression;
using System.Text;

namespace Pacpar.Alpm.Tests.Fixtures;

/// <summary>
/// Builds the smallest package <c>alpm_pkg_load</c> accepts, so tests that need a real package file
/// do not need the integration container. <see cref="IsolatedAlpmEnvironment.Install(PackageSpec, TransactionFlags)"/> is the way to
/// get one of these into a database.
/// </summary>
/// <remarks>
/// A <c>.PKGINFO</c> plus, optionally, one file the package owns is enough. The archive stays
/// uncompressed - libalpm reads a plain tar, so no <c>zstd</c> is involved - and no <c>.MTREE</c> is
/// required, not even for a <c>full</c> load or for reaching the commit-time file-conflict check. A
/// package built exactly this way was measured against libalpm to pass both cases.
/// </remarks>
internal static class PackageArchive
{
  /// <summary>The version every generated package uses, so assertions can spell the names out.</summary>
  internal const string Version = "1.0-1";

  /// <summary>Writes a default package and returns the path it was written to.</summary>
  /// <param name="directory">Where to write; it must already exist.</param>
  /// <param name="name">The package name (<c>pkgname</c>).</param>
  internal static string Create(string directory, string name)
    => Create(directory, new PackageSpec(name));

  /// <summary>Writes a package matching <paramref name="spec"/> and returns the path it was written to.</summary>
  /// <param name="directory">Where to write; it must already exist.</param>
  /// <param name="spec">The package specification.</param>
  internal static string Create(string directory, PackageSpec spec)
  {
    var info = new StringBuilder()
      .AppendLine($"pkgname = {spec.Name}")
      .AppendLine($"pkgbase = {spec.Base ?? spec.Name}")
      .AppendLine($"pkgver = {spec.Version}")
      .AppendLine($"pkgdesc = {spec.Description ?? "test package"}")
      .AppendLine($"url = {spec.Url ?? "https://example.invalid"}")
      .AppendLine($"builddate = {spec.BuildDate ?? 0}")
      .AppendLine($"packager = {spec.Packager ?? "test"}")
      .AppendLine($"size = {spec.Size ?? 1}")
      .AppendLine($"arch = {spec.Arch}");

    foreach (var license in spec.Licenses) info.AppendLine($"license = {license}");
    foreach (var group in spec.Groups) info.AppendLine($"group = {group}");
    foreach (var depend in spec.Depends) info.AppendLine($"depend = {depend}");
    foreach (var optdepend in spec.OptDepends) info.AppendLine($"optdepend = {optdepend}");
    foreach (var checkdepend in spec.CheckDepends) info.AppendLine($"checkdepend = {checkdepend}");
    foreach (var makedepend in spec.MakeDepends) info.AppendLine($"makedepend = {makedepend}");
    foreach (var conflict in spec.Conflicts) info.AppendLine($"conflict = {conflict}");
    foreach (var provide in spec.Provides) info.AppendLine($"provides = {provide}");
    foreach (var replace in spec.Replaces) info.AppendLine($"replaces = {replace}");
    foreach (var backup in spec.BackupFiles) info.AppendLine($"backup = {backup}");

    var path = Path.Combine(directory, $"{spec.Name}-{spec.Version}-{spec.Arch}.pkg.tar");

    using var stream = File.Create(path);
    using var writer = new TarWriter(stream, TarEntryFormat.Pax);

    writer.WriteEntry(TextEntry(".PKGINFO", info.ToString()));

    if (spec.Changelog != null) writer.WriteEntry(TextEntry(".CHANGELOG", spec.Changelog));
    if (spec.InstallScriptlet != null) writer.WriteEntry(TextEntry(".INSTALL", spec.InstallScriptlet));
    if (spec.MTree != null)
    {
      using var gzStream = new MemoryStream();
      using (var gz = new GZipStream(gzStream, CompressionMode.Compress, leaveOpen: true))
      using (var gzWriter = new StreamWriter(gz, new UTF8Encoding(false)))
      {
        gzWriter.Write(spec.MTree);
      }
      var gzBytes = gzStream.ToArray();
      var mtreeEntry = new PaxTarEntry(TarEntryType.RegularFile, ".MTREE")
      {
        DataStream = new MemoryStream(gzBytes)
      };
      writer.WriteEntry(mtreeEntry);
    }
    if (spec.File != null)
    {
      writer.WriteEntry(new PaxTarEntry(TarEntryType.Directory, "usr/"));
      writer.WriteEntry(new PaxTarEntry(TarEntryType.Directory, "usr/bin/"));
      writer.WriteEntry(TextEntry(spec.File, "packaged contents"));
    }

    foreach (var f in spec.Files)
    {
      writer.WriteEntry(TextEntry(f, "packaged contents"));
    }

    return path;
  }

  internal static void CreateSyncDatabase(string dbpath, string dbName, string pkgName, string groupName, string version = Version)
  {
    var syncDir = Path.Combine(dbpath, "sync");
    Directory.CreateDirectory(syncDir);
    var dbFile = Path.Combine(syncDir, $"{dbName}.db");

    var desc = new StringBuilder()
      .AppendLine("%NAME%")
      .AppendLine(pkgName)
      .AppendLine()
      .AppendLine("%VERSION%")
      .AppendLine(version)
      .AppendLine()
      .AppendLine("%FILENAME%")
      .AppendLine($"{pkgName}-{version}-x86_64.pkg.tar")
      .AppendLine()
      .AppendLine("%GROUPS%")
      .AppendLine(groupName)
      .AppendLine();

    using var stream = File.Create(dbFile);
    using var writer = new TarWriter(stream, TarEntryFormat.Pax);
    writer.WriteEntry(TextEntry($"{pkgName}-{version}/desc", desc.ToString()));
  }

  private static PaxTarEntry TextEntry(string name, string contents)
    => new(TarEntryType.RegularFile, name)
    {
      DataStream = new MemoryStream(Encoding.UTF8.GetBytes(contents))
    };
}
