using Pacpar.Alpm.Bindings;

namespace Pacpar.Alpm;

/// <summary>A read-only forward stream over a package's changelog, backed by libalpm's changelog read routine.</summary>
/// <remarks>
/// The stream holds the archive handle returned by <c>alpm_pkg_changelog_open</c> and releases it in
/// <see cref="Dispose(bool)" />. There is deliberately no finalizer: libalpm writes the package handle's error state on
/// close, so a finalizer that runs after the session released the package would be a use-after-free; see the lifetime
/// design document. Callers must dispose the stream.
/// </remarks>
internal sealed unsafe class PackageChangelogStream : Stream
{
  private readonly PackageBase _package;
  private void* _fp;
  private bool _disposed;

  internal PackageChangelogStream(PackageBase package, void* fp)
  {
    _package = package;
    _fp = fp;
  }

  public override bool CanRead => !_disposed;
  public override bool CanSeek => false;
  public override bool CanWrite => false;
  public override long Length => throw new NotSupportedException();
  public override long Position
  {
    get => throw new NotSupportedException();
    set => throw new NotSupportedException();
  }

  public override void Flush() { }

  public override int Read(byte[] buffer, int offset, int count)
  {
    ObjectDisposedException.ThrowIf(_disposed, this);
    ArgumentNullException.ThrowIfNull(buffer);
    ArgumentOutOfRangeException.ThrowIfNegative(offset);
    ArgumentOutOfRangeException.ThrowIfNegative(count);
    if (offset > buffer.Length || count > buffer.Length - offset)
    {
      throw new ArgumentException("Offset and count exceed buffer length.");
    }
    if (count == 0) return 0;

    return Read(buffer.AsSpan(offset, count));
  }

  public override int Read(Span<byte> buffer)
  {
    ObjectDisposedException.ThrowIf(_disposed, this);
    if (buffer.Length == 0) return 0;

    fixed (byte* ptr = buffer)
    {
      var readBytes = NativeMethods.alpm_pkg_changelog_read(ptr, (nuint)buffer.Length, _package.BackingStruct, _fp);
      GC.KeepAlive(_package);
      return (int)readBytes;
    }
  }

  public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
  public override void SetLength(long value) => throw new NotSupportedException();
  public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

  protected override void Dispose(bool disposing)
  {
    if (!_disposed)
    {
      if (_fp != null)
      {
        NativeMethods.alpm_pkg_changelog_close(_package.BackingStruct, _fp);
        _fp = null;
      }
      _disposed = true;
    }
    base.Dispose(disposing);
  }
}
