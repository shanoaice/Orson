using System.Buffers;
using System.Runtime.InteropServices;
using System.Text;

namespace Orson.Alpm;

/// <summary>Caches the managed string for one recurring native UTF-8 value.</summary>
internal sealed class Utf8StringCache
{
  private const int MinimumBufferLength = 256;

  private byte[]? _buffer;
  private int _length;
  private string _value = "";
  private bool _hasValue;

  internal unsafe string GetOrCreate(byte* value)
  {
    if (value == null) return "";

    var utf8 = MemoryMarshal.CreateReadOnlySpanFromNullTerminated(value);
    return GetOrCreate(utf8);
  }

  internal string GetOrCreate(ReadOnlySpan<byte> utf8)
  {
    if (_hasValue && _length == utf8.Length && utf8.SequenceEqual(_buffer.AsSpan(0, _length)))
    {
      return _value;
    }

    if (utf8.IsEmpty)
    {
      _length = 0;
      _value = "";
      _hasValue = true;
      return _value;
    }

    if (_buffer == null || _buffer.Length < utf8.Length)
    {
      if (_buffer != null) ArrayPool<byte>.Shared.Return(_buffer);
      _buffer = ArrayPool<byte>.Shared.Rent(Math.Max(utf8.Length, MinimumBufferLength));
    }

    utf8.CopyTo(_buffer);
    _length = utf8.Length;
    _value = Encoding.UTF8.GetString(utf8);
    _hasValue = true;
    return _value;
  }

  internal void Release()
  {
    if (_buffer == null) return;

    ArrayPool<byte>.Shared.Return(_buffer);
    _buffer = null;
    _length = 0;
    _value = "";
    _hasValue = false;
  }
}
