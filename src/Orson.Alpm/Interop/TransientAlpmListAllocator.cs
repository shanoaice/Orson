using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using JetBrains.Annotations;
using Orson.Alpm.Bindings;

namespace Orson.Alpm.Interop;

/// <summary>
/// Builds a transient alpm_list_t for P/Invoke use.
/// </summary>
/// <remarks>
/// This is a mutable ref struct. If passed to a helper method, it MUST be passed by <see langword="ref"/>,
/// otherwise any heap chunks allocated inside the helper will be leaked, and the inline buffer will be on the wrong stack,
/// i.e. becomes dangling pointers once helper methods return.
/// </remarks>
[MustDisposeResource]
internal ref struct TransientAlpmListAllocator() : IDisposable
{
  private const int ListBufferCapacity = 32;

  [InlineArray(ListBufferCapacity)]
  private struct ListBuffer
  {
    [SuppressMessage("Roslynator", "RCS1213:Remove unused member declaration"),
     SuppressMessage("Roslynator", "RCS1169:Make field read-only")]
    private _alpm_list_t _elem0;
  }

  private ListBuffer _listBuffer = default;
  private int _listBufferOffset = 0;

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

    if (count <= ListBufferCapacity - _listBufferOffset)
    {
      buffer = (_alpm_list_t*)Unsafe.AsPointer(ref _listBuffer[_listBufferOffset]);
      _listBufferOffset += count;
    }
    else
    {
      var headerSize = (nuint)sizeof(HeapChunk);
      var payloadSize = (nuint)sizeof(_alpm_list_t) * (nuint)count;
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
