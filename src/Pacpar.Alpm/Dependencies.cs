using System.Runtime.InteropServices;
using Pacpar.Alpm.Bindings;

namespace Pacpar.Alpm;

/// <summary>The version comparison a dependency uses (libalpm's <c>_alpm_depmod_t</c>).</summary>
/// <remarks>The values map directly to libalpm comparison operators, where unconstrained dependencies use <see cref="Any" /> (1). Zero is not a valid operator.</remarks>
public enum DependencyComparison : uint
{
  /// <summary>Any version satisfies the dependency (<c>ALPM_DEP_MOD_ANY</c>).</summary>
  Any = 1,

  /// <summary>= (<c>ALPM_DEP_MOD_EQ</c>).</summary>
  Equal = 2,

  /// <summary>&gt;= (<c>ALPM_DEP_MOD_GE</c>).</summary>
  GreaterOrEqual = 3,

  /// <summary>&lt;= (<c>ALPM_DEP_MOD_LE</c>).</summary>
  LessOrEqual = 4,

  /// <summary>&gt; (<c>ALPM_DEP_MOD_GT</c>).</summary>
  Greater = 5,

  /// <summary>&lt; (<c>ALPM_DEP_MOD_LT</c>).</summary>
  Less = 6
}

/// <summary>A dependency (<c>alpm_depend_t</c>).</summary>
/// <remarks>
/// A managed snapshot: construction copies every field out of the native struct, and no pointer to it is retained. The
/// instance stays valid after the package, database or option list it came from is gone, but - like libalpm's own
/// <c>alpm_depend_t</c> - it is a value, not a handle, so <i>changing</i> the dependency means replacing the entry in the
/// list that owns it.
/// <para>
/// Handing a snapshot back to libalpm is explicit: <see cref="ToNative" /> materialises a struct for the calls that
/// take one, and the assume-installed option list uses it for <c>Add</c>. Lookups (<c>Contains</c>, <c>Remove</c>) compare
/// by value through <see cref="Matches" /> instead, because libalpm's removal predicate also compares the internal
/// <c>name_hash</c>, which only a struct libalpm built itself carries.
/// </para>
/// </remarks>
public sealed unsafe class Dependency
{
  /// <summary>Creates a detached, fully managed copy of a native dependency.</summary>
  internal Dependency(_alpm_depend_t* backingStruct)
  {
    Name = NativeString.FromNative((nint)backingStruct->name);
    Version = NativeString.FromNative((nint)backingStruct->version);
    Description = NativeString.FromNative((nint)backingStruct->desc);
    Depmod = (DependencyComparison)(uint)backingStruct->mod_;
  }

  /// <summary>Creates a new copy of <see cref="Dependency" /> from managed components</summary>
  private Dependency(string? name, string? version, string? description, DependencyComparison depmod)
  {
    Name = name;
    Version = version;
    Description = description;
    Depmod = depmod;
  }

  internal static Dependency Factory(void* ptr, Lifetime? lifetime) => new((_alpm_depend_t*)ptr);

  /// <summary>
  /// Borrowed view over a dependency list owned by libalpm (for example <c>alpm_pkg_get_depends</c>,
  /// <c>alpm_option_get_assumeinstalled</c>).
  /// </summary>
  /// <param name="alpmList">The borrowed list. May be <c>null</c>.</param>
  /// <param name="lifetime">Token of the context that owns the list; guards traversal.</param>
  internal static AlpmList<Dependency> ListFactory(_alpm_list_t* alpmList, Lifetime? lifetime)
    => AlpmList<Dependency>.Borrow(alpmList, &Factory, lifetime);

  /// <summary>An optional description of why this dependency is required or recommended.</summary>
  public string? Description { get; }

  /// <summary>The package name required by this dependency.</summary>
  public string? Name { get; }

  /// <summary>The version constraint for this dependency, or <c>null</c> if any version satisfies it.</summary>
  public string? Version { get; }

  /// <summary>The comparison operator applied to <see cref="Version" />.</summary>
  public DependencyComparison Depmod { get; }

  /// <summary>Determines whether this dependency matches <paramref name="other" /> by name, version, and version modifier.</summary>
  internal bool Matches(Dependency other)
    => string.Equals(Name, other.Name, StringComparison.Ordinal)
       && string.Equals(Version ?? string.Empty, other.Version ?? string.Empty, StringComparison.Ordinal)
       && Depmod == other.Depmod;

  /// <summary>
  /// Materialises a native <c>alpm_depend_t</c> carrying this snapshot for native calls that accept a dependency
  /// pointer.
  /// </summary>
  /// <remarks>The caller owns the returned pointer and must release it using <see cref="FreeNative" />.</remarks>
  internal _alpm_depend_t* ToNative()
  {
    var native = (_alpm_depend_t*)NativeMemory.Alloc((nuint)sizeof(_alpm_depend_t));
    native->name = NativeString.ToNative(Name);
    native->version = NativeString.ToNative(Version);
    native->desc = NativeString.ToNative(Description);
    native->name_hash = default;
    native->mod_ = (_alpm_depmod_t)(uint)Depmod;
    return native;
  }

  /// <summary>Releases a struct produced by <see cref="ToNative" />.</summary>
  internal static void FreeNative(_alpm_depend_t* native)
  {
    if (native == null) return;

    if (native->name != null) NativeMemory.Free(native->name);
    if (native->version != null) NativeMemory.Free(native->version);
    if (native->desc != null) NativeMemory.Free(native->desc);
    NativeMemory.Free(native);
  }
}

/// <summary>A dependency that is missing from the transaction outcome.</summary>
/// <remarks>
/// Managed snapshot: the list libalpm dumps into <c>alpm_trans_prepare</c>'s output parameter is caller-owned, so
/// <see cref="Transaction.Prepare" /> copies the values out and frees the native memory (list and elements). No native
/// pointer is retained.
/// </remarks>
public sealed class MissingDependency
{
  private MissingDependency(string? target, string? causingPackage, Dependency? dependency)
  {
    Target = target;
    CausingPackage = causingPackage;
    Dependency = dependency;
  }

  // Token parameter unused: a DepMissing is an eager snapshot of caller-owned failure data.
  internal static unsafe MissingDependency Factory(void* native, Lifetime? lifetime)
    => new(NativeString.FromNative((nint)((_alpm_depmissing_t*)native)->target),
      NativeString.FromNative((nint)((_alpm_depmissing_t*)native)->causingpkg),
      ((_alpm_depmissing_t*)native)->depend != null ? new Dependency(((_alpm_depmissing_t*)native)->depend) : null);

  /// <summary>The dependency requirement that could not be satisfied.</summary>
  public Dependency? Dependency { get; }

  /// <summary>The name of the package that caused this dependency to be required, if known.</summary>
  public string? CausingPackage { get; }

  /// <summary>The target package or resource name that triggered the dependency check.</summary>
  public string? Target { get; }
}
