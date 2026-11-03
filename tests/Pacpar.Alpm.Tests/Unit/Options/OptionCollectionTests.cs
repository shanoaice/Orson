using Pacpar.Alpm.Tests.Fixtures;

namespace Pacpar.Alpm.Tests.Unit.Options;

/// <summary>
/// Regression tests for the option collection template: <c>Clear()</c> must not keep walking the native list
/// after removing from it, and <c>CopyTo</c> must follow <see cref="ICollection{T}" /> semantics instead of shifting the
/// destination.
/// </summary>
/// <remarks>
/// These tests run against <see cref="IsolatedAlpmEnvironment" />, which builds the handle under
/// <see cref="Path.GetTempPath()" /> deliberately instead of using the container fixture, so they stay in the
/// non-integration test set.
/// </remarks>
public sealed class OptionCollectionTests : IDisposable
{
  private readonly IsolatedAlpmEnvironment _environment = new();

  private ICollection<string> Architectures => _environment.AlpmSession.Options.Architectures;

  public void Dispose() => _environment.Dispose();

  [Fact]
  public void CopyTo_WithNonZeroArrayIndex_WritesFromThatIndex()
  {
    var architectures = Architectures;
    architectures.Add("x86_64");
    architectures.Add("aarch64");
    architectures.Add("any");

    var destination = new string[5];

    architectures.CopyTo(destination, 1);

    Assert.Null(destination[0]);
    Assert.Equal("x86_64", destination[1]);
    Assert.Equal("aarch64", destination[2]);
    Assert.Equal("any", destination[3]);
    Assert.Null(destination[4]);
  }

  [Fact]
  public void CopyTo_ExactlyFillingTheDestination_Succeeds()
  {
    var architectures = Architectures;
    architectures.Add("x86_64");
    architectures.Add("aarch64");
    architectures.Add("any");

    var destination = new string[3];

    architectures.CopyTo(destination, 0);

    Assert.Equal(["x86_64", "aarch64", "any"], destination);

  }

  [Fact]
  public void CopyTo_NullArray_ThrowsArgumentNullException()
  {
    var architectures = Architectures;
    architectures.Add("x86_64");

    Assert.Throws<ArgumentNullException>(() => architectures.CopyTo(null!, 0));
  }

  [Fact]
  public void CopyTo_NegativeArrayIndex_ThrowsArgumentOutOfRangeException()
  {
    var architectures = Architectures;
    architectures.Add("x86_64");

    Assert.Throws<ArgumentOutOfRangeException>(() => architectures.CopyTo(new string[1], -1));
  }

  /// <summary>
  /// The old implementation never checked <c>array.Length</c>: it wrote <c>Count</c> items starting at
  /// <c>array[0]</c> and ran off the end (or overwrote the wrong slots) whenever the destination was too small.
  /// </summary>
  [Theory]
  [InlineData(3, 1)] // needs 3 free slots, only 2 remain
  [InlineData(2, 0)] // needs 3 free slots, only 2 exist
  [InlineData(5, 3)] // needs 3 free slots, only 2 remain
  public void CopyTo_DestinationTooSmall_ThrowsArgumentException(int arrayLength, int arrayIndex)
  {
    var architectures = Architectures;
    architectures.Add("x86_64");
    architectures.Add("aarch64");
    architectures.Add("any");

    var exception = Assert.Throws<ArgumentException>(() => architectures.CopyTo(new string[arrayLength], arrayIndex));

    Assert.Equal("array", exception.ParamName);
  }

  /// <summary>
  /// The old implementation pre-advanced the enumerator and then read <c>Current</c> in a <c>do/while</c>, so
  /// copying an empty collection threw <see cref="InvalidOperationException" />.
  /// </summary>
  [Fact]
  public void CopyTo_EmptyCollection_IsANoOp()
  {
    var architectures = Architectures;
    Assert.Empty(architectures);

    var destination = new[] { "keep" };

    architectures.CopyTo(destination, 1);

    Assert.Equal(["keep"], destination);
  }
  [Fact]
  public void Clear_RemovesEveryItem_AndSucceedsOnEmptyCollection()
  {
    var architectures = Architectures;

    // Clear on empty collection must not throw
    architectures.Clear();
    Assert.Empty(architectures);

    architectures.Add("x86_64");
    architectures.Add("aarch64");
    architectures.Add("any");
    Assert.Equal(3, architectures.Count);

    architectures.Clear();
    Assert.Empty(architectures);

    // The handle must still be usable after Clear().
    architectures.Add("riscv64");
    Assert.Equal("riscv64", Assert.Single(architectures));
  }

