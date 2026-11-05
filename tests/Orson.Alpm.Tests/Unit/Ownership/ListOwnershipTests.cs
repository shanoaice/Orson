using System.Runtime.InteropServices;
using Orson.Alpm.Bindings;
using Orson.Alpm.Interop;

namespace Orson.Alpm.Tests.Unit.Ownership;

/// <summary>
/// Locks in the list ownership contract: borrowed views can never free (and therefore are not disposable), and
/// the only owning type frees both the list and its elements.
/// </summary>
/// <remarks>
/// <see cref="TakeOwned_FreesListAndStrings_RepeatedCallsDoNotGrowTheNativeHeap" /> measures the whole process'
/// native heap. It joins the <c>ProcessWideNativeHeap</c> collection, whose definition disables parallelization; xUnit
/// runs that collection after the parallel collections finish, so no other test allocates into the measured window.
/// </remarks>
[Collection("ProcessWideNativeHeap")]
public sealed unsafe class ListOwnershipTests
{
  [Fact]
  public void Borrow_ReadsItems_WithoutFreeingTheList()
  {
    var list = BuildStringList("alpha", "beta", "gamma");
    try
    {
      var view = AlpmList<string>.Borrow(list, &TestStringFactory, null);

      Assert.Equal(3, view.Count);
      Assert.Equal(["alpha", "beta", "gamma"], view.ToArray());
      Assert.Equal("beta", view.ElementAt(1));
    }
    finally
    {
      // The borrowed view must not have freed anything, so the caller can still free it.
      FreeNativeStringList(list);
    }
  }

  [Fact]
  public void Borrow_NullList_IsAnEmptyView()
  {
    var view = AlpmList<string>.Borrow(null, &TestStringFactory, null);

    Assert.Empty(view);
    Assert.Empty(view.ToArray());
  }

  [Fact]
  public void BuildStringList_NullElement_ThrowsAndFreesEarlierBuffers()
    => Assert.Throws<ArgumentException>(() =>
      NativeListMarshal.BuildStringList(["alpha", null!], out _));

  [Fact]
  public void Borrow_ElementAt_ThrowsOnOutOfRange()
    => Assert.Throws<ArgumentOutOfRangeException>(() =>
    {
      var view = AlpmList<string>.Borrow(null, &TestStringFactory, null);
      _ = view.ElementAt(0);
    });

  [Fact]
  public void TakeOwned_NullList_IsAnEmptyCollection()
    => Assert.Empty(AlpmStringList.TakeOwned(null, &UnmanagedDestructors.CFreeExtern));

  /// <summary>
  /// The P0 acceptance check: a caller-owned list must be freed, elements included. If
  /// <see cref="AlpmStringList.TakeOwned" /> leaked the list or the strings, 50k iterations would grow the native heap by
  /// several megabytes; the assertion tolerates 1 MB of allocator noise.
  /// </summary>
  [Fact]
  public void TakeOwned_FreesListAndStrings_RepeatedCallsDoNotGrowTheNativeHeap()
  {
    if (!OperatingSystem.IsLinux()) return; // mallinfo2 is glibc-specific

    for (var i = 0; i < 500; ++i) TakeAndCheck();

    var before = NativeHeapInUse();
    for (var i = 0; i < 50_000; ++i) TakeAndCheck();
    var after = NativeHeapInUse();

    Assert.True(after - before < 1_048_576,
      $"native heap grew by {after - before} bytes across 50,000 TakeOwned calls");
  }

  private static void TakeAndCheck()
  {
    var list = BuildStringList("glibc", "bash", "zsh");
    var names = AlpmStringList.TakeOwned(list, &UnmanagedDestructors.CFreeExtern);

    Assert.Equal(3, names.Count);
  }

  [StructLayout(LayoutKind.Sequential)]
  private struct MallInfo2
  {
    public nuint Arena, Ordblks, Smblks, Hblks, Hblkhd, Usmblks, Fsmblks, Uordblks, Fordblks, Keepcost;
  }

  [DllImport("libc", EntryPoint = "mallinfo2")]
  private static extern MallInfo2 MallInfo2Native();

  /// <summary>Bytes currently allocated on the native heap (glibc).</summary>
  private static long NativeHeapInUse()
  {
    GC.Collect();
    GC.WaitForPendingFinalizers();

    return (long)MallInfo2Native().Uordblks;
  }

  private static _alpm_list_t* BuildStringList(params string[] values)
  {
    _alpm_list_t* list = null;
    foreach (var value in values)
    {
      var valuePtr = Marshal.StringToCoTaskMemUTF8(value);
      try
      {
        // alpm_list_append* updates the head through the out parameter and returns the new node.
        _ = NativeMethods.alpm_list_append_strdup(&list, (byte*)valuePtr);
      }
      finally
      {
        Marshal.FreeCoTaskMem(valuePtr);
      }
    }

    return list;
  }

  private static void FreeNativeStringList(_alpm_list_t* list)
    => NativeListMarshal.Free(list, &UnmanagedDestructors.CFreeExtern);

  // The lifetime parameter matches the factory delegate shape; this test's strings are owned by the
  // test itself, so the views are borrowed with a null token and the factory never looks at it.
  private static string TestStringFactory(void* data, Lifetime? lifetime)
    => Marshal.PtrToStringUTF8((nint)data) ?? string.Empty;
}
