namespace Orson.Alpm.Tests.Fixtures;

/// <summary>Specifications for synthetic packages built by <see cref="PackageArchive" />.</summary>
/// <param name="Name">The package name (<c>pkgname</c>).</param>
internal sealed record PackageSpec(string Name)
{
  public string Version { get; init; } = PackageArchive.Version;
  public string Arch { get; init; } = "x86_64";
  public string? Base { get; init; }
  public string? Description { get; init; }
  public string? Url { get; init; }
  public string? Packager { get; init; }
  public long? BuildDate { get; init; }
  public long? Size { get; init; }

  public string? MTree { get; init; }
  public string? Changelog { get; init; }
  public string? InstallScriptlet { get; init; }

  public List<string> Licenses { get; init; } = [];
  public string? License
  {
    init { if (value != null) Licenses.Add(value); }
  }

  public List<string> Groups { get; init; } = [];
  public string? Group
  {
    init { if (value != null) Groups.Add(value); }
  }

  public List<string> Depends { get; init; } = [];
  public string? Depend
  {
    init { if (value != null) Depends.Add(value); }
  }

  public List<string> OptDepends { get; init; } = [];
  public string? OptDepend
  {
    init { if (value != null) OptDepends.Add(value); }
  }

  public List<string> CheckDepends { get; init; } = [];
  public string? CheckDepend
  {
    init { if (value != null) CheckDepends.Add(value); }
  }

  public List<string> MakeDepends { get; init; } = [];
  public string? MakeDepend
  {
    init { if (value != null) MakeDepends.Add(value); }
  }

  public List<string> Conflicts { get; init; } = [];
  public string? Conflict
  {
    init { if (value != null) Conflicts.Add(value); }
  }

  public List<string> Provides { get; init; } = [];
  public string? Provide
  {
    init { if (value != null) Provides.Add(value); }
  }

  public List<string> Replaces { get; init; } = [];
  public string? Replace
  {
    init { if (value != null) Replaces.Add(value); }
  }

  public List<string> BackupFiles { get; init; } = [];
  public string? Backup
  {
    init { if (value != null) BackupFiles.Add(value); }
  }

  public List<string> Files { get; init; } = [];
  public string? File { get; init; }
}