  /// <summary>
  /// Every <c>alpm_option_remove_*</c> returns 1 when it removed the entry and 0 when it found nothing, while its
  /// header documents "0 on success, -1 on error". The wrapper used to test that result against 0, which inverted the result
  /// of <see cref="ICollection{T}.Remove" /> for all nine option collections.
  /// </summary>
  [Fact]
  public void Remove_ReportsWhetherTheItemWasThere()
  {
    var architectures = Architectures;
    architectures.Add("x86_64");

    Assert.True(architectures.Remove("x86_64"));
    Assert.Empty(architectures);

    Assert.False(architectures.Remove("x86_64"));

    // An item that was never there must answer false without disturbing the list.
    architectures.Add("aarch64");
    Assert.False(architectures.Remove("riscv64"));
    Assert.Equal("aarch64", Assert.Single(architectures));
  }

  /// <summary>
  /// <c>Contains</c> used to compare libalpm's finder result with the needle it had just allocated.
  /// <c>alpm_list_find_str</c> answers with the element stored in the list - probed: a list holding its own
  /// <c>strdup("abc")</c> returned that pointer, not the equal buffer the caller passed - so the comparison held for no
  /// string list at all and <c>Contains</c> answered <c>false</c> for every item that *was* present. Nothing covered it:
  /// <c>Remove</c> never used the comparison, and <c>Clear</c> ignores what <c>Remove</c> returns.
  /// </summary>
  [Fact]
  public void Contains_ReportsItemsThatArePresent()
  {
    var architectures = Architectures;
    architectures.Add("x86_64");
    architectures.Add("aarch64");

    Assert.True(architectures.Contains("x86_64"));
    Assert.True(architectures.Contains("aarch64"));
    Assert.False(architectures.Contains("riscv64"));

    // Removing one must not make the other unreachable.
    Assert.True(architectures.Remove("x86_64"));
    Assert.False(architectures.Contains("x86_64"));
    Assert.True(architectures.Contains("aarch64"));
  }

  [Fact]
  public void ScalarOptions_ThrowAlpmLifetimeException_AfterSessionDisposed()
  {
    var options = _environment.AlpmSession.Options;
    _environment.AlpmSession.Dispose();

    Assert.Throws<AlpmLifetimeException>(() => options.CheckSpace);
    Assert.Throws<AlpmLifetimeException>(() => options.CheckSpace = true);
    Assert.Throws<AlpmLifetimeException>(() => options.DatabaseExtension);
    Assert.Throws<AlpmLifetimeException>(() => options.DatabasePath);
    Assert.Throws<AlpmLifetimeException>(() => options.Root);
    Assert.Throws<AlpmLifetimeException>(() => options.DefaultSigLevel);
    Assert.Throws<AlpmLifetimeException>(() => options.DefaultSigLevel = SigLevel.Package);
    Assert.Throws<AlpmLifetimeException>(() => options.LocalFileSigLevel);
    Assert.Throws<AlpmLifetimeException>(() => options.RemoteFileSigLevel);
    Assert.Throws<AlpmLifetimeException>(() => options.ParallelDownloads);
    Assert.Throws<AlpmLifetimeException>(() => options.ParallelDownloads = 5);
    Assert.Throws<AlpmLifetimeException>(() => options.LogFile);
    Assert.Throws<AlpmLifetimeException>(() => options.UseSyslog);
    Assert.Throws<AlpmLifetimeException>(() => options.Lockfile);
    Assert.Throws<AlpmLifetimeException>(() => options.GpgDirectory);
    Assert.Throws<AlpmLifetimeException>(() => options.DisableDownloadTimeout);
    Assert.Throws<AlpmLifetimeException>(() => options.DisableSandboxFilesystem);
    Assert.Throws<AlpmLifetimeException>(() => options.SandboxUser);
  }

