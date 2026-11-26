using System.Runtime.CompilerServices;
using Orson.Alpm.Bindings;
using Orson.Alpm.Interop;

namespace Orson.Alpm.Tests.Unit.Interop;

public sealed class TransientAlpmListAllocatorTests
{
  private readonly struct TestNativeWrapper(nint ptr) : INativeWrapper
  {
    public unsafe void* BackingStruct => (void*)ptr;
  }

  private static unsafe bool PointerInRange(void* start, int byteLength, void* target)
  {
    var s = (byte*)start;
    var t = (byte*)target;
    return t >= s && t < s + byteLength;
  }

  private static TestNativeWrapper[] CreateTestWrapperList(int count, int startPayload = 1)
  {
    var values = new TestNativeWrapper[count];
    for (var i = 0; i < count; i++) values[i] = new TestNativeWrapper(startPayload + i);
    return values;
  }

  private static unsafe void AssertValidList(
    _alpm_list_t* head,
    ReadOnlySpan<TestNativeWrapper> expected,
    void* builderAddr = null,
    int builderSize = 0,
    bool? expectStackNodes = null)
  {
    Assert.NotNull(head);

    _alpm_list_t* prev = null;
    var node = head;

    foreach (var item in expected)
    {
      Assert.NotNull(node);
      if (prev != null) Assert.True(node->prev == prev);
      if (expectStackNodes.HasValue)
        Assert.Equal(expectStackNodes.Value, PointerInRange(builderAddr, builderSize, node));

      Assert.True(node->data == item.BackingStruct);
      prev = node;
      node = node->next;
    }

    Assert.Null(node);
    Assert.True(head->prev == prev);
  }

  [Fact]
  public unsafe void TransientAlpmListBuilder_EmptyInput_ReturnsNullAndConsumesNoCapacity()
  {
    var exactFull = CreateTestWrapperList(32);

    using var builder = new TransientAlpmListAllocator();
    var builderAddr = Unsafe.AsPointer(in builder);
    var builderSize = sizeof(TransientAlpmListAllocator);

    var empty = builder.ConstructNative<TestNativeWrapper>([]);
    var full = builder.ConstructNative(exactFull);

    Assert.Null(empty);
    AssertValidList(full, exactFull, builderAddr, builderSize, expectStackNodes: true);
  }

  [Fact]
  public unsafe void TransientAlpmListBuilder_Singleton_SetsSelfBackLinkAndNullNext()
  {
    TestNativeWrapper[] single = [new(42)];

    using var builder = new TransientAlpmListAllocator();
    var builderAddr = Unsafe.AsPointer(in builder);
    var builderSize = sizeof(TransientAlpmListAllocator);

    var list = builder.ConstructNative(single);

    AssertValidList(list, single, builderAddr, builderSize, expectStackNodes: true);
  }

  [Fact]
  public unsafe void TransientAlpmListBuilder_PreservesOrderAndTailBackLink()
  {
    TestNativeWrapper[] values = [new(11), new(22), new(33)];

    using var builder = new TransientAlpmListAllocator();
    var builderAddr = Unsafe.AsPointer(in builder);
    var builderSize = sizeof(TransientAlpmListAllocator);

    var list = builder.ConstructNative(values);

    AssertValidList(list, values, builderAddr, builderSize, expectStackNodes: true);
  }

  [Fact]
  public unsafe void TransientAlpmListBuilder_MultipleListsRemainValidUntilScopeExit()
  {
    TestNativeWrapper[] firstValues = [new(1), new(2)];
    TestNativeWrapper[] secondValues = [new(3), new(4), new(5)];
    TestNativeWrapper[] thirdValues = [new(6)];

    using var builder = new TransientAlpmListAllocator();
    var builderAddr = Unsafe.AsPointer(in builder);
    var builderSize = sizeof(TransientAlpmListAllocator);

    var first = builder.ConstructNative(firstValues);
    var second = builder.ConstructNative(secondValues);
    var third = builder.ConstructNative(thirdValues);

    AssertValidList(first, firstValues, builderAddr, builderSize, expectStackNodes: true);
    AssertValidList(second, secondValues, builderAddr, builderSize, expectStackNodes: true);
    AssertValidList(third, thirdValues, builderAddr, builderSize, expectStackNodes: true);
  }

  [Fact]
  public unsafe void TransientAlpmListBuilder_InterleavesStackAndHeapAllocations()
  {
    var list1 = CreateTestWrapperList(20, startPayload: 1);
    var list2 = CreateTestWrapperList(15, startPayload: 101);
    var list3 = CreateTestWrapperList(12, startPayload: 201);
    var list4 = CreateTestWrapperList(5, startPayload: 301);

    using var builder = new TransientAlpmListAllocator();
    var builderAddr = Unsafe.AsPointer(in builder);
    var builderSize = sizeof(TransientAlpmListAllocator);

    var nativeList1 = builder.ConstructNative(list1);
    var nativeList2 = builder.ConstructNative(list2);
    var nativeList3 = builder.ConstructNative(list3);
    var nativeList4 = builder.ConstructNative(list4);

    AssertValidList(nativeList1, list1, builderAddr, builderSize, expectStackNodes: true);
    AssertValidList(nativeList2, list2, builderAddr, builderSize, expectStackNodes: false);
    AssertValidList(nativeList3, list3, builderAddr, builderSize, expectStackNodes: true);
    AssertValidList(nativeList4, list4, builderAddr, builderSize, expectStackNodes: false);
  }

  [Fact]
  public unsafe void TransientAlpmListBuilder_ExactNodeBoundaries_SelectExpectedStorage()
  {
    var seed = CreateTestWrapperList(20, startPayload: 1);
    var exactRemaining = CreateTestWrapperList(12, startPayload: 101);
    var zeroLeftOverflow = CreateTestWrapperList(1, startPayload: 201);

    using var exactBuilder = new TransientAlpmListAllocator();
    var exactAddr = Unsafe.AsPointer(in exactBuilder);
    var exactSize = sizeof(TransientAlpmListAllocator);

    var seedList = exactBuilder.ConstructNative(seed);
    var exactList = exactBuilder.ConstructNative(exactRemaining);
    var overZeroList = exactBuilder.ConstructNative(zeroLeftOverflow);

    AssertValidList(seedList, seed, exactAddr, exactSize, expectStackNodes: true);
    AssertValidList(exactList, exactRemaining, exactAddr, exactSize, expectStackNodes: true);
    AssertValidList(overZeroList, zeroLeftOverflow, exactAddr, exactSize, expectStackNodes: false);

    var oneOverRemaining = CreateTestWrapperList(13, startPayload: 301);

    using var spillBuilder = new TransientAlpmListAllocator();
    var spillAddr = Unsafe.AsPointer(in spillBuilder);
    var spillSize = sizeof(TransientAlpmListAllocator);

    var spillSeed = spillBuilder.ConstructNative(seed);
    var spilled = spillBuilder.ConstructNative(oneOverRemaining);

    AssertValidList(spillSeed, seed, spillAddr, spillSize, expectStackNodes: true);
    AssertValidList(spilled, oneOverRemaining, spillAddr, spillSize, expectStackNodes: false);
  }

  [Fact]
  public unsafe void TransientAlpmListBuilder_HeapFallback_SupportsLargeLists()
  {
    var values = CreateTestWrapperList(3_000);

    using var builder = new TransientAlpmListAllocator();
    var builderAddr = Unsafe.AsPointer(in builder);
    var builderSize = sizeof(TransientAlpmListAllocator);

    var list = builder.ConstructNative(values);

    AssertValidList(list, values, builderAddr, builderSize, expectStackNodes: false);
  }
}
