using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Orson.Alpm.Bindings;
using Orson.Alpm.Interop;

namespace Orson.Alpm.Tests.Unit.Interop;

public class TransientAlpmStringListBuilderTest
{
  private static unsafe bool PointerInSpanRange<T>(in Span<T> span, void* pointer)
  {
    if (span.IsEmpty)
    {
      return false;
    }

    var start = (byte*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(span));
    var end = start + (nint)span.Length * Unsafe.SizeOf<T>();
    var target = (byte*)pointer;

    return target >= start && target < end;
  }

  [Fact]
  public unsafe void TransientAlpmStringListBuilder_PreservesOrderAndLoopsBack()
  {
    string[] strings = ["first", "second", "third"];
    var firstString = "first"u8;
    var secondString = "second"u8;
    var thirdString = "third"u8;

    using var builder = new TransientAlpmStringListBuilder(default, default);

    var first = builder.ConstructNative(strings);

    var second = first->next;
    var third = second->next;

    var firstData = MemoryMarshal.CreateReadOnlySpanFromNullTerminated((byte*)first->data);
    var secondData = MemoryMarshal.CreateReadOnlySpanFromNullTerminated((byte*)second->data);
    var thirdData = MemoryMarshal.CreateReadOnlySpanFromNullTerminated((byte*)third->data);

    Assert.True(firstData.SequenceEqual(firstString));
    Assert.True(secondData.SequenceEqual(secondString));
    Assert.True(thirdData.SequenceEqual(thirdString));

    Assert.True(first->prev == third);
    Assert.Null(third->next);
  }

  [Fact]
  public unsafe void TransientAlpmStringListBuilder_SupportsMultipleListBuilding()
  {
    var lifetime = RootLifetime.Create(this, "test frame");

    string[] strings1 = ["first", "second", "third"];
    string[] strings2 = ["first", "second"];
    string[] strings3 = ["foo", "bar", "baz"];

    using var builder = new TransientAlpmStringListBuilder(default, default);

    var firstNative = builder.ConstructNative(strings1);
    var secondNative = builder.ConstructNative(strings2);
    var thirdNative = builder.ConstructNative(strings3);

    Assert.True(new AlpmStringList(firstNative, lifetime).SequenceEqual(strings1));
    Assert.True(new AlpmStringList(secondNative, lifetime).SequenceEqual(strings2));
    Assert.True(new AlpmStringList(thirdNative, lifetime).SequenceEqual(strings3));
  }

  [Fact]
  public unsafe void TransientAlpmStringListBuilder_PlacesSmallListsOnStack()
  {
    string[] strings = ["foo", "bar", "baz"];

    Span<_alpm_list_t> stackList = stackalloc _alpm_list_t[4];
    Span<byte> stackBuffer = stackalloc byte[128];

    using var builder = new TransientAlpmStringListBuilder(stackList, stackBuffer);

    var list = builder.ConstructNative(strings);
    var second = list->next;
    var third = second->next;

    var firstString = list->data;
    var secondString = second->data;
    var thirdString = third->data;

    Assert.True(PointerInSpanRange(stackList, list));
    Assert.True(PointerInSpanRange(stackList, second));
    Assert.True(PointerInSpanRange(stackList, third));

    Assert.True(PointerInSpanRange(stackBuffer, firstString));
    Assert.True(PointerInSpanRange(stackBuffer, secondString));
    Assert.True(PointerInSpanRange(stackBuffer, thirdString));
  }

  [Fact]
  public unsafe void TransientAlpmStringListBuilder_SuccessfullyInterleavesStackAndHeapAllocation()
  {
    string[] list1 = ["a", "b"];
    string[] list2 = ["string exceeding remaining 12 bytes"];
    string[] list3 = ["c", "d", "e"];
    string[] list4 = ["hello"];
    string[] list5 = ["overflow"];

    Span<_alpm_list_t> stackList = stackalloc _alpm_list_t[4];
    Span<byte> stackBuffer = stackalloc byte[16];

    using var builder = new TransientAlpmStringListBuilder(stackList, stackBuffer);

    var nativeList1 = builder.ConstructNative(list1);

    var list1FirstData =  nativeList1->data;
    var list1Second =  nativeList1->next;
    var list1SecondData = list1Second->data;

    Assert.True(PointerInSpanRange(stackList, nativeList1));
    Assert.True(PointerInSpanRange(stackBuffer, list1FirstData));
    Assert.True(PointerInSpanRange(stackList, list1Second));
    Assert.True(PointerInSpanRange(stackBuffer, list1SecondData));

    var nativeList2 = builder.ConstructNative(list2);

    var list2FirstData = nativeList2->data;

    Assert.True(PointerInSpanRange(stackList, nativeList2));
    Assert.False(PointerInSpanRange(stackBuffer, list2FirstData));

    var nativeList3 = builder.ConstructNative(list3);
    var nativeList4 = builder.ConstructNative(list4);
    var nativeList5 = builder.ConstructNative(list5);
  }
}