  [Fact]
  public void OptionCollections_ReturnSameInstance_OnConsecutiveCalls()
  {
    var options = _environment.AlpmSession.Options;

    Assert.Same(options.Architectures, options.Architectures);
    Assert.Same(options.AssumeInstalled, options.AssumeInstalled);
    Assert.Same(options.CacheDirectories, options.CacheDirectories);
    Assert.Same(options.OverwritableFiles, options.OverwritableFiles);
    Assert.Same(options.HookDirectories, options.HookDirectories);
    Assert.Same(options.IgnoreGroups, options.IgnoreGroups);
    Assert.Same(options.IgnorePackages, options.IgnorePackages);
    Assert.Same(options.NoExtract, options.NoExtract);
    Assert.Same(options.NoUpgrade, options.NoUpgrade);
  }

  [Fact]
  public void OptionCollections_ThrowAlpmLifetimeException_AfterSessionDisposed()
  {
    var options = _environment.AlpmSession.Options;
    var architectures = options.Architectures;
    var assumeInstalled = options.AssumeInstalled;
    var cacheDirectories = options.CacheDirectories;
    var overwritableFiles = options.OverwritableFiles;
    var hookDirectories = options.HookDirectories;
    var ignoreGroups = options.IgnoreGroups;
    var ignorePackages = options.IgnorePackages;
    var noExtract = options.NoExtract;
    var noUpgrade = options.NoUpgrade;

    _environment.AlpmSession.Dispose();

    Assert.Throws<AlpmLifetimeException>(() => architectures.Count);
    Assert.Throws<AlpmLifetimeException>(() => architectures.Add("x86_64"));
    Assert.Throws<AlpmLifetimeException>(() => architectures.Contains("x86_64"));
    Assert.Throws<AlpmLifetimeException>(() => architectures.Remove("x86_64"));
    Assert.Throws<AlpmLifetimeException>(() => architectures.Clear());
    Assert.Throws<AlpmLifetimeException>(() => architectures.GetEnumerator());

    Assert.Throws<AlpmLifetimeException>(() => assumeInstalled.Count);
    Assert.Throws<AlpmLifetimeException>(() => cacheDirectories.Count);
    Assert.Throws<AlpmLifetimeException>(() => overwritableFiles.Count);
    Assert.Throws<AlpmLifetimeException>(() => hookDirectories.Count);
    Assert.Throws<AlpmLifetimeException>(() => ignoreGroups.Count);
    Assert.Throws<AlpmLifetimeException>(() => ignorePackages.Count);
    Assert.Throws<AlpmLifetimeException>(() => noExtract.Count);
    Assert.Throws<AlpmLifetimeException>(() => noUpgrade.Count);
  }

  [Fact]
  public void OptionsAndCollections_RemainValid_AfterTransactionCommit()
  {
    var pkgDir = Path.Combine(_environment.WorkspaceRoot, "packages");
    Directory.CreateDirectory(pkgDir);
    var archive = PackageArchive.Create(pkgDir, "pacpar-commit-opts");
    var pkg = _environment.AlpmSession.LoadPackage(archive, full: true, SigLevel.UseDefault);

    var options = _environment.AlpmSession.Options;
    options.IgnorePackages.Add("pkg-before");

    using (var tx = _environment.AlpmSession.BeginTransaction(TransactionFlags.NoDeps | TransactionFlags.NoScriptlet))
    {
      tx.AddPackage(pkg);
      tx.Prepare();
      tx.Commit();
    }

    // Scalar options remain readable and writable
    options.CheckSpace = true;
    Assert.True(options.CheckSpace);
    Assert.NotEmpty(options.Root);

    // Cached collection option instances remain usable
    options.IgnorePackages.Add("pkg-after");
    Assert.True(options.IgnorePackages.Contains("pkg-before"));
    Assert.True(options.IgnorePackages.Contains("pkg-after"));
    Assert.Contains("pkg-after", options.IgnorePackages);
  }

