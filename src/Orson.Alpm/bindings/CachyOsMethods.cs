using System.Runtime.InteropServices;

namespace Orson.Alpm.Bindings;

internal static unsafe class CachyOsMethods
{
  private struct Bindings
  {
    public delegate* unmanaged[Cdecl]<_alpm_handle_t*, _alpm_list_t*> GetPhysicalArchitectures;
    public delegate* unmanaged[Cdecl]<_alpm_pkg_t*, byte*> GetPackageInstalledDatabase;
  }

  private static readonly Bindings s_bindings = LoadBindings();

  internal static bool IsPhysicalArchitectureSupported => s_bindings.GetPhysicalArchitectures != null;
  internal static bool IsInstalledDatabaseSupported => s_bindings.GetPackageInstalledDatabase != null;

  private static Bindings LoadBindings()
  {
    if (!NativeLibrary.TryLoad("libalpm.so", out var libalpmHandle)
        && !NativeLibrary.TryLoad("libalpm", out libalpmHandle))
    {
      return default;
    }

    delegate* unmanaged[Cdecl]<_alpm_handle_t*, _alpm_list_t*> getPhysArch = null;
    delegate* unmanaged[Cdecl]<_alpm_pkg_t*, byte*> getInstalledDb = null;

    if (NativeLibrary.TryGetExport(libalpmHandle, "alpm_option_get_physical_architectures", out var pGetPhysicalArchitectures))
    {
      getPhysArch = (delegate* unmanaged[Cdecl]<_alpm_handle_t*, _alpm_list_t*>)pGetPhysicalArchitectures;
    }

    if (NativeLibrary.TryGetExport(libalpmHandle, "alpm_pkg_get_installed_db", out var pGetInstalledDb))
    {
      getInstalledDb = (delegate* unmanaged[Cdecl]<_alpm_pkg_t*, byte*>)pGetInstalledDb;
    }

    return new Bindings
    {
      GetPhysicalArchitectures = getPhysArch,
      GetPackageInstalledDatabase = getInstalledDb,
    };
  }

  internal static _alpm_list_t* alpm_option_get_physical_architectures(SafeAlpmHandle handle)
  {
    if (!IsPhysicalArchitectureSupported)
    {
      throw new PlatformNotSupportedException("The installed libalpm does not export alpm_option_get_physical_architectures.");
    }

    var success = false;
    handle.DangerousAddRef(ref success);
    try
    {
      return s_bindings.GetPhysicalArchitectures((_alpm_handle_t*)handle.DangerousGetHandle());
    }
    finally
    {
      if (success) handle.DangerousRelease();
    }
  }

  internal static byte* alpm_pkg_get_installed_db(_alpm_pkg_t* pkg)
  {
    if (!IsInstalledDatabaseSupported)
    {
      throw new PlatformNotSupportedException("The installed libalpm does not export alpm_pkg_get_installed_db.");
    }

    return s_bindings.GetPackageInstalledDatabase(pkg);
  }
}
