using System.Runtime.InteropServices;
using Orson.Alpm.Bindings;

namespace Orson.Alpm;

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

  /// <summary>
  /// Materializes a caller-owned native list of self-contained items into an array and frees the native list.
  /// </summary>
  internal static unsafe T[] MaterializeAndFree<T>(
    _alpm_list_t* list,
    delegate*<void*, T> factory,
    delegate* unmanaged[Cdecl]<void*, void> innerFree = null)
  {
    if (list == null) return [];

    try
    {
      var count = (int)NativeMethods.alpm_list_count(list);
      var result = new T[count];
      var i = 0;
      for (var node = list; node != null; node = node->next)
      {
        result[i++] = factory(node->data);
      }
      return result;
    }
    finally
    {
      Free(list, innerFree);
    }
  }

  /// <summary>
  /// Materializes a caller-owned native list of lifetime-anchored views into an array and frees the native list.
  /// </summary>
  internal static unsafe T[] MaterializeAndFree<T>(
    _alpm_list_t* list,
    delegate*<void*, Lifetime, T> factory,
    Lifetime lifetime,
    delegate* unmanaged[Cdecl]<void*, void> innerFree = null)
  {
    ArgumentNullException.ThrowIfNull(lifetime);
    if (list == null) return [];

    try
    {
      var count = (int)NativeMethods.alpm_list_count(list);
      var result = new T[count];
      var i = 0;
      for (var node = list; node != null; node = node->next)
      {
        result[i++] = factory(node->data, lifetime);
      }
      return result;
    }
    finally
    {
      Free(list, innerFree);
    }
  }
}
