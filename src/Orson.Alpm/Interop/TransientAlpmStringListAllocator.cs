using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using JetBrains.Annotations;
using Orson.Alpm.Bindings;

namespace Orson.Alpm.Interop;

/// <summary>
/// Allocates a transient alpm_list_t with strings encoded as UTF-8 from managed strings for P/Invoke use.
/// </summary>
/// <remarks>
/// This is a mutable ref struct. If passed to a helper method, it MUST be passed by <see langword="ref"/>,
/// otherwise any heap chunks allocated inside the helper will be leaked, and the inline buffer will be on the wrong stack,
/// i.e. becomes dangling pointers once helper methods return.
/// </remarks>
[MustDisposeResource]
internal ref struct TransientAlpmStringListAllocator() : IDisposable
{
  private const int ListBufferCapacity = 4;
  private const int StringBufferCapacity = 512;

  [InlineArray(ListBufferCapacity)]
  private struct ListBuffer
  {
    [SuppressMessage("Roslynator", "RCS1213:Remove unused member declaration"),
     SuppressMessage("Roslynator", "RCS1169:Make field read-only")]
    private _alpm_list_t _elem0;
  }

  [InlineArray(StringBufferCapacity)]
  private struct StringBuffer
  {
    [SuppressMessage("Roslynator", "RCS1213:Remove unused member declaration"),
     SuppressMessage("Roslynator", "RCS1169:Make field read-only")]
    private byte _elem0;
  }

  private ListBuffer _listBuffer = default;
  private int _listBufferOffset = 0;

  private StringBuffer _stringBuffer = default;
  private int _stringBufferOffset = 0;


  private unsafe struct HeapChunk
  {
    public HeapChunk* Prev;
  }

  private unsafe HeapChunk* _heapLists = null;
  private unsafe HeapChunk* _heapStrings = null;

  internal unsafe _alpm_list_t* ConstructNative(IReadOnlyCollection<string> strings)
  {
    var listCount = strings.Count;

    if (listCount == 0) return null;

    _alpm_list_t* lists;

    if (listCount <= ListBufferCapacity - _listBufferOffset)
    {
      lists = (_alpm_list_t*)Unsafe.AsPointer(ref _listBuffer[_listBufferOffset]);
      _listBufferOffset += listCount;
    }
    else
    {
      var prev = _heapLists;

      var headerSize = (nuint)sizeof(HeapChunk);
      var payloadSize = (nuint)sizeof(_alpm_list_t) * (nuint)listCount;
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

    scoped Span<byte> buffer;

    if (byteCount <= StringBufferCapacity - _stringBufferOffset)
    {
      buffer = _stringBuffer[_stringBufferOffset..(_stringBufferOffset + byteCount)];
      _stringBufferOffset += byteCount;
    }
    else
    {
      var prev = _heapStrings;

      var headerSize = (nuint)sizeof(HeapChunk);
      var payloadSize = (nuint)byteCount;

      _heapStrings = (HeapChunk*)NativeMemory.Alloc(headerSize + payloadSize);
      _heapStrings->Prev = prev;
      buffer = new Span<byte>((byte*)&_heapStrings[1], byteCount);
    }

    var idx = 0;

    foreach (var str in strings)
    {
      var bytesWritten = Encoding.UTF8.GetBytes(str, buffer);

      buffer[bytesWritten] = 0;

      lists[idx].data = Unsafe.AsPointer(ref MemoryMarshal.GetReference(buffer));
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
