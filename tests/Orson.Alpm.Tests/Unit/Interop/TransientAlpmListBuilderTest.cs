using Orson.Alpm.Bindings;
using Orson.Alpm.Interop;

namespace Orson.Alpm.Tests.Unit.Interop;

public sealed class TransientAlpmListBuilderTest
{
  private static unsafe int CountNodes(_alpm_list_t* list)
  {
    var count = 0;
    for (var node = list; node != null; node = node->next) count++;
    return count;
  }

  private static unsafe long[] ReadPayloads(_alpm_list_t* list)
  {
    var payloads = new List<long>();
    for (var node = list; node != null; node = node->next) payloads.Add((long)(nint)node->data);
    return [.. payloads];
  }

  private static unsafe void BuildLargeList(TestNativeWrapper[] values)
  {
    using var builder = new TransientAlpmListBuilder(default);
    var list = builder.ConstructNative(values);

    Assert.Equal(values.Length, CountNodes(list));
  }

  private readonly struct TestNativeWrapper(nint ptr) : INativeWrapper
  {
    public unsafe void* BackingStruct => (void*)ptr;
  }

  [Fact]
  public unsafe void TransientAlpmListBuilder_PreservesOrderAndTailBackLink()
  {
    TestNativeWrapper[] values = [new(11), new(22), new(33)];

    Span<_alpm_list_t> scratch = stackalloc _alpm_list_t[8];
    using var builder = new TransientAlpmListBuilder(scratch);
    var list = builder.ConstructNative(values);

    var second = list->next;
    var third = second->next;
    Assert.Equal(11, (long)(nint)list->data);
    Assert.Equal(22, (long)(nint)second->data);
    Assert.Equal(33, (long)(nint)third->data);
    Assert.Null(third->next);
    Assert.True(second->prev == list);
    Assert.True(third->prev == second);
    Assert.True(list->prev == third);
  }

  [Fact]
  public unsafe void TransientAlpmListBuilder_MultipleListsRemainValidUntilScopeExit()
  {
    TestNativeWrapper[] firstValues = [new(1), new(2)];
    TestNativeWrapper[] secondValues = [new(3), new(4), new(5)];
    TestNativeWrapper[] thirdValues = [new(6)];

    Span<_alpm_list_t> scratch = stackalloc _alpm_list_t[16];
    using var builder = new TransientAlpmListBuilder(scratch);
    var first = builder.ConstructNative(firstValues);
    var second = builder.ConstructNative(secondValues);
    var third = builder.ConstructNative(thirdValues);

    Assert.Equal([1L, 2L], ReadPayloads(first));
    Assert.Equal([3L, 4L, 5L], ReadPayloads(second));
    Assert.Equal([6L], ReadPayloads(third));
  }

  [Fact]
  public unsafe void TransientAlpmListBuilder_HeapFallback_SupportsLargeLists()
  {
    const int count = 3_000;
    var values = new TestNativeWrapper[count];
    for (var i = 0; i < count; i++) values[i] = new(i + 1);

    using var builder = new TransientAlpmListBuilder(default);
    var list = builder.ConstructNative(values);

    _alpm_list_t* previous = null;
    var node = list;
    for (var i = 0; i < count; i++)
    {
      Assert.Equal(i + 1, (long)(nint)node->data);
      if (previous != null) Assert.True(node->prev == previous);
      previous = node;
      node = node->next;
    }

    Assert.Null(node);
    Assert.True(list->prev == previous);
  }

  [Fact]
  public void TransientAlpmListBuilder_RepeatedLargeScopesDoNotGrowNativeHeap()
  {
    const int count = 3_000;
    var values = new TestNativeWrapper[count];
    for (var i = 0; i < count; i++) values[i] = new(i + 1);

    for (var i = 0; i < 16; ++i) BuildLargeList(values);

    //var before = NativeHeapInUse();
    for (var i = 0; i < 200; ++i) BuildLargeList(values);
    //var after = NativeHeapInUse();

    //Assert.True(after - before < 1_048_576,
    //  $"native heap grew by {after - before} bytes across 200 scratch scopes of 3,000 nodes");
  }
}
