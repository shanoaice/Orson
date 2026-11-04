using System.Runtime.InteropServices;
using Pacpar.Alpm.Bindings;

namespace Pacpar.Alpm;

/// <summary>
/// Marshalling helpers for building and freeing native <c>alpm_list_t</c> structures passed across P/Invoke boundaries.
/// </summary>
internal static class NativeListMarshal
{
  /// <summary>Frees a native list and optionally its elements. Safe with a <c>null</c> pointer.</summary>
  /// <param name="list">The native list to free, or <c>null</c>.</param>
  /// <param name="innerFree">Optional element destructor, or <c>null</c> if elements do not require individual destruction.</param>
  internal static unsafe void Free(_alpm_list_t* list, delegate* unmanaged[Cdecl]<void*, void> innerFree = null)
  {
    if (list == null) return;

    if (innerFree != null) NativeMethods.alpm_list_free_inner(list, innerFree);
    NativeMethods.alpm_list_free(list);
  }

  internal static unsafe _alpm_list_t* BuildPointerList(void* pointer)
  {
    return pointer != null ? NativeMethods.alpm_list_add(null, pointer) : null;
  }

  internal static unsafe _alpm_list_t* BuildPointerList(IEnumerable<PackageView> packages)
  {
    ArgumentNullException.ThrowIfNull(packages);

    _alpm_list_t* list = null;
    try
    {
      foreach (var pkg in packages)
      {
        if (pkg is null)
        {
          throw new ArgumentException("The collection contains a null package.", nameof(packages));
        }

        list = NativeMethods.alpm_list_add(list, pkg.BackingStruct);
      }
      return list;
    }
    catch
    {
      if (list != null) NativeMethods.alpm_list_free(list);
      throw;
    }
  }

  internal static unsafe _alpm_list_t* BuildPointerList(IEnumerable<Database> databases)
  {
    ArgumentNullException.ThrowIfNull(databases);

    _alpm_list_t* list = null;
    try
    {
      foreach (var db in databases)
      {
        if (db is null)
        {
          throw new ArgumentException("The collection contains a null database.", nameof(databases));
        }

        list = NativeMethods.alpm_list_add(list, db.ValidatedPtr);
      }
      return list;
    }
    catch
    {
      if (list != null) NativeMethods.alpm_list_free(list);
      throw;
    }
  }

  internal static unsafe _alpm_list_t* BuildPointerList(IEnumerable<nint> pointers)
  {
    ArgumentNullException.ThrowIfNull(pointers);

    _alpm_list_t* list = null;
    try
    {
      foreach (var ptr in pointers)
      {
        list = NativeMethods.alpm_list_add(list, (void*)ptr);
      }
      return list;
    }
    catch
    {
      if (list != null) NativeMethods.alpm_list_free(list);
      throw;
    }
  }

  internal static unsafe _alpm_list_t* BuildStringList(IEnumerable<string> strings, out List<nint> allocatedBuffers)
  {
    ArgumentNullException.ThrowIfNull(strings);

    allocatedBuffers = [];
    _alpm_list_t* list = null;
    try
    {
      foreach (var str in strings)
      {
        if (str is null)
        {
          throw new ArgumentException("The collection contains a null string.", nameof(strings));
        }

        var ptr = (nint)NativeString.ToNative(str);
        allocatedBuffers.Add(ptr);
        list = NativeMethods.alpm_list_add(list, (void*)ptr);
      }
      return list;
    }
    catch
    {
      FreeStringListBuffers(allocatedBuffers, list);
      allocatedBuffers.Clear();
      throw;
    }
  }

  internal static unsafe void FreeStringListBuffers(List<nint>? allocatedBuffers, _alpm_list_t* list)
  {
    if (allocatedBuffers != null)
    {
      foreach (var buf in allocatedBuffers)
      {
        if (buf != 0) NativeMemory.Free((void*)buf);
      }
    }
    if (list != null) NativeMethods.alpm_list_free(list);
  }
}
