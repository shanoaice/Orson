using System.Runtime.InteropServices;
using Orson.Alpm.Bindings;
using Orson.Alpm.Interop;

namespace Orson.Alpm.Tests.Unit.Ownership;

/// <summary>
/// Locks in the list ownership contract: borrowed views can never free (and therefore are not disposable), and
/// the only owning type frees both the list and its elements.
/// </summary>
public sealed unsafe class ListOwnershipTests
{
  [Fact]
  public void Borrow_ReadsItems_WithoutFreeingTheList()
  {
    var lifetime = RootLifetime.Create(new object(), "test");
    var list = BuildStringList("alpha", "beta", "gamma");
    try
    {
      var view = AlpmList<string>.Borrow(list, &TestStringFactory, lifetime);

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
    var lifetime = RootLifetime.Create(new object(), "test");
    var view = AlpmList<string>.Borrow(null, &TestStringFactory, lifetime);

    Assert.Empty(view);
    Assert.Empty(view.ToArray());
  }

  [Fact]
  public void Enumerator_Current_ReturnsCachedItemWithoutReinvokingFactory()
  {
    var lifetime = RootLifetime.Create(new object(), "test");
    var list = BuildStringList("alpha");
    try
    {
      var view = AlpmList<string>.Borrow(list, &TestStringFactory, lifetime);
      using var enumerator = view.GetEnumerator();
      Assert.True(enumerator.MoveNext());

      var first = enumerator.Current;
      var second = enumerator.Current;

      Assert.Same(first, second);
    }
    finally
    {
      FreeNativeStringList(list);
    }
  }

  [Fact]
  public void Borrow_ElementAt_ThrowsOnOutOfRange()
    => Assert.Throws<ArgumentOutOfRangeException>(() =>
    {
      var lifetime = RootLifetime.Create(new object(), "test");
      var view = AlpmList<string>.Borrow(null, &TestStringFactory, lifetime);
      _ = view.ElementAt(0);
    });

  [Fact]
  public void TakeOwned_NullList_IsAnEmptyCollection()
    => Assert.Empty(AlpmStringList.TakeOwned(null, &UnmanagedDestructors.CFreeExtern));

  [Fact]
  public void TakeOwned_PopulatedList_ReturnsElements()
  {
    var list = BuildStringList("glibc", "bash", "zsh");
    var names = AlpmStringList.TakeOwned(list, &UnmanagedDestructors.CFreeExtern);

    Assert.Equal(["glibc", "bash", "zsh"], names);
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

  // The lifetime parameter matches the factory delegate shape; these strings are owned by the test
  // itself, so the factory ignores the token.
  private static string TestStringFactory(void* data, Lifetime lifetime)
    => Marshal.PtrToStringUTF8((nint)data) ?? string.Empty;
}
