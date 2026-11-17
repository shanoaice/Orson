using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using JetBrains.Annotations;
using Orson.Alpm.Bindings;

namespace Orson.Alpm.Interop;

/// <summary>
/// Builds a transient alpm_list_t for P/Invoke use.
/// </summary>
/// <param name="stack">The stack buffer for allocation. You must NEVER pass a span backed by managed memory, or all bets are off.</param>
[MustDisposeResource]
internal ref struct TransientAlpmListBuilder(Span<_alpm_list_t> stack) : IDisposable
{
  private Span<_alpm_list_t> _stack = stack;

  private unsafe struct HeapChunk
  {
    public HeapChunk* Prev;
  }

  private unsafe HeapChunk* _heap = null;

  internal unsafe _alpm_list_t* ConstructNative<T>(IEnumerable<T> managedList)
    where T : INativeWrapper
  {
    var count = managedList.Count();

    if (count == 0) return null;

    _alpm_list_t* buffer;

    if (count <= _stack.Length)
    {
      buffer = (_alpm_list_t*)Unsafe.AsPointer(ref _stack[0]);
      _stack = _stack[count..];
    }
    else
    {
      var headerSize = (nuint)sizeof(HeapChunk);
      var payloadSize = (nuint)(sizeof(_alpm_list_t) * count);
      var prevHeap = _heap;
      _heap = (HeapChunk*)NativeMemory.Alloc(headerSize + payloadSize);
      _heap->Prev = prevHeap;
      buffer = (_alpm_list_t*)&_heap[1];
    }

    var idx = 0;
    foreach (var item in managedList)
    {
      buffer[idx].data = item.BackingStruct;
      buffer[idx].prev = idx != 0 ? &buffer[idx - 1] : &buffer[count - 1];
      buffer[idx].next = idx != count - 1 ? &buffer[idx + 1] : null;
      ++idx;
    }

    return buffer;
  }

  public unsafe void Dispose()
  {
    var current = _heap;
    while (current != null)
    {
      var prev = current->Prev;
      NativeMemory.Free(current);
      current = prev;
    }

    _heap = null;
  }
}
