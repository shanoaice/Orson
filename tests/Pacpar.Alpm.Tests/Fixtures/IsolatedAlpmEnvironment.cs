namespace Pacpar.Alpm.Tests.Fixtures;

/// <summary>
/// An isolated libalpm handle for tests that must run without the project container: a unique
/// root/dbpath tree under <see cref="Path.GetTempPath()"/>, with no assertion that the container is
/// running. It also provides <see cref="Install(PackageSpec, TransactionFlags)"/>, the synthetic package every feature test starts
/// from.
/// </summary>
internal sealed class IsolatedAlpmEnvironment : IDisposable
{

  internal IsolatedAlpmEnvironment()
  {
    WorkspaceRoot = Path.Combine(Path.GetTempPath(), "pacpar-isolated", Guid.NewGuid().ToString("n"));
    var root = Path.Combine(WorkspaceRoot, "root");
    var dbpath = Path.Combine(WorkspaceRoot, "var", "lib", "pacman");

    PackageDirectory = Path.Combine(WorkspaceRoot, "packages");

    Directory.CreateDirectory(root);
    Directory.CreateDirectory(Path.Combine(dbpath, "local"));
    Directory.CreateDirectory(Path.Combine(root, "tmp"));
    Directory.CreateDirectory(Path.Combine(root, "var", "cache", "pacman", "pkg"));
    Directory.CreateDirectory(PackageDirectory);

    AlpmSession = new AlpmSession(root, dbpath);
  }

  internal AlpmSession AlpmSession { get; }
  internal string WorkspaceRoot { get; }

  /// <summary>The fake installation root the handle was opened on.</summary>
  internal string Root => Path.Combine(WorkspaceRoot, "root");
  /// <summary>The database path for this isolated environment.</summary>
  internal string DatabasePath => Path.Combine(WorkspaceRoot, "var", "lib", "pacman");


  /// <summary>Where <see cref="Install(PackageSpec, TransactionFlags)"/> writes the packages it builds.</summary>
  internal string PackageDirectory { get; }

  /// <summary>
  /// Builds a synthetic package in <see cref="PackageDirectory"/>, commits it, and answers with the
  /// view the local database holds for it afterwards.
  /// </summary>
  /// <param name="name">The package name.</param>
  /// <param name="file">Optional file the package owns, for example <c>usr/bin/probe-file</c>.</param>
  /// <param name="mtree">Optional <c>.MTREE</c> contents.</param>
  /// <param name="depend">Optional <c>depend</c> entry.</param>
  /// <param name="conflict">Optional <c>conflict</c> entry.</param>
  /// <param name="flags">Transaction flags to commit with.</param>
  internal PackageView Install(string name, string? file = null, string? mtree = null, string? depend = null,
    string? conflict = null, TransactionFlags flags = 0)
    => Install(new PackageSpec(name)
    {
      File = file,
      MTree = mtree,
      Depend = depend,
      Conflict = conflict
    }, flags);

  /// <summary>
  /// Builds a synthetic package from <paramref name="spec"/> in <see cref="PackageDirectory"/>, commits it,
  /// and answers with the view the local database holds for it afterwards.
  /// </summary>
  internal PackageView Install(PackageSpec spec, TransactionFlags flags = 0)
  {
    var archive = PackageArchive.Create(PackageDirectory, spec);

    using var transaction = AlpmSession.BeginTransaction(flags);
    transaction.AddPackage(AlpmSession.LoadPackage(archive, full: true, SigLevel.UseDefault));
    transaction.Prepare();
    transaction.Commit();

    var installed = AlpmSession.GetLocalDatabase().GetPackage(spec.Name);
    Assert.NotNull(installed);
    return installed;
  }

  public void Dispose()
  {
    AlpmSession.Dispose();

    if (Directory.Exists(WorkspaceRoot))
    {
      Directory.Delete(WorkspaceRoot, recursive: true);
    }
  }
}
