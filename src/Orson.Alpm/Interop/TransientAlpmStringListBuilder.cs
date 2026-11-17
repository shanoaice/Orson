using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Orson.Alpm.Bindings;

namespace Orson.Alpm.Interop;

/// <summary>
/// Allocates a transient alpm_list_t with strings encoded as UTF-8 from managed strings for P/Invoke use.
/// </summary>
/// <param name="listBuffer">The stack buffer for list entry allocation. You must NEVER pass a span backed by managed memory, or all bets are off.</param>
/// <param name="stringBuffer">Same thing but for storing the strings.</param>
internal ref struct TransientAlpmStringListBuilder(Span<_alpm_list_t> listBuffer, Span<byte> stringBuffer) : IDisposable
{
  private Span<_alpm_list_t> _listBuffer = listBuffer;
  private Span<byte> _stringBuffer = stringBuffer;

  private unsafe struct HeapChunk
  {
    public HeapChunk* Prev;
  }

  private unsafe HeapChunk* _heapLists = null;
  private unsafe HeapChunk* _heapStrings = null;

  internal unsafe _alpm_list_t* ConstructNative(IEnumerable<string> strings)
  {
    var listCount = strings.Count();

    if (listCount == 0) return null;

    _alpm_list_t* lists;

    if (listCount <= _listBuffer.Length)
    {
      lists = (_alpm_list_t*)Unsafe.AsPointer(MemoryMarshal.GetReference(_listBuffer));
      _listBuffer = _listBuffer[listCount..];
    }
    else
    {
      var prev = _heapLists;

      var headerSize = (nuint)sizeof(HeapChunk);
      var payloadSize = (nuint)(sizeof(_alpm_list_t) * listCount);
      _heapLists = (HeapChunk*)NativeMemory.Alloc(headerSize + payloadSize);
      _heapLists->Prev = prev;
      lists = (_alpm_list_t*)&_heapLists[1];
    }

    var byteCount = 0;

    // ReSharper disable once LoopCanBeConvertedToQuery
    foreach (var str in strings)
    {
      byteCount += Encoding.UTF8.GetByteCount(str) + 1;
    }

    Span<byte> buffer;

    if (byteCount <= _stringBuffer.Length)
    {
      buffer = _stringBuffer;
      _stringBuffer = _stringBuffer[byteCount..];
    }
    else
    {
      var prev = _heapStrings;

      var headerSize = (nuint)sizeof(HeapChunk);
      var payloadSize = (nuint)(sizeof(byte) * byteCount);

      _heapStrings = (HeapChunk*)NativeMemory.Alloc(headerSize + payloadSize);
      _heapStrings->Prev = prev;
      buffer = new Span<byte>((byte*)&_heapStrings[1], byteCount);
    }

    var idx = 0;

    foreach (var str in strings)
    {
      var bytesWritten = Encoding.UTF8.GetBytes(str, buffer);
      buffer[bytesWritten] = 0;

      lists[idx].data = Unsafe.AsPointer(MemoryMarshal.GetReference(buffer));
      lists[idx].prev = idx != 0 ? &lists[idx - 1] : &lists[listCount - 1];
      lists[idx].next = idx != listCount - 1 ? &lists[idx + 1] : null;

      buffer = buffer[(bytesWritten + 1)..];
      ++idx;
    }

    return lists;
  }

  public unsafe void Dispose()
  {
    var currentList = _heapLists;
    while (currentList != null)
    {
      var prev = currentList->Prev;
      NativeMemory.Free(currentList);
      currentList = prev;
    }

    _heapLists = null;

    var currentBuffer = _heapStrings;
    while (currentBuffer != null)
    {
      var prev = currentBuffer->Prev;
      NativeMemory.Free(currentBuffer);
      currentBuffer = prev;
    }

    _heapStrings = null;
  }
}
