using BenchmarkDotNet.Attributes;
using Orson.Alpm;
using Orson.Alpm.Bindings;
using Orson.Alpm.Interop;

namespace Orson.Benchmarks;

/// <summary>
/// Measures the performance of building transient <c>alpm_list_t</c> pointer chains.
/// </summary>
[MemoryDiagnoser]
[ProcessCount(1)]
[WarmupCount(3)]
[IterationCount(10)]
public unsafe class NativeListScratchBenchmarks
{
  private TestItem[] _items = null!;

  /// <summary>Number of nodes to construct per list.</summary>
  [Params(32, 256, 1024)]
  public int Count { get; set; }

  [GlobalSetup]
  public void Setup()
  {
    _items = new TestItem[Count];
    for (var i = 0; i < Count; i++) _items[i] = new(i + 1);

    var heapTail = BuildHeapList();
    var builderTail = BuildTransientList();
    if (heapTail != Count || builderTail != Count)
    {
      throw new InvalidOperationException($"Benchmark paths disagree: heap={heapTail}, builder={builderTail}, expected={Count}.");
    }
  }

  /// <summary>Builds the native list via individual <c>alpm_list_add</c> calls.</summary>
  [Benchmark(Baseline = true)]
  public nint Heap_AlpmListAdd() => BuildHeapList();

  /// <summary>Builds the native list via <see cref="TransientAlpmListAllocator" />.</summary>
  [Benchmark]
  public nint Builder_TransientAlpmList() => BuildTransientList();

  private nint BuildHeapList()
  {
    _alpm_list_t* list = null;
    try
    {
      foreach (var item in _items) list = NativeMethods.alpm_list_add(list, (void*)item.Pointer);
      return TailPayload(list);
    }
    finally
    {
      NativeListMarshal.Free(list);
    }
  }

  private nint BuildTransientList()
  {
    using var builder = new TransientAlpmListAllocator();
    var list = builder.ConstructNative(_items);
    return TailPayload(list);
  }

  private static nint TailPayload(_alpm_list_t* list) => list == null ? 0 : (nint)list->prev->data;

  private readonly struct TestItem(nint ptr) : INativeWrapper
  {
    public nint Pointer => ptr;
    public unsafe void* BackingStruct => (void*)ptr;
  }
}