  public static TheoryData<string, Func<AlpmOptions, ICollection<string>>, string, string> StringOptionCollections() => new()
  {
    { "Architectures", o => o.Architectures, "x86_64", "aarch64" },
    { "CacheDirectories", o => o.CacheDirectories, "/var/cache/pacman/pkg-a/", "/var/cache/pacman/pkg-b/" },
    { "HookDirectories", o => o.HookDirectories, "/etc/pacman.d/hooks-a/", "/etc/pacman.d/hooks-b/" },
    { "IgnoreGroups", o => o.IgnoreGroups, "gnome", "kde" },
    { "IgnorePackages", o => o.IgnorePackages, "linux", "glibc" },
    { "NoExtract", o => o.NoExtract, "usr/share/doc/*", "usr/share/man/*" },
    { "NoUpgrade", o => o.NoUpgrade, "etc/pacman.conf", "etc/makepkg.conf" },
    { "OverwritableFiles", o => o.OverwritableFiles, "usr/lib/*", "usr/bin/*" },
  };

  [Theory]
  [MemberData(nameof(StringOptionCollections))]
  public void StringOptionCollection_SupportsAddContainsCopyToRemoveAndClear(
    string name,
    Func<AlpmOptions, ICollection<string>> selector,
    string first,
    string second)
  {
    Assert.NotEmpty(name);
    var collection = selector(_environment.AlpmSession.Options);
    collection.Clear();

    collection.Add(first);
    collection.Add(second);

    Assert.Equal(2, collection.Count);
    Assert.Contains(first, collection);
    Assert.Contains(second, collection);
    Assert.DoesNotContain("absent", collection);

    var array = new string[2];
    collection.CopyTo(array, 0);
    Assert.Contains(first, array);
    Assert.Contains(second, array);

    Assert.True(collection.Remove(first));
    Assert.False(collection.Remove("absent"));
    Assert.Single(collection);

    collection.Clear();
    Assert.Empty(collection);
  }

  [Fact]
  public void ScalarOptions_RoundTripValues()
  {
    var options = _environment.AlpmSession.Options;

    options.CheckSpace = true;
    Assert.True(options.CheckSpace);
    options.CheckSpace = false;
    Assert.False(options.CheckSpace);

    options.DatabaseExtension = ".db.tar.gz";
    Assert.Equal(".db.tar.gz", options.DatabaseExtension);

    options.DefaultSigLevel = SigLevel.Package | SigLevel.DatabaseOptional;
    Assert.Equal(SigLevel.Package | SigLevel.DatabaseOptional, options.DefaultSigLevel);

    options.LocalFileSigLevel = SigLevel.PackageOptional;
    Assert.Equal(SigLevel.PackageOptional, options.LocalFileSigLevel);

    options.RemoteFileSigLevel = SigLevel.Package | SigLevel.PackageMarginalOk;
    Assert.Equal(SigLevel.Package | SigLevel.PackageMarginalOk, options.RemoteFileSigLevel);

    options.ParallelDownloads = 5;
    Assert.Equal(5, options.ParallelDownloads);

    options.UseSyslog = true;
    Assert.True(options.UseSyslog);
    options.UseSyslog = false;
    Assert.False(options.UseSyslog);

    options.DisableDownloadTimeout = true;
    Assert.True(options.DisableDownloadTimeout);
    options.DisableDownloadTimeout = false;
    Assert.False(options.DisableDownloadTimeout);

    var gpgDir = Path.Combine(_environment.WorkspaceRoot, "gpg") + "/";
    Directory.CreateDirectory(gpgDir);
    options.GpgDirectory = gpgDir;
    Assert.Equal(gpgDir, options.GpgDirectory);

    var logFile = Path.Combine(_environment.WorkspaceRoot, "alpm.log");
    options.LogFile = logFile;
    Assert.Equal(logFile, options.LogFile);

    Assert.NotEmpty(options.Root);
    Assert.NotEmpty(options.DatabasePath);
    Assert.NotEmpty(options.Lockfile);

    Assert.ThrowsAny<Exception>(() => options.ParallelDownloads = 0);
    Assert.Throws<ArgumentNullException>(() => options.DatabaseExtension = null!);
  }

}
