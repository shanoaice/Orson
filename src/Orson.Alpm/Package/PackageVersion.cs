using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Orson.Alpm.Bindings;

namespace Orson.Alpm;

[InlineArray(InlineCapacity)]
internal struct InlineVersionBuffer
{
  internal const int InlineCapacity = 31;

  [SuppressMessage("Roslynator", "RCS1213:Remove unused member declaration"),
   SuppressMessage("Roslynator", "RCS1169:Make field read-only")]
  private byte _element0;
}

/// <summary>
/// A package version (<c>alpm_pkg_get_version</c>), ordered by libalpm's own <c>alpm_pkg_vercmp</c>.
/// </summary>
/// <remarks>
/// Versions with UTF-8 byte lengths up to 30 bytes are stored inline without heap allocation. Longer versions
/// fall back to a heap-allocated buffer.
/// </remarks>
public readonly struct PackageVersion : IComparable, IComparable<PackageVersion>, IEquatable<PackageVersion>,
  IUtf8SpanFormattable, ISpanFormattable
{
  private const byte HeapSentinel = 0xFF;

  private readonly byte _length;
  private readonly InlineVersionBuffer _inline;
  private readonly byte[]? _heap;

  internal unsafe PackageVersion(byte* nativeVersion)
  {
    if (nativeVersion == null || *nativeVersion == 0)
    {
      _length = 0;
      _inline = default;
      _heap = null;
      return;
    }

    var span = MemoryMarshal.CreateReadOnlySpanFromNullTerminated(nativeVersion);
    var len = span.Length;

    if (len < InlineVersionBuffer.InlineCapacity)
    {
      _length = (byte)len;
      _heap = null;
      _inline = default;
      var dest = MemoryMarshal.CreateSpan(ref _inline[0], InlineVersionBuffer.InlineCapacity);
      span.CopyTo(dest);
      dest[len] = 0; // null-terminated for alpm_pkg_vercmp
    }
    else
    {
      _length = HeapSentinel;
      var buffer = new byte[len + 1];
      span.CopyTo(buffer);
      buffer[len] = 0; // null-terminated for alpm_pkg_vercmp
      _heap = buffer;
      _inline = default;
    }
  }

  /// <summary>Initializes a new instance of <see cref="PackageVersion" /> from a UTF-8 byte span.</summary>
  public PackageVersion(ReadOnlySpan<byte> utf8Version)
  {
    if (utf8Version.IsEmpty)
    {
      _length = 0;
      _inline = default;
      _heap = null;
      return;
    }

    var len = utf8Version.Length;
    if (len < InlineVersionBuffer.InlineCapacity)
    {
      _length = (byte)len;
      _heap = null;
      _inline = default;
      var dest = MemoryMarshal.CreateSpan(ref _inline[0], InlineVersionBuffer.InlineCapacity);
      utf8Version.CopyTo(dest);
      dest[len] = 0;
    }
    else
    {
      _length = HeapSentinel;
      var buffer = new byte[len + 1];
      utf8Version.CopyTo(buffer);
      buffer[len] = 0;
      _heap = buffer;
      _inline = default;
    }
  }

  /// <summary>Initializes a new instance of <see cref="PackageVersion" /> from a managed string.</summary>
  public PackageVersion(string version)
  {
    ArgumentNullException.ThrowIfNull(version);
    if (version.Length == 0)
    {
      _length = 0;
      _inline = default;
      _heap = null;
      return;
    }

    var maxByteCount = Encoding.UTF8.GetByteCount(version);
    if (maxByteCount < InlineVersionBuffer.InlineCapacity)
    {
      _inline = default;
      _heap = null;
      var dest = MemoryMarshal.CreateSpan(ref _inline[0], InlineVersionBuffer.InlineCapacity);
      var written = Encoding.UTF8.GetBytes(version, dest);
      _length = (byte)written;
      dest[written] = 0;
    }
    else
    {
      _length = HeapSentinel;
      var buffer = new byte[maxByteCount + 1];
      var written = Encoding.UTF8.GetBytes(version, buffer);
      buffer[written] = 0;
      _heap = buffer;
      _inline = default;
    }
  }

  /// <summary>Gets the raw UTF-8 bytes of this version without the trailing null terminator.</summary>
  public ReadOnlySpan<byte> Utf8Span
  {
    get
    {
      return _length switch
      {
        0 => default,
        HeapSentinel => _heap is not null ? _heap.AsSpan(0, _heap.Length - 1) : default,
        _ => MemoryMarshal.CreateReadOnlySpan(in _inline[0], _length)
      };
    }
  }

  private static ReadOnlySpan<byte> EmptyNullTerminated => "\0"u8;

  private ReadOnlySpan<byte> NullTerminatedSpan
  {
    get
    {
      return _length switch
      {
        0 => EmptyNullTerminated,
        HeapSentinel => _heap is not null ? _heap.AsSpan() : EmptyNullTerminated,
        _ => MemoryMarshal.CreateReadOnlySpan(in _inline[0], _length + 1)
      };
    }
  }

  /// <summary>
  /// Orders this version against <paramref name="other" /> the way <c>alpm_pkg_vercmp</c> does: negative when this
  /// one is older, positive when it is newer, and 0 when libalpm reports them as the same version.
  /// </summary>
  public unsafe int CompareTo(PackageVersion other)
  {
    var s1 = NullTerminatedSpan;
    var s2 = other.NullTerminatedSpan;

    switch (s1.Length)
    {
      case <= 1 when s2.Length <= 1:
        return 0;
      case <= 1:
        return -1;
    }

    if (s2.Length <= 1) return 1;

    fixed (byte* p1 = s1, p2 = s2)
    {
      return NativeMethods.alpm_pkg_vercmp(p1, p2);
    }
  }

  /// <inheritdoc />
  public int CompareTo(object? obj)
  {
    return obj switch
    {
      null => 1,
      PackageVersion other => CompareTo(other),
      _ => throw new ArgumentException("Object must be of type PackageVersion.", nameof(obj))
    };
  }

  /// <summary>Whether <paramref name="other" /> carries the same version text as this instance.</summary>
  public bool Equals(PackageVersion other) => Utf8Span.SequenceEqual(other.Utf8Span);

  /// <inheritdoc />
  public override bool Equals(object? obj) => obj is PackageVersion other && Equals(other);

  /// <inheritdoc />
  public override int GetHashCode()
  {
    var hash = new HashCode();
    hash.AddBytes(Utf8Span);
    return hash.ToHashCode();
  }

  /// <summary>Whether the two operands carry the same version text.</summary>
  public static bool operator ==(PackageVersion left, PackageVersion right) => left.Equals(right);

  /// <summary>Whether the two operands carry different version text.</summary>
  public static bool operator !=(PackageVersion left, PackageVersion right) => !left.Equals(right);

  /// <inheritdoc />
  public override string ToString() => Utf8Span.IsEmpty ? string.Empty : Encoding.UTF8.GetString(Utf8Span);

  /// <inheritdoc />
  public bool TryFormat(Span<byte> utf8Destination, out int bytesWritten, ReadOnlySpan<char> format,
    IFormatProvider? provider)
  {
    var span = Utf8Span;
    if (span.TryCopyTo(utf8Destination))
    {
      bytesWritten = span.Length;
      return true;
    }

    bytesWritten = 0;
    return false;
  }

  /// <inheritdoc />
  public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format,
    IFormatProvider? provider)
  {
    var span = Utf8Span;
    if (span.IsEmpty)
    {
      charsWritten = 0;
      return true;
    }

    var status = System.Text.Unicode.Utf8.ToUtf16(span, destination, out _, out charsWritten);
    return status == OperationStatus.Done;
  }

  string IFormattable.ToString(string? format, IFormatProvider? formatProvider) => ToString();
}
