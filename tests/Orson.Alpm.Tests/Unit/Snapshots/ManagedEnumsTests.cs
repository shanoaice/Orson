using Orson.Alpm.Bindings;

namespace Orson.Alpm.Tests.Unit.Snapshots;

/// <summary>
/// Every managed enum mirrors its native counterpart value-for-value, so a libalpm update that adds a member
/// turns these tests red instead of silently dropping the new value.
/// </summary>
/// <remarks>
/// The mirror only covers enums the generated bindings project as a C# enum. The rest - the <c>alpm_db_usage_t</c> flags
/// behind <see cref="DatabaseUsage" /> and the capability bits behind <see cref="Capability" /> - arrive as a plain
/// integer, so there is no counterpart type to compare against; their values are pinned where the wrapper declares them
/// instead.
/// <para>
/// Which members the public surface uses is not checked here: a public member cannot carry an internal binding type
/// without the compiler refusing it, so that shape needs no test.
/// </para>
/// </remarks>
public sealed class ManagedEnumsTests
{
  [Theory]
  [InlineData(typeof(_alpm_pkgfrom_t), typeof(PackageOrigin))]
  [InlineData(typeof(_alpm_pkgreason_t), typeof(PackageReason))]
  [InlineData(typeof(_alpm_depmod_t), typeof(DependencyComparison))]
  [InlineData(typeof(_alpm_hook_when_t), typeof(HookWhen))]
  [InlineData(typeof(_alpm_package_operation_t), typeof(PackageOperation))]
  [InlineData(typeof(_alpm_progress_t), typeof(ProgressType))]
  [InlineData(typeof(_alpm_loglevel_t), typeof(LogLevel))]
  public void ManagedEnum_MatchesTheNativeEnum(Type nativeType, Type managedType)
    => AssertMirrors(nativeType, managedType);

  [Theory]
  [InlineData(typeof(TransactionFlags))]
  [InlineData(typeof(DatabaseUsage))]
  [InlineData(typeof(SigLevel))]
  [InlineData(typeof(Capability))]
  [InlineData(typeof(LogLevel))]
  public void FlagEnums_DefineZeroAsNone(Type flagEnumType)
  {
    Assert.True(flagEnumType.IsDefined(typeof(FlagsAttribute), false));
    Assert.True(Enum.IsDefined(flagEnumType, 0u));
    Assert.Equal("None", Enum.GetName(flagEnumType, 0u));
  }

  /// <summary>Both enums must cover exactly the same numeric values.</summary>
  private static void AssertMirrors(Type nativeType, Type managedType)
  {
    foreach (var native in Enum.GetValues(nativeType))
    {
      var value = Convert.ToUInt32(native);
      var managed = Enum.ToObject(managedType, value);

      Assert.True(Enum.IsDefined(managedType, managed), $"{managedType.Name} has no member for {native} ({value})");
    }

    foreach (var managed in Enum.GetValues(managedType))
    {
      var value = Convert.ToUInt32(managed);
      if (value == 0 && managedType.IsDefined(typeof(FlagsAttribute), false))
      {
        continue;
      }

      var native = Enum.ToObject(nativeType, value);

      Assert.True(Enum.IsDefined(nativeType, native), $"{nativeType.Name} has no member for {managed} ({value})");
    }
  }
}
